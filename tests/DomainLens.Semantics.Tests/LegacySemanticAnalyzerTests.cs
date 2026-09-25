using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DomainLens.Core;
using DomainLens.Scanner;
using DomainLens.Semantics;

namespace DomainLens.Semantics.Tests;

public sealed class LegacySemanticAnalyzerTests
{
    [Fact]
    public async Task Net472_compilation_binds_framework_and_local_symbols_without_hiding_missing_types()
    {
        using var fixture = TemporaryFixture.Copy();
        var document = await ScanAsync(fixture.Path);

        var result = await new LegacySemanticAnalyzer().AnalyzeAsync(fixture.Path, document);

        Assert.Equal(
            SemanticCompilationScope.RepositoryManifestCSharpSources,
            result.CompilationScope);
        Assert.Equal(ResolutionQuality.Partial, result.CompilationResolutionQuality);

        var serviceContract = Assert.Single(result.Observations, observation =>
            observation.Kind == SemanticObservationKind.AttributeType &&
            observation.SourceExpression == "ServiceContract");
        Assert.Equal(ResolutionQuality.Exact, serviceContract.Quality);
        Assert.Equal("System.ServiceModel.ServiceContractAttribute", serviceContract.ResolvedSymbol);
        Assert.Equal("System.ServiceModel", serviceContract.ResolvedAssembly);

        Assert.Contains(result.Observations, observation =>
            observation.Kind == SemanticObservationKind.DeclaredType &&
            observation.ResolvedSymbol == "Legacy.Semantic.CustomerService" &&
            observation.Quality == ResolutionQuality.Partial);
        Assert.Contains(result.Observations, observation =>
            observation.Kind == SemanticObservationKind.BaseType &&
            observation.SourceExpression == "ServiceBase" &&
            observation.ResolvedSymbol == "Legacy.Semantic.ServiceBase" &&
            observation.Quality == ResolutionQuality.Partial);
        Assert.Contains(result.Observations, observation =>
            observation.Kind == SemanticObservationKind.ImplementedInterface &&
            observation.SourceExpression == "ICustomerService" &&
            observation.ResolvedSymbol == "Legacy.Semantic.ICustomerService" &&
            observation.Quality == ResolutionQuality.Partial);
        Assert.True(result.Observations.Any(observation =>
            observation.Kind == SemanticObservationKind.Invocation &&
            observation.SourceExpression == "_repository.Get(id)" &&
            observation.ResolvedSymbol is not null &&
            observation.ResolvedSymbol.Contains("Legacy.Semantic.IRepository.Get", StringComparison.Ordinal) &&
            observation.Quality == ResolutionQuality.Partial),
            string.Join(Environment.NewLine, result.Observations
                .Where(observation => observation.Kind == SemanticObservationKind.Invocation)
                .Select(observation => $"{observation.SourceExpression} => {observation.ResolvedSymbol} ({observation.Quality})")));
        Assert.True(result.Observations.Any(observation =>
            observation.Kind == SemanticObservationKind.MemberAccess &&
            observation.SourceExpression == "_repository.Save" &&
            observation.ResolvedSymbol is not null &&
            observation.ResolvedSymbol.Contains("Legacy.Semantic.IRepository.Save", StringComparison.Ordinal) &&
            observation.Quality == ResolutionQuality.Partial),
            string.Join(Environment.NewLine, result.Observations
                .Where(observation => observation.Kind == SemanticObservationKind.MemberAccess)
                .Select(observation => $"{observation.SourceExpression} => {observation.ResolvedSymbol} ({observation.Quality})")));

        var missing = Assert.Single(result.Observations, observation =>
            observation.Kind == SemanticObservationKind.ImplementedInterface &&
            observation.SourceExpression == "ILegacyDependency");
        Assert.Equal(ResolutionQuality.Unresolved, missing.Quality);
        Assert.Null(missing.ResolvedSymbol);
        Assert.DoesNotContain(result.Observations, observation =>
            observation.Kind == SemanticObservationKind.Invocation &&
            observation.SourceExpression == "nameof(GetCustomer)");
        Assert.DoesNotContain(result.Observations, observation =>
            observation.Kind == SemanticObservationKind.BaseType &&
            observation.SourceExpression == "MissingFirstPosition");
        Assert.Contains(result.Observations, observation =>
            observation.Kind == SemanticObservationKind.ImplementedInterface &&
            observation.SourceExpression == "IMissingStructContract" &&
            observation.Quality == ResolutionQuality.Unresolved);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == SemanticDiagnosticCode.CompilationError &&
            diagnostic.CompilerDiagnosticId == "CS0246" &&
            diagnostic.RelativePath == "CustomerService.cs");
    }

    [Fact]
    public async Task Metadata_inputs_are_fixed_tool_owned_reference_identities_and_repository_build_is_inert()
    {
        using var fixture = TemporaryFixture.Copy();
        await File.WriteAllBytesAsync(
            System.IO.Path.Combine(fixture.Path, "RepositoryOwned.dll"),
            Encoding.UTF8.GetBytes("not a trusted compiler input"));
        var document = await ScanAsync(fixture.Path);

        var result = await new LegacySemanticAnalyzer().AnalyzeAsync(fixture.Path, document);

        Assert.Equal("microsoft.netframework.referenceassemblies.net472/1.0.3", result.ReferenceSetId);
        Assert.NotEmpty(result.MetadataReferences);
        Assert.Equal(TrustedNet472ReferenceCatalog.GetReferenceDescriptors(), result.MetadataReferences);
        Assert.Contains(result.MetadataReferences, reference => reference.AssemblyName == "mscorlib");
        Assert.Contains(result.MetadataReferences, reference => reference.AssemblyName == "System.ServiceModel");
        Assert.All(result.MetadataReferences, reference =>
        {
            Assert.False(System.IO.Path.IsPathRooted(reference.RelativePath));
            Assert.DoesNotContain("..", reference.RelativePath, StringComparison.Ordinal);
            Assert.DoesNotContain(fixture.Path, reference.RelativePath, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith(".dll", reference.RelativePath, StringComparison.OrdinalIgnoreCase);
        });
        Assert.DoesNotContain(result.MetadataReferences, reference =>
            reference.RelativePath.Contains("RepositoryOwned", StringComparison.OrdinalIgnoreCase));
        var serialized = JsonSerializer.Serialize(result);
        Assert.DoesNotContain(fixture.Path, serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(AppContext.BaseDirectory, serialized, StringComparison.OrdinalIgnoreCase);
        AssertRepositoryRemainsInert(fixture.Path);
    }

    [Fact]
    public void Pinned_reference_catalog_rejects_extra_missing_and_corrupt_dlls()
    {
        var trustedCatalog = TrustedNet472ReferenceCatalog.GetSnapshot();
        Assert.True(trustedCatalog.IsComplete);

        var copy = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "domainlens-reference-catalog-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(copy);
        try
        {
            foreach (var sourcePath in Directory.EnumerateFiles(
                         trustedCatalog.RootPath,
                         "*.dll",
                         SearchOption.AllDirectories))
            {
                var relativePath = System.IO.Path.GetRelativePath(trustedCatalog.RootPath, sourcePath);
                var targetPath = System.IO.Path.Combine(copy, relativePath);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(targetPath)!);
                File.Copy(sourcePath, targetPath);
            }

            var mscorlib = System.IO.Path.Combine(copy, "mscorlib.dll");
            var extra = System.IO.Path.Combine(copy, "Stale.Repository.Payload.dll");
            File.Copy(mscorlib, extra);
            Assert.False(TrustedNet472ReferenceCatalog.LoadCatalogFromRoot(copy).IsComplete);
            File.Delete(extra);

            var serviceModel = System.IO.Path.Combine(copy, "System.ServiceModel.dll");
            File.Delete(serviceModel);
            Assert.False(TrustedNet472ReferenceCatalog.LoadCatalogFromRoot(copy).IsComplete);
            File.Copy(
                System.IO.Path.Combine(trustedCatalog.RootPath, "System.ServiceModel.dll"),
                serviceModel);

            File.WriteAllBytes(mscorlib, Encoding.UTF8.GetBytes("corrupt framework reference"));
            Assert.False(TrustedNet472ReferenceCatalog.LoadCatalogFromRoot(copy).IsComplete);
        }
        finally
        {
            if (Directory.Exists(copy))
            {
                Directory.Delete(copy, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Repository_wide_synthetic_bindings_are_explicitly_partial()
    {
        using var fixture = TemporaryFixture.Copy();
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(fixture.Path, "UnreferencedProjectType.cs"),
            "namespace Missing.External { public interface ILegacyDependency { } }");
        var document = await ScanAsync(fixture.Path);

        var result = await new LegacySemanticAnalyzer().AnalyzeAsync(fixture.Path, document);

        var crossProjectCandidate = Assert.Single(result.Observations, observation =>
            observation.Kind == SemanticObservationKind.ImplementedInterface &&
            observation.SourceExpression == "ILegacyDependency");
        Assert.Equal("Missing.External.ILegacyDependency", crossProjectCandidate.ResolvedSymbol);
        Assert.Equal(ResolutionQuality.Partial, crossProjectCandidate.Quality);
        Assert.Contains("synthetic compilation", crossProjectCandidate.Details, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Observations, observation =>
            observation.ResolvedSymbol == "Missing.External.ILegacyDependency" &&
            observation.Quality == ResolutionQuality.Exact);
        Assert.Equal(ResolutionQuality.Partial, result.CompilationResolutionQuality);
        AssertRepositoryRemainsInert(fixture.Path);
    }

    [Fact]
    public async Task Only_manifest_sources_with_their_captured_hash_are_analyzed()
    {
        using var fixture = TemporaryFixture.Copy();
        var document = await ScanAsync(fixture.Path);
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(fixture.Path, "Unmanifested.cs"),
            "namespace Untrusted { public sealed class NotInSnapshot { } }");

        var first = await new LegacySemanticAnalyzer().AnalyzeAsync(fixture.Path, document);

        Assert.DoesNotContain(first.Observations, observation =>
            observation.RelativePath == "Unmanifested.cs" ||
            observation.ResolvedSymbol?.Contains("NotInSnapshot", StringComparison.Ordinal) == true);

        var sourcePath = System.IO.Path.Combine(fixture.Path, "CustomerService.cs");
        var source = await File.ReadAllTextAsync(sourcePath);
        await File.WriteAllTextAsync(sourcePath, source.Replace("CustomerService", "XustomerService"));

        var second = await new LegacySemanticAnalyzer().AnalyzeAsync(fixture.Path, document);

        Assert.Contains(second.Diagnostics, diagnostic =>
            (diagnostic.Code is SemanticDiagnosticCode.ManifestHashMismatch or
                SemanticDiagnosticCode.ManifestLengthMismatch) &&
            diagnostic.RelativePath == "CustomerService.cs");
        Assert.DoesNotContain(second.Observations, observation =>
            observation.RelativePath == "CustomerService.cs");
        AssertRepositoryRemainsInert(fixture.Path);
    }

    [Fact]
    public async Task Source_larger_than_manifest_expectation_is_rejected_as_a_length_mismatch()
    {
        using var fixture = TemporaryFixture.Copy();
        var document = await ScanAsync(fixture.Path);
        var sourcePath = System.IO.Path.Combine(fixture.Path, "CustomerService.cs");
        var original = await File.ReadAllBytesAsync(sourcePath);
        await File.WriteAllBytesAsync(sourcePath, original.Concat(new byte[] { (byte)' ' }).ToArray());

        var result = await new LegacySemanticAnalyzer().AnalyzeAsync(fixture.Path, document);

        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == SemanticDiagnosticCode.ManifestLengthMismatch &&
            diagnostic.RelativePath == "CustomerService.cs");
        Assert.DoesNotContain(result.Observations, observation =>
            observation.RelativePath == "CustomerService.cs");
    }

    [Fact]
    public async Task Source_shorter_than_manifest_expectation_is_rejected_as_a_length_mismatch()
    {
        using var fixture = TemporaryFixture.Copy();
        var document = await ScanAsync(fixture.Path);
        var sourcePath = System.IO.Path.Combine(fixture.Path, "CustomerService.cs");
        var original = await File.ReadAllBytesAsync(sourcePath);
        await File.WriteAllBytesAsync(sourcePath, original[..^1]);

        var result = await new LegacySemanticAnalyzer().AnalyzeAsync(fixture.Path, document);

        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == SemanticDiagnosticCode.ManifestLengthMismatch &&
            diagnostic.RelativePath == "CustomerService.cs");
        Assert.DoesNotContain(result.Observations, observation =>
            observation.RelativePath == "CustomerService.cs");
    }

    [Fact]
    public async Task Same_length_source_change_is_rejected_as_a_hash_mismatch()
    {
        using var fixture = TemporaryFixture.Copy();
        var document = await ScanAsync(fixture.Path);
        var sourcePath = System.IO.Path.Combine(fixture.Path, "CustomerService.cs");
        var original = await File.ReadAllTextAsync(sourcePath);
        var changed = original.Replace("CustomerService", "XustomerService", StringComparison.Ordinal);
        Assert.NotEqual(original, changed);
        Assert.Equal(Encoding.UTF8.GetByteCount(original), Encoding.UTF8.GetByteCount(changed));
        await File.WriteAllTextAsync(sourcePath, changed, new UTF8Encoding(false));

        var result = await new LegacySemanticAnalyzer().AnalyzeAsync(fixture.Path, document);

        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == SemanticDiagnosticCode.ManifestHashMismatch &&
            diagnostic.RelativePath == "CustomerService.cs");
        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Code == SemanticDiagnosticCode.ManifestLengthMismatch &&
            diagnostic.RelativePath == "CustomerService.cs");
        Assert.DoesNotContain(result.Observations, observation =>
            observation.RelativePath == "CustomerService.cs");
    }

    [Fact]
    public async Task Obvious_length_drift_is_rejected_without_consuming_source_bytes()
    {
        var expected = Encoding.UTF8.GetBytes("internal sealed class Expected { }");
        var enlarged = expected.Concat(new byte[1024 * 1024]).ToArray();
        await using var source = new TrackingReadStream(enlarged, canSeek: true);
        var entry = new ManifestEntry(
            "Expected.cs",
            CanonicalIdentity.Sha256Hex(expected),
            expected.LongLength);

        var result = await LegacySemanticAnalyzer.ReadBoundedSourceAsync(
            source,
            entry,
            CancellationToken.None);

        Assert.Equal(ManifestSourceReadStatus.LengthMismatch, result.Status);
        Assert.Null(result.Bytes);
        Assert.Equal(0, source.BytesRead);
        Assert.Equal(0, source.ReadCallCount);
    }

    [Fact]
    public async Task Growth_during_read_is_rejected_without_consuming_beyond_the_captured_bound()
    {
        var expected = Encoding.UTF8.GetBytes("internal sealed class Expected { }");
        var enlarged = expected.Concat(new byte[1024 * 1024]).ToArray();
        await using var source = new TrackingReadStream(
            enlarged,
            canSeek: true,
            initialReportedLength: expected.LongLength);
        var entry = new ManifestEntry(
            "Expected.cs",
            CanonicalIdentity.Sha256Hex(expected),
            expected.LongLength);

        var result = await LegacySemanticAnalyzer.ReadBoundedSourceAsync(
            source,
            entry,
            CancellationToken.None);

        Assert.Equal(ManifestSourceReadStatus.LengthMismatch, result.Status);
        Assert.Null(result.Bytes);
        Assert.Equal(expected.Length, source.BytesRead);
        Assert.Equal(expected.Length, source.LastRequestedCount);
        Assert.Equal(1, source.ReadCallCount);
    }

    [Fact]
    public async Task Cancellation_during_bounded_source_read_is_propagated()
    {
        var content = Encoding.UTF8.GetBytes(new string('a', 256));
        using var cancellation = new CancellationTokenSource();
        await using var source = new TrackingReadStream(
            content,
            canSeek: true,
            maximumBytesPerRead: 1,
            afterFirstRead: cancellation.Cancel);
        var entry = new ManifestEntry(
            "Cancelled.cs",
            CanonicalIdentity.Sha256Hex(content),
            content.LongLength);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            LegacySemanticAnalyzer.ReadBoundedSourceAsync(source, entry, cancellation.Token));

        Assert.Equal(1, source.BytesRead);
    }

    [Fact]
    public async Task Valid_manifest_source_still_analyzes_after_bounded_verification()
    {
        using var fixture = TemporaryFixture.Copy();
        var document = await ScanAsync(fixture.Path);

        var result = await new LegacySemanticAnalyzer().AnalyzeAsync(fixture.Path, document);

        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Code is SemanticDiagnosticCode.ManifestLengthMismatch or
                SemanticDiagnosticCode.ManifestHashMismatch or
                SemanticDiagnosticCode.SourceReadFailed);
        Assert.Contains(result.Observations, observation =>
            observation.Kind == SemanticObservationKind.DeclaredType &&
            observation.RelativePath == "CustomerService.cs" &&
            observation.ResolvedSymbol == "Legacy.Semantic.CustomerService");
        AssertRepositoryRemainsInert(fixture.Path);
    }

    [Fact]
    public async Task Pre_cancelled_analysis_stops_before_source_or_compiler_work()
    {
        using var fixture = TemporaryFixture.Copy();
        var document = await ScanAsync(fixture.Path);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new LegacySemanticAnalyzer().AnalyzeAsync(fixture.Path, document, cancellation.Token));

        AssertRepositoryRemainsInert(fixture.Path);
    }

    [Fact]
    public async Task Strict_json_round_trip_is_deterministic_camel_case_and_path_free()
    {
        using var fixture = TemporaryFixture.Copy();
        var document = await ScanAsync(fixture.Path);
        var result = await new LegacySemanticAnalyzer().AnalyzeAsync(fixture.Path, document);

        var validation = LegacySemanticAnalysisValidator.Validate(result, document);
        var json = LegacySemanticAnalysisJson.Serialize(result, document, indented: false);
        var integrationJson = LegacySemanticAnalysisJson.Serialize(result, indented: false);
        var restored = LegacySemanticAnalysisJson.Deserialize(json, document);
        var integrationRestored = LegacySemanticAnalysisJson.Deserialize(integrationJson);

        Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Issues));
        Assert.Equal(json, integrationJson);
        Assert.Equal(json, LegacySemanticAnalysisJson.Serialize(restored, document, indented: false));
        Assert.Equal(integrationJson, LegacySemanticAnalysisJson.Serialize(integrationRestored, indented: false));
        Assert.Contains("\"observations\"", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"declaredType\"", json, StringComparison.Ordinal);
        Assert.Contains(
            "\"compilationScope\":\"repositoryManifestCSharpSources\"",
            json,
            StringComparison.Ordinal);
        Assert.Contains("\"compilationResolutionQuality\":\"partial\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Observations\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Path, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(AppContext.BaseDirectory, json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Validator_rejects_forged_reference_descriptors_and_resolved_assemblies()
    {
        using var fixture = TemporaryFixture.Copy();
        var document = await ScanAsync(fixture.Path);
        var result = await new LegacySemanticAnalyzer().AnalyzeAsync(fixture.Path, document);

        var forgedReferences = result with
        {
            MetadataReferences = result.MetadataReferences
                .Append(new TrustedMetadataReference("Repository.Payload", "Repository.Payload.dll"))
                .ToArray()
        };
        var referenceValidation = LegacySemanticAnalysisValidator.Validate(forgedReferences, document);
        Assert.False(referenceValidation.IsValid);
        Assert.Contains(referenceValidation.Issues, issue => issue.Code == "reference.catalog.unknown");
        Assert.Contains(referenceValidation.Issues, issue => issue.Code == "references.catalog.count");
        Assert.Throws<InvalidDataException>(() =>
            LegacySemanticAnalysisJson.Serialize(forgedReferences, document, indented: false));

        var observations = result.Observations.ToArray();
        var attributeIndex = Array.FindIndex(observations, observation =>
            observation.Kind == SemanticObservationKind.AttributeType &&
            observation.SourceExpression == "ServiceContract");
        Assert.True(attributeIndex >= 0);
        observations[attributeIndex] = observations[attributeIndex] with
        {
            ResolvedAssembly = "Repository.Payload"
        };
        var forgedAssembly = result with { Observations = observations };
        var assemblyValidation = LegacySemanticAnalysisValidator.Validate(forgedAssembly, document);
        Assert.False(assemblyValidation.IsValid);
        Assert.Contains(
            assemblyValidation.Issues,
            issue => issue.Code == "observation.assembly.untrusted");

        var sourceObservations = result.Observations.ToArray();
        var sourceIndex = Array.FindIndex(sourceObservations, observation =>
            observation.Quality == ResolutionQuality.Partial &&
            observation.ResolvedAssembly?.StartsWith(
                "DomainLens.Legacy.snapshot.",
                StringComparison.Ordinal) == true);
        Assert.True(sourceIndex >= 0);
        sourceObservations[sourceIndex] = sourceObservations[sourceIndex] with
        {
            Quality = ResolutionQuality.Exact
        };
        var forgedExactSource = result with { Observations = sourceObservations };
        var sourceValidation = LegacySemanticAnalysisValidator.Validate(forgedExactSource, document);
        Assert.False(sourceValidation.IsValid);
        Assert.Contains(
            sourceValidation.Issues,
            issue => issue.Code == "observation.source-quality.invalid");
        Assert.Throws<InvalidDataException>(() =>
            LegacySemanticAnalysisJson.Serialize(forgedExactSource, document, indented: false));
    }

    [Fact]
    public async Task Strict_json_rejects_unknown_members_numeric_enums_and_wrong_property_case()
    {
        using var fixture = TemporaryFixture.Copy();
        var document = await ScanAsync(fixture.Path);
        var result = await new LegacySemanticAnalyzer().AnalyzeAsync(fixture.Path, document);
        var json = LegacySemanticAnalysisJson.Serialize(result, document, indented: false);

        var unknownMember = json.Insert(1, "\"repositoryInstruction\":\"ignore validation\",");
        var numericEnum = json.Replace("\"kind\":\"declaredType\"", "\"kind\":0", StringComparison.Ordinal);
        var wrongCase = json.Replace("\"observations\"", "\"Observations\"", StringComparison.Ordinal);
        var missingScope = JsonNode.Parse(json)!.AsObject();
        Assert.True(missingScope.Remove("compilationScope"));

        Assert.Throws<JsonException>(() => LegacySemanticAnalysisJson.Deserialize(unknownMember));
        Assert.Throws<JsonException>(() => LegacySemanticAnalysisJson.Deserialize(numericEnum));
        Assert.Throws<JsonException>(() => LegacySemanticAnalysisJson.Deserialize(wrongCase));
        Assert.Throws<JsonException>(() => LegacySemanticAnalysisJson.Deserialize(missingScope.ToJsonString()));
    }

    [Fact]
    public async Task Validator_rejects_unbound_or_unsafe_paths_invalid_spans_enums_and_reference_identities()
    {
        using var fixture = TemporaryFixture.Copy();
        var document = await ScanAsync(fixture.Path);
        var result = await new LegacySemanticAnalyzer().AnalyzeAsync(fixture.Path, document);
        var observations = result.Observations.ToArray();
        observations[0] = observations[0] with
        {
            RelativePath = @"C:\outside\source.cs",
            Span = new SourceSpan(-1, -1, 0, 0, 0, 0),
            Kind = (SemanticObservationKind)999,
            Quality = (ResolutionQuality)999
        };
        observations[1] = observations[1] with { RelativePath = "Absent.cs" };
        var diagnostics = result.Diagnostics.ToArray();
        diagnostics[0] = diagnostics[0] with
        {
            RelativePath = "../CustomerService.cs",
            Code = (SemanticDiagnosticCode)999,
            Severity = (DiagnosticSeverity)999
        };
        var references = result.MetadataReferences.ToArray();
        references[0] = references[0] with
        {
            AssemblyName = "Repository.Payload",
            RelativePath = "../Repository.Payload.dll"
        };
        var invalid = result with
        {
            Observations = observations,
            Diagnostics = diagnostics,
            MetadataReferences = references,
            ReferenceSetId = @"C:\repository\payload",
            CompilationScope = (SemanticCompilationScope)999,
            CompilationResolutionQuality = ResolutionQuality.Exact
        };

        var validation = LegacySemanticAnalysisValidator.Validate(invalid, document);
        var codes = validation.Issues.Select(issue => issue.Code).ToHashSet(StringComparer.Ordinal);

        Assert.False(validation.IsValid);
        Assert.Contains("observation.path.invalid", codes);
        Assert.Contains("observation.path.unresolved", codes);
        Assert.Contains("observation.kind.invalid", codes);
        Assert.Contains("observation.quality.invalid", codes);
        Assert.Contains("diagnostic.path.invalid", codes);
        Assert.Contains("diagnostic.code.invalid", codes);
        Assert.Contains("diagnostic.severity.invalid", codes);
        Assert.Contains("reference.path.invalid", codes);
        Assert.Contains("references.set.invalid", codes);
        Assert.Contains("compilation.scope.invalid", codes);
        Assert.Contains("compilation.quality.invalid", codes);
    }

    [Fact]
    public async Task Manifest_aware_deserialization_rejects_a_structurally_safe_but_unbound_path()
    {
        using var fixture = TemporaryFixture.Copy();
        var document = await ScanAsync(fixture.Path);
        var result = await new LegacySemanticAnalyzer().AnalyzeAsync(fixture.Path, document);
        var json = LegacySemanticAnalysisJson.Serialize(result, document, indented: false);
        var unbound = json.Replace(
            "\"relativePath\":\"CustomerService.cs\"",
            "\"relativePath\":\"Absent.cs\"",
            StringComparison.Ordinal);

        Assert.NotNull(LegacySemanticAnalysisJson.Deserialize(unbound));
        Assert.Throws<InvalidDataException>(() => LegacySemanticAnalysisJson.Deserialize(unbound, document));
    }

    private static async Task<AnalysisDocument> ScanAsync(string repositoryPath)
    {
        var document = await new RepositoryScanner().AnalyzeAsync(new ScannerOptions(repositoryPath));
        Assert.NotEqual(AnalysisStatus.Failure, document.Status);
        return document;
    }

    private static void AssertRepositoryRemainsInert(string repositoryPath)
    {
        Assert.False(File.Exists(System.IO.Path.Combine(repositoryPath, "SHOULD_NOT_EXIST.marker")));
        Assert.False(Directory.Exists(System.IO.Path.Combine(repositoryPath, "bin")));
        Assert.False(Directory.Exists(System.IO.Path.Combine(repositoryPath, "obj")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(repositoryPath, "project.assets.json", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateDirectories(repositoryPath, "assets", SearchOption.AllDirectories));
    }

    private sealed class TrackingReadStream : Stream
    {
        private readonly byte[] _content;
        private readonly bool _canSeek;
        private readonly int _maximumBytesPerRead;
        private readonly Action? _afterFirstRead;
        private readonly long? _initialReportedLength;
        private int _position;

        public TrackingReadStream(
            byte[] content,
            bool canSeek,
            int maximumBytesPerRead = int.MaxValue,
            Action? afterFirstRead = null,
            long? initialReportedLength = null)
        {
            _content = content;
            _canSeek = canSeek;
            _maximumBytesPerRead = maximumBytesPerRead;
            _afterFirstRead = afterFirstRead;
            _initialReportedLength = initialReportedLength;
        }

        public int BytesRead { get; private set; }

        public int ReadCallCount { get; private set; }

        public int LastRequestedCount { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => _canSeek;

        public override bool CanWrite => false;

        public override long Length =>
            ReadCallCount == 0 && _initialReportedLength is not null
                ? _initialReportedLength.Value
                : _content.LongLength;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadCore(buffer.AsSpan(offset, count));

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(ReadCore(buffer.Span));
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int ReadCore(Span<byte> destination)
        {
            ReadCallCount++;
            LastRequestedCount = destination.Length;
            var count = Math.Min(
                Math.Min(destination.Length, _maximumBytesPerRead),
                _content.Length - _position);
            _content.AsSpan(_position, count).CopyTo(destination);
            _position += count;
            BytesRead += count;
            if (ReadCallCount == 1 && count > 0)
            {
                _afterFirstRead?.Invoke();
            }

            return count;
        }
    }

    private sealed class TemporaryFixture : IDisposable
    {
        private TemporaryFixture(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryFixture Copy()
        {
            var source = System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "SemanticLegacy");
            var destination = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "domainlens-semantic-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(destination);
            foreach (var sourcePath in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var relativePath = System.IO.Path.GetRelativePath(source, sourcePath);
                var targetPath = System.IO.Path.Combine(destination, relativePath);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(targetPath)!);
                File.Copy(sourcePath, targetPath);
            }

            return new TemporaryFixture(destination);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
