using System.Security.Cryptography;
using DomainLens.Cli;
using DomainLens.Core;
using DomainLens.Scanner;

namespace DomainLens.Scanner.Tests;

public sealed class ScannerAcceptanceTests
{
    [Fact]
    public async Task Sdk_solution_discovers_projects_frameworks_references_and_source_shapes()
    {
        var document = await ScanFixtureAsync("MultiProjectSdk");

        Assert.Equal(AnalysisStatus.Success, document.Status);
        Assert.Equal(AnalysisSchema.CurrentVersion, document.SchemaVersion);
        Assert.True(AnalysisJson.VerifyCanonicalHash(document));

        var projects = document.Nodes.Where(node => node.Kind == "Project").ToArray();
        Assert.Equal(2, projects.Length);
        Assert.Contains(projects, node =>
            node.QualifiedName == "src/Acme.Orders.Domain/Acme.Orders.Domain.csproj" &&
            node.Properties["targetFrameworks"] == "net8.0;netstandard2.0" &&
            node.Properties["isSdkStyle"] == "true" &&
            node.Properties["configurationSelectionResolution"] == "exact");
        Assert.Contains(projects, node =>
            node.QualifiedName == "src/Acme.Orders.Application/Acme.Orders.Application.csproj" &&
            node.Properties["targetFrameworks"] == "net8.0");

        Assert.Single(document.Nodes, node => node.Kind == "Solution");
        Assert.Contains(document.Edges, edge => edge.Kind == "ReferencesProject" && edge.ToNodeId is not null);
        Assert.Contains(document.Edges, edge => edge.Kind == "ReferencesAssembly" && edge.ToNodeId is not null);
        Assert.Contains(document.Edges, edge => edge.Kind == "ReferencesPackage" && edge.ToNodeId is not null);
        Assert.Contains(document.Nodes, node => node.Kind == "AssemblyReference" && node.Name == "System.Xml");
        Assert.Contains(document.Nodes, node =>
            node.Kind == "PackageReference" &&
            node.Name == "Newtonsoft.Json" &&
            node.Properties["version"] == "13.0.3");

        Assert.Contains(document.Nodes, node => node.Kind == "Namespace" && node.QualifiedName == "Acme.Orders.Domain");
        Assert.Contains(document.Nodes, node => node.Kind == "Class" && node.QualifiedName == "Acme.Orders.Domain.Order");
        Assert.Contains(document.Nodes, node => node.Kind == "Interface" && node.QualifiedName == "Acme.Orders.Domain.IAggregateRoot");
        Assert.Contains(document.Nodes, node => node.Kind == "Struct" && node.QualifiedName == "Acme.Orders.Domain.CustomerId");
        Assert.Contains(document.Nodes, node => node.Kind == "Enum" && node.QualifiedName == "Acme.Orders.Domain.OrderStatus");
        Assert.Contains(document.Nodes, node => node.Kind == "Record" && node.QualifiedName == "Acme.Orders.Domain.OrderNumber");
        Assert.Contains(document.Nodes, node => node.Kind == "RecordStruct" && node.QualifiedName == "Acme.Orders.Domain.Money");
    }

    [Fact]
    public async Task Legacy_project_is_read_declaratively_without_MSBuild_evaluation()
    {
        var document = await ScanFixtureAsync("LegacyFramework");

        Assert.Equal(AnalysisStatus.Success, document.Status);
        var project = Assert.Single(document.Nodes, node => node.Kind == "Project");
        Assert.Equal("false", project.Properties["isSdkStyle"]);
        Assert.Equal("v4.7.2", project.Properties["targetFrameworks"]);
        Assert.Equal("Legacy.Customer.Contracts", project.Properties["assemblyName"]);

        Assert.Contains(document.Nodes, node =>
            node.Kind == "Class" && node.QualifiedName == "Legacy.Customer.Contracts.CustomerRecord");
        Assert.Contains(document.Nodes, node => node.Kind == "AssemblyReference" && node.Name == "System");
        Assert.Contains(document.Edges, edge => edge.Kind == "ReferencesAssembly");
    }

    [Fact]
    public async Task Repository_without_solution_falls_back_to_project_discovery()
    {
        var document = await ScanFixtureAsync("ProjectOnly");

        Assert.Equal(AnalysisStatus.Success, document.Status);
        Assert.DoesNotContain(document.Nodes, node => node.Kind == "Solution");
        Assert.Single(document.Nodes, node => node.Kind == "Project");
        Assert.Contains(document.Nodes, node =>
            node.Kind == "Class" && node.QualifiedName == "Standalone.Inventory.InventoryItem");
    }

    [Fact]
    public async Task Missing_references_and_syntax_errors_produce_partial_evidence_not_failure()
    {
        var document = await ScanFixtureAsync("PartialAnalysis");

        Assert.Equal(AnalysisStatus.PartialSuccess, document.Status);
        Assert.NotEmpty(document.Nodes);
        Assert.Contains(document.Nodes, node =>
            node.Kind == "Class" && node.QualifiedName == "Partial.Sample.RecoverableService");
        Assert.Contains(document.Nodes, node =>
            node.Kind == "Class" && node.QualifiedName == "Partial.Sample.PartiallyParsedType");
        Assert.Contains(document.Nodes, node =>
            node.Kind == "Class" && node.QualifiedName == "RecoverableMembers");
        Assert.Contains(document.Nodes, node =>
            node.Kind == "Enum" && node.QualifiedName == "RecoverableEnum");
        Assert.Contains(document.Nodes, node =>
            node.Kind == "EnumMember" && node.QualifiedName == "RecoverableEnum.Kept");
        Assert.DoesNotContain(document.Nodes, node =>
            string.IsNullOrWhiteSpace(node.Name) || string.IsNullOrWhiteSpace(node.QualifiedName));
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "DL2011");
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "DL3002");
        Assert.Contains(document.Edges, edge =>
            edge.Kind == "ReferencesProject" &&
            edge.ToNodeId is null &&
            edge.UnresolvedTarget is not null &&
            edge.Resolution.Quality == ResolutionQuality.Unresolved);
        Assert.Contains(document.Edges, edge =>
            edge.Kind == "DeclaredTypeDependency" &&
            edge.ToNodeId is null &&
            edge.UnresolvedTarget == "MissingType");
        var branchBase = FindNode(document, "Class", "Partial.Sample.BranchBase");
        var branchDerived = FindNode(document, "Class", "Partial.Sample.BranchDerived");
        Assert.Contains(document.Edges, edge =>
            edge.Kind == "Inherits" &&
            edge.FromNodeId == branchDerived.NodeId &&
            edge.ToNodeId == branchBase.NodeId &&
            edge.Resolution.Quality == ResolutionQuality.Partial);
        Assert.True(AnalysisGraphValidator.Validate(document).IsValid);
    }

    [Fact]
    public async Task Inheritance_and_interface_implementation_edges_resolve_to_declared_types()
    {
        var document = await ScanFixtureAsync("MultiProjectSdk");
        var order = FindNode(document, "Class", "Acme.Orders.Domain.Order");
        var auditedEntity = FindNode(document, "Class", "Acme.Orders.Domain.AuditedEntity");
        var aggregateRoot = FindNode(document, "Interface", "Acme.Orders.Domain.IAggregateRoot");
        var identified = FindNode(document, "Interface", "Acme.Orders.Domain.IIdentified`1");

        Assert.Contains(document.Edges, edge =>
            edge.Kind == "Inherits" &&
            edge.FromNodeId == order.NodeId &&
            edge.ToNodeId == auditedEntity.NodeId &&
            edge.Resolution.Quality == ResolutionQuality.Exact);
        Assert.Contains(document.Edges, edge =>
            edge.Kind == "Implements" &&
            edge.FromNodeId == order.NodeId &&
            edge.ToNodeId == aggregateRoot.NodeId);
        Assert.Contains(document.Edges, edge =>
            edge.Kind == "Implements" &&
            edge.FromNodeId == order.NodeId &&
            edge.ToNodeId == identified.NodeId);
    }

    [Fact]
    public async Task Type_resolution_requires_a_literal_project_reference_even_when_a_using_matches()
    {
        var document = await ScanFixtureAsync("TypeResolutionScope");
        Assert.Equal(AnalysisStatus.PartialSuccess, document.Status);
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "DL2019");
        var customer = FindNode(document, "Class", "Shared.Contracts.Customer");
        var referencedProperty = FindNode(
            document,
            "Property",
            "Consumers.Referenced.ReferencedConsumer.Value:Customer?");
        var unrelatedProperty = FindNode(
            document,
            "Property",
            "Consumers.Unrelated.UnrelatedConsumer.Value:Customer?");
        var conditionalProperty = FindNode(
            document,
            "Property",
            "Consumers.Conditional.ConditionalConsumer.Value:Customer?");
        var noCompileProperty = FindNode(
            document,
            "Property",
            "Consumers.NoCompile.NoCompileConsumer.Value:Customer?");
        var aliasOnlyProperty = FindNode(
            document,
            "Property",
            "Consumers.AliasOnly.AliasOnlyConsumer.Value:Customer?");

        Assert.Contains(document.Edges, edge =>
            edge.Kind == "DeclaredTypeDependency" &&
            edge.FromNodeId == referencedProperty.NodeId &&
            edge.ToNodeId == customer.NodeId &&
            edge.Resolution.Quality == ResolutionQuality.Exact);
        Assert.Contains(document.Edges, edge =>
            edge.Kind == "DeclaredTypeDependency" &&
            edge.FromNodeId == unrelatedProperty.NodeId &&
            edge.ToNodeId is null &&
            edge.UnresolvedTarget == "Customer" &&
            edge.Resolution.Quality == ResolutionQuality.Unresolved);
        Assert.Contains(document.Edges, edge =>
            edge.Kind == "DeclaredTypeDependency" &&
            edge.FromNodeId == conditionalProperty.NodeId &&
            edge.ToNodeId is null &&
            edge.UnresolvedTarget == "Customer" &&
            edge.Resolution.Quality == ResolutionQuality.Unresolved);
        Assert.Contains(document.Edges, edge =>
            edge.Kind == "DeclaredTypeDependency" &&
            edge.FromNodeId == noCompileProperty.NodeId &&
            edge.ToNodeId is null &&
            edge.UnresolvedTarget == "Customer" &&
            edge.Resolution.Quality == ResolutionQuality.Unresolved);
        Assert.Contains(document.Edges, edge =>
            edge.Kind == "DeclaredTypeDependency" &&
            edge.FromNodeId == aliasOnlyProperty.NodeId &&
            edge.ToNodeId is null &&
            edge.UnresolvedTarget == "Customer" &&
            edge.Resolution.Quality == ResolutionQuality.Unresolved);

        var conditionalProject = FindNode(document, "Project", "Conditional/Conditional.csproj");
        Assert.Contains(document.Edges, edge =>
            edge.Kind == "ReferencesProject" &&
            edge.FromNodeId == conditionalProject.NodeId &&
            edge.ToNodeId is not null &&
            edge.Resolution.Quality == ResolutionQuality.Partial);
    }

    [Fact]
    public async Task Members_records_and_attributes_are_preserved_as_deterministic_evidence()
    {
        var document = await ScanFixtureAsync("MultiProjectSdk");
        var order = FindNode(document, "Class", "Acme.Orders.Domain.Order");

        Assert.Contains("DomainConcept", order.Attributes);
        Assert.Contains(document.Nodes, node =>
            node.Kind == "Constructor" &&
            node.ProjectId == order.ProjectId &&
            node.QualifiedName.StartsWith("Acme.Orders.Domain.Order..ctor(", StringComparison.Ordinal));
        Assert.Contains(document.Nodes, node =>
            node.Kind == "Property" && node.QualifiedName == "Acme.Orders.Domain.Order.Status:OrderStatus");
        Assert.Contains(document.Nodes, node =>
            node.Kind == "Field" && node.Name == "_charges");
        Assert.Contains(document.Nodes, node =>
            node.Kind == "Method" &&
            node.QualifiedName == "Acme.Orders.Domain.Order.AddCharge(Money)" &&
            node.Attributes.Contains("DomainConcept"));

        Assert.Contains(document.Nodes, node =>
            node.Kind == "Record" && node.QualifiedName == "Acme.Orders.Domain.OrderNumber");
        Assert.Contains(document.Nodes, node =>
            node.Kind == "RecordStruct" && node.QualifiedName == "Acme.Orders.Domain.Money");
        Assert.Contains(document.Nodes, node =>
            node.Kind == "Constructor" &&
            node.QualifiedName == "Acme.Orders.Domain.Money..ctor(decimal,string)" &&
            node.Properties["primary"] == "true");
    }

    [Fact]
    public async Task Generic_arity_and_constructor_kind_are_part_of_canonical_symbol_identity()
    {
        var document = await ScanFixtureAsync("ProjectOnly");
        var boxOfOne = FindNode(document, "Class", "Standalone.Inventory.Box`1");
        var boxOfTwo = FindNode(document, "Class", "Standalone.Inventory.Box`2");
        var one = FindNode(
            document,
            "Property",
            "Standalone.Inventory.GenericConsumer.One:Box<int>");
        var two = FindNode(
            document,
            "Property",
            "Standalone.Inventory.GenericConsumer.Two:Box<int, string>");

        Assert.NotEqual(boxOfOne.NodeId, boxOfTwo.NodeId);
        Assert.Contains(document.Edges, edge =>
            edge.Kind == "DeclaredTypeDependency" &&
            edge.FromNodeId == one.NodeId &&
            edge.ToNodeId == boxOfOne.NodeId &&
            edge.Resolution.Quality == ResolutionQuality.Exact);
        Assert.Contains(document.Edges, edge =>
            edge.Kind == "DeclaredTypeDependency" &&
            edge.FromNodeId == two.NodeId &&
            edge.ToNodeId == boxOfTwo.NodeId &&
            edge.Resolution.Quality == ResolutionQuality.Exact);

        var constructors = document.Nodes
            .Where(node => node.Kind == "Constructor" &&
                           node.QualifiedName.StartsWith(
                               "Standalone.Inventory.Initialization..",
                               StringComparison.Ordinal))
            .ToArray();
        Assert.Contains(constructors, node =>
            node.QualifiedName == "Standalone.Inventory.Initialization..ctor()");
        Assert.Contains(constructors, node =>
            node.QualifiedName == "Standalone.Inventory.Initialization..cctor()");
        Assert.Equal(2, constructors.Select(node => node.NodeId).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task Evidence_provenance_resolves_to_manifest_hash_and_exact_source_span()
    {
        var repository = FixturePath("ProjectOnly");
        var document = await new RepositoryScanner().AnalyzeAsync(new ScannerOptions(repository));
        var inventoryItem = FindNode(document, "Class", "Standalone.Inventory.InventoryItem");
        var evidenceById = document.Evidence.ToDictionary(item => item.EvidenceId, StringComparer.Ordinal);
        var manifestByPath = document.Snapshot.Manifest.ToDictionary(item => item.Path, StringComparer.Ordinal);

        Assert.NotEmpty(inventoryItem.EvidenceIds);
        foreach (var evidenceId in inventoryItem.EvidenceIds)
        {
            var evidence = evidenceById[evidenceId];
            var manifest = manifestByPath[evidence.RelativePath];
            var sourcePath = ResolveRepositoryPath(repository, evidence.RelativePath);
            var sourceBytes = await File.ReadAllBytesAsync(sourcePath);
            var sourceText = await File.ReadAllTextAsync(sourcePath);

            Assert.Equal(document.Snapshot.SnapshotId, evidence.SnapshotId);
            Assert.Equal(manifest.ContentHash, evidence.ContentHash);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant(), evidence.ContentHash);
            Assert.InRange(evidence.Span.StartOffset, 0, sourceText.Length);
            Assert.InRange(evidence.Span.StartOffset + evidence.Span.Length, 0, sourceText.Length);
            Assert.True(evidence.Span.StartLine >= 1);
            Assert.True(evidence.Span.StartColumn >= 1);
            Assert.False(string.IsNullOrWhiteSpace(evidence.Provenance.ExtractorId));
            Assert.False(string.IsNullOrWhiteSpace(evidence.Provenance.ExtractorVersion));
            Assert.False(string.IsNullOrWhiteSpace(evidence.Provenance.RuleId));

            var capturedText = sourceText.Substring(evidence.Span.StartOffset, evidence.Span.Length);
            Assert.Contains("InventoryItem", capturedText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Identical_repository_content_has_root_independent_snapshot_graph_and_JSON()
    {
        using var first = TemporaryDirectory.Create();
        using var second = TemporaryDirectory.Create();
        var firstRepository = first.CopyFixture("MultiProjectSdk", "first-root");
        var secondRepository = second.CopyFixture("MultiProjectSdk", "unrelated-root-name");
        var scanner = new RepositoryScanner();

        var firstDocument = await scanner.AnalyzeAsync(new ScannerOptions(firstRepository));
        var repeatedDocument = await scanner.AnalyzeAsync(new ScannerOptions(firstRepository));
        var secondDocument = await scanner.AnalyzeAsync(new ScannerOptions(secondRepository));

        Assert.Equal(firstDocument.Snapshot.SnapshotId, repeatedDocument.Snapshot.SnapshotId);
        Assert.Equal(firstDocument.Snapshot.SnapshotId, secondDocument.Snapshot.SnapshotId);
        Assert.Equal(firstDocument.CanonicalHash, repeatedDocument.CanonicalHash);
        Assert.Equal(firstDocument.CanonicalHash, secondDocument.CanonicalHash);
        Assert.Equal(AnalysisJson.Serialize(firstDocument, indented: false), AnalysisJson.Serialize(repeatedDocument, indented: false));
        Assert.Equal(AnalysisJson.Serialize(firstDocument, indented: false), AnalysisJson.Serialize(secondDocument, indented: false));
        Assert.DoesNotContain(firstRepository, AnalysisJson.Serialize(firstDocument), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(secondRepository, AnalysisJson.Serialize(secondDocument), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Repository_controlled_build_target_remains_inert()
    {
        var repository = FixturePath("MaliciousBuild");
        var marker = Path.Combine(repository, "SHOULD_NOT_EXIST.marker");
        Assert.False(File.Exists(marker), "The hostile fixture marker must not exist before analysis.");

        var document = await new RepositoryScanner().AnalyzeAsync(new ScannerOptions(repository));

        Assert.Equal(AnalysisStatus.PartialSuccess, document.Status);
        Assert.False(File.Exists(marker), "Scanning must not execute repository-controlled MSBuild targets.");
        Assert.Contains(document.Diagnostics, diagnostic =>
            diagnostic.Code == "DL2009" &&
            diagnostic.Severity == DiagnosticSeverity.Information &&
            diagnostic.Message.Contains("Exec", StringComparison.Ordinal) &&
            diagnostic.Message.Contains("UsingTask", StringComparison.Ordinal) &&
            diagnostic.Message.Contains("PreBuildEvent", StringComparison.Ordinal) &&
            diagnostic.Message.Contains("PostBuildEvent", StringComparison.Ordinal));
        Assert.Contains(document.Diagnostics, diagnostic =>
            diagnostic.Code == "DL2016" && diagnostic.Severity == DiagnosticSeverity.Warning);
        Assert.Contains(document.Diagnostics, diagnostic =>
            diagnostic.Code == "DL2022" && diagnostic.Severity == DiagnosticSeverity.Warning);
        Assert.Contains(document.Nodes, node =>
            node.Kind == "Class" && node.QualifiedName == "MaliciousBuildFixture.HarmlessSource");
    }

    [Fact]
    public async Task Unevaluated_repository_wide_source_configuration_is_explicitly_partial()
    {
        var document = await ScanFixtureAsync("MsBuildPartial");

        Assert.Equal(AnalysisStatus.PartialSuccess, document.Status);
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "DL2016");
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "DL2020");
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "DL2022");
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "DL2024");
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "DL2026");
        var project = FindNode(document, "Project", "MsBuildPartial.csproj");
        Assert.Equal("partial", project.Properties["sourceSelectionResolution"]);
        Assert.Equal("partial", project.Properties["dependencySelectionResolution"]);
        Assert.Equal("partial", project.Properties["configurationSelectionResolution"]);
        var projectEvidence = Assert.Single(document.Evidence, evidence =>
            project.EvidenceIds.Contains(evidence.EvidenceId, StringComparer.Ordinal) &&
            evidence.Provenance.RuleId == "project.declaration");
        Assert.Equal(ResolutionQuality.Partial, projectEvidence.Resolution.Quality);
        var observedType = FindNode(document, "Class", "MsBuild.Partial.ObservedType");
        Assert.Equal("partial", observedType.Properties["projectMembershipResolution"]);
        var repository = FindNode(document, "Repository", "Repository");
        Assert.Contains(document.Edges, edge =>
            edge.Kind == "Contains" &&
            edge.FromNodeId == repository.NodeId &&
            edge.ToNodeId == project.NodeId &&
            edge.Resolution.Quality == ResolutionQuality.Partial);
        var duplicateAssemblyDeclarations = document.Nodes
            .Where(node => node.Kind == "AssemblyReference" && node.Name == "Contoso.Shared")
            .ToArray();
        Assert.Equal(2, duplicateAssemblyDeclarations.Length);
        Assert.Equal(
            2,
            duplicateAssemblyDeclarations.Select(node => node.NodeId).Distinct(StringComparer.Ordinal).Count());
        Assert.All(duplicateAssemblyDeclarations, node =>
            Assert.Contains(document.Edges, edge =>
                edge.Kind == "ReferencesAssembly" &&
                edge.ToNodeId == node.NodeId &&
                edge.Resolution.Quality == ResolutionQuality.Partial));
    }

    [Fact]
    public async Task Graph_edges_and_evidence_references_satisfy_integrity_invariants()
    {
        var document = await ScanFixtureAsync("MultiProjectSdk");
        var validation = AnalysisGraphValidator.Validate(document);
        var nodeIds = document.Nodes.Select(node => node.NodeId).ToHashSet(StringComparer.Ordinal);
        var evidenceIds = document.Evidence.Select(item => item.EvidenceId).ToHashSet(StringComparer.Ordinal);

        Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Issues.Select(issue => $"{issue.Code}: {issue.Message}")));
        Assert.Equal(document.Nodes.Count, nodeIds.Count);
        Assert.Equal(document.Edges.Count, document.Edges.Select(edge => edge.EdgeId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(document.Evidence.Count, evidenceIds.Count);
        foreach (var node in document.Nodes)
        {
            Assert.All(node.EvidenceIds, evidenceId => Assert.Contains(evidenceId, evidenceIds));
            if (node.ProjectId is not null)
            {
                Assert.Contains(node.ProjectId, nodeIds);
            }
        }

        foreach (var edge in document.Edges)
        {
            Assert.Contains(edge.FromNodeId, nodeIds);
            if (edge.ToNodeId is not null)
            {
                Assert.Contains(edge.ToNodeId, nodeIds);
                Assert.Null(edge.UnresolvedTarget);
            }
            else
            {
                Assert.False(string.IsNullOrWhiteSpace(edge.UnresolvedTarget));
            }

            Assert.All(edge.EvidenceIds, evidenceId => Assert.Contains(evidenceId, evidenceIds));
        }
    }

    [Fact]
    public async Task Explicit_solution_selection_limits_the_analyzed_graph()
    {
        var repository = FixturePath("SolutionSelection");

        var document = await new RepositoryScanner().AnalyzeAsync(
            new ScannerOptions(repository, "Chosen.sln"));

        Assert.Equal(AnalysisStatus.Success, document.Status);
        var solution = Assert.Single(document.Nodes, node => node.Kind == "Solution");
        Assert.Equal("Chosen.sln", solution.QualifiedName);
        var project = Assert.Single(document.Nodes, node => node.Kind == "Project");
        Assert.Equal("Chosen/Chosen.csproj", project.QualifiedName);
        Assert.Contains(document.Nodes, node => node.QualifiedName == "Selection.Chosen.ChosenType");
        Assert.DoesNotContain(document.Nodes, node => node.QualifiedName == "Selection.Other.OtherType");

        var allSolutions = await new RepositoryScanner().AnalyzeAsync(new ScannerOptions(repository));
        Assert.Equal(AnalysisStatus.PartialSuccess, allSolutions.Status);
        Assert.Contains(allSolutions.Diagnostics, diagnostic => diagnostic.Code == "DL2010");
        Assert.Contains(allSolutions.Edges, edge =>
            edge.Kind == "Contains" &&
            edge.ToNodeId is null &&
            edge.UnresolvedTarget == "Missing/Missing.csproj" &&
            edge.Resolution.Quality == ResolutionQuality.Unresolved);

        var missingSelection = await new RepositoryScanner().AnalyzeAsync(
            new ScannerOptions(repository, "Does.Not.Exist.sln"));
        Assert.Equal(AnalysisStatus.Failure, missingSelection.Status);
        Assert.Contains(missingSelection.Diagnostics, diagnostic => diagnostic.Code == "DL0012");

        var wrongDirectorySelection = await new RepositoryScanner().AnalyzeAsync(
            new ScannerOptions(repository, "Wrong/Chosen.sln"));
        Assert.Equal(AnalysisStatus.Failure, wrongDirectorySelection.Status);
        Assert.Contains(wrongDirectorySelection.Diagnostics, diagnostic => diagnostic.Code == "DL0012");
    }

    [Fact]
    public async Task Missing_repository_root_is_a_fatal_but_serializable_result()
    {
        var missingRoot = Path.Combine(Path.GetTempPath(), $"domainlens-missing-{Guid.NewGuid():N}");

        var document = await new RepositoryScanner().AnalyzeAsync(new ScannerOptions(missingRoot));

        Assert.Equal(AnalysisStatus.Failure, document.Status);
        Assert.Empty(document.Nodes);
        Assert.Empty(document.Edges);
        Assert.Contains(document.Diagnostics, diagnostic =>
            diagnostic.Code == "DL0001" && diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.True(AnalysisJson.VerifyCanonicalHash(document));
        Assert.True(AnalysisGraphValidator.Validate(document).IsValid);
    }

    [Fact]
    public async Task Cli_scan_and_inspect_report_status_and_provenance()
    {
        using var temporary = TemporaryDirectory.Create();
        var repository = temporary.CopyFixture("ProjectOnly", "repository");
        var artifactPath = Path.Combine(temporary.Path, "artifacts", "analysis.json");
        using var scanOutput = new StringWriter();
        using var scanError = new StringWriter();

        var scanExitCode = await CliApplication.RunAsync(
            ["scan", "--repository", repository, "--output", artifactPath],
            scanOutput,
            scanError);

        Assert.Equal(CliApplication.SuccessExitCode, scanExitCode);
        Assert.True(File.Exists(artifactPath));
        Assert.Contains("Status: Success", scanOutput.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, scanError.ToString());

        var document = AnalysisJson.Deserialize(await File.ReadAllTextAsync(artifactPath));
        var inventoryItem = FindNode(document, "Class", "Standalone.Inventory.InventoryItem");
        using var inspectOutput = new StringWriter();
        using var inspectError = new StringWriter();

        var inspectExitCode = await CliApplication.RunAsync(
            ["inspect", "--analysis", artifactPath, "--symbol", inventoryItem.NodeId],
            inspectOutput,
            inspectError);

        Assert.Equal(CliApplication.SuccessExitCode, inspectExitCode);
        Assert.Equal(string.Empty, inspectError.ToString());
        Assert.Contains("Class: Standalone.Inventory.InventoryItem", inspectOutput.ToString(), StringComparison.Ordinal);
        Assert.Contains("content sha256:", inspectOutput.ToString(), StringComparison.Ordinal);
        Assert.Contains("extractor:", inspectOutput.ToString(), StringComparison.Ordinal);
        Assert.Contains("resolution:", inspectOutput.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cli_uses_distinct_partial_success_exit_code()
    {
        using var temporary = TemporaryDirectory.Create();
        var repository = temporary.CopyFixture("PartialAnalysis", "repository");
        var artifactPath = Path.Combine(temporary.Path, "partial-analysis.json");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(
            ["scan", "--repository", repository, "--output", artifactPath],
            output,
            error);

        Assert.Equal(CliApplication.PartialSuccessExitCode, exitCode);
        Assert.Contains("Status: PartialSuccess", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(AnalysisStatus.PartialSuccess, AnalysisJson.Deserialize(await File.ReadAllTextAsync(artifactPath)).Status);
    }

    [Fact]
    public async Task Cli_reports_invalid_options_as_usage_errors()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(
            ["scan", "--not-an-option", "value"],
            output,
            error);

        Assert.Equal(CliApplication.UsageExitCode, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains("Unknown option '--not-an-option'.", error.ToString(), StringComparison.Ordinal);
    }

    private static async Task<AnalysisDocument> ScanFixtureAsync(string fixtureName) =>
        await new RepositoryScanner().AnalyzeAsync(new ScannerOptions(FixturePath(fixtureName)));

    private static EvidenceNode FindNode(AnalysisDocument document, string kind, string qualifiedName) =>
        Assert.Single(document.Nodes, node =>
            node.Kind == kind && string.Equals(node.QualifiedName, qualifiedName, StringComparison.Ordinal));

    private static string FixturePath(string fixtureName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixtureName);
        Assert.True(Directory.Exists(path), $"Fixture '{fixtureName}' was not copied to '{path}'.");
        return path;
    }

    private static string ResolveRepositoryPath(string repository, string relativePath) =>
        Path.Combine(repository, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "DomainLens.Scanner.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public string CopyFixture(string fixtureName, string destinationName)
        {
            var source = FixturePath(fixtureName);
            var destination = System.IO.Path.Combine(Path, destinationName);
            CopyDirectory(source, destination);
            return destination;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup of a test-owned temporary directory.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup of a test-owned temporary directory.
            }
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            {
                var relative = System.IO.Path.GetRelativePath(source, directory);
                Directory.CreateDirectory(System.IO.Path.Combine(destination, relative));
            }

            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var relative = System.IO.Path.GetRelativePath(source, file);
                var target = System.IO.Path.Combine(destination, relative);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: false);
            }
        }
    }
}
