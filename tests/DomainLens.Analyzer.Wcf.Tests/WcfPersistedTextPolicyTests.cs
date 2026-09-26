using System.Buffers.Binary;
using System.Security.Cryptography;
using DomainLens.Core;
using DomainLens.Scanner;

namespace DomainLens.Analyzer.Wcf.Tests;

public sealed class WcfPersistedTextPolicyTests
{
    private const int MaximumPersistedCharacters = 1024;

    [Fact]
    public async Task Oversized_source_attribute_value_is_abbreviated_with_verifiable_metadata()
    {
        using var fixture = TemporaryWcfFixture.Copy("WcfBasic");
        var sourcePath = System.IO.Path.Combine(fixture.Path, "Contracts.cs");
        var source = await File.ReadAllTextAsync(sourcePath);
        var oversizedName = "CustomerContract-" + new string('s', 2048);
        await File.WriteAllTextAsync(
            sourcePath,
            source.Replace("CustomerContract", oversizedName, StringComparison.Ordinal));

        var document = await AnalyzeAsync(fixture.Path);

        var contract = Assert.Single(document.Nodes, node =>
            node.QualifiedName == "Fixtures.Wcf.Basic.ICustomerService");
        var persisted = contract.Properties[WcfVocabulary.Properties.ServiceContractName];
        AssertAbbreviation(persisted, oversizedName);
        var diagnostic = Assert.Single(document.Diagnostics, candidate =>
            candidate.Code == WcfVocabulary.Diagnostics.PersistedTextAbbreviated &&
            candidate.RelativePath == "Contracts.cs" &&
            candidate.Properties.GetValueOrDefault("field") ==
            $"node.properties.{WcfVocabulary.Properties.ServiceContractName}");
        AssertAbbreviationDiagnostic(diagnostic, oversizedName);
        Assert.Equal(AnalysisStatus.PartialSuccess, document.Status);
        Assert.DoesNotContain(oversizedName, AnalysisJson.Serialize(document, indented: false));
        AssertBoundedWcfOutput(document);
        AssertValid(document);
    }

    [Fact]
    public async Task Oversized_declarative_values_remain_distinct_bounded_and_deterministic()
    {
        using var fixture = TemporaryWcfFixture.Copy("WcfHostingConfig");
        var commonPrefix = "Fixtures.Wcf.Oversized." + new string('x', 1400);
        var firstContract = commonPrefix + ".IFirstContract";
        var secondContract = commonPrefix + ".ISecondContract";
        var firstName = commonPrefix + ".FirstEndpoint";
        var secondName = commonPrefix + ".SecondEndpoint";
        var firstAddress = "https://example.invalid/" + new string('a', 1500) + "/first";
        var secondAddress = "https://example.invalid/" + new string('a', 1500) + "/second";
        var service = commonPrefix + ".MissingService";
        var factory = commonPrefix + ".InertFactory";
        var extensionType = commonPrefix + ".InertExtension, Oversized.Assembly";

        await File.WriteAllTextAsync(
            System.IO.Path.Combine(fixture.Path, "Oversized.svc"),
            $"<%@ ServiceHost Language=\"C#\" Service=\"{service}\" Factory=\"{factory}\" %>");
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(fixture.Path, "Oversized.config"),
            $$"""
              <?xml version="1.0" encoding="utf-8"?>
              <configuration>
                <system.serviceModel>
                  <client>
                    <endpoint name="{{firstName}}" address="{{firstAddress}}" contract="{{firstContract}}" />
                    <endpoint name="{{secondName}}" address="{{secondAddress}}" contract="{{secondContract}}" />
                  </client>
                  <extensions>
                    <behaviorExtensions>
                      <add name="oversizedExtension" type="{{extensionType}}" />
                    </behaviorExtensions>
                  </extensions>
                </system.serviceModel>
              </configuration>
              """);

        var baseline = await new RepositoryScanner().AnalyzeAsync(new ScannerOptions(fixture.Path));
        Assert.NotEqual(AnalysisStatus.Failure, baseline.Status);
        var first = await new ClassicWcfAnalyzer().AnalyzeAsync(fixture.Path, baseline);
        var second = await new ClassicWcfAnalyzer().AnalyzeAsync(fixture.Path, baseline);

        var endpoints = first.Nodes
            .Where(node => node.Kind == WcfVocabulary.NodeKinds.Endpoint &&
                           NodeHasEvidenceFrom(first, node, "Oversized.config"))
            .OrderBy(node => node.NodeId, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(2, endpoints.Length);
        Assert.NotEqual(endpoints[0].NodeId, endpoints[1].NodeId);
        var persistedNames = endpoints
            .Select(node => node.Properties[WcfVocabulary.Properties.ConfigEndpointName])
            .ToArray();
        Assert.Contains(persistedNames, value => value.Contains(Sha256Utf16(firstName), StringComparison.Ordinal));
        Assert.Contains(persistedNames, value => value.Contains(Sha256Utf16(secondName), StringComparison.Ordinal));
        Assert.NotEqual(persistedNames[0], persistedNames[1]);
        Assert.Contains(endpoints, node =>
            node.Properties[WcfVocabulary.Properties.Address]
                .Contains(Sha256Utf16(firstAddress), StringComparison.Ordinal));
        Assert.Contains(endpoints, node =>
            node.Properties[WcfVocabulary.Properties.Address]
                .Contains(Sha256Utf16(secondAddress), StringComparison.Ordinal));

        var contractTargets = first.Edges
            .Where(edge => edge.Kind == WcfVocabulary.EdgeKinds.EndpointContract &&
                           edge.ToNodeId is null &&
                           edge.UnresolvedTarget is not null &&
                           (edge.UnresolvedTarget.Contains(Sha256Utf16(firstContract), StringComparison.Ordinal) ||
                            edge.UnresolvedTarget.Contains(Sha256Utf16(secondContract), StringComparison.Ordinal)))
            .Select(edge => edge.UnresolvedTarget!)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(2, contractTargets.Length);
        Assert.NotEqual(contractTargets[0], contractTargets[1]);
        AssertAbbreviation(
            Assert.Single(contractTargets, value =>
                value.Contains(Sha256Utf16(firstContract), StringComparison.Ordinal)),
            firstContract);
        AssertAbbreviation(
            Assert.Single(contractTargets, value =>
                value.Contains(Sha256Utf16(secondContract), StringComparison.Ordinal)),
            secondContract);

        var host = Assert.Single(first.Nodes, node =>
            node.Kind == WcfVocabulary.NodeKinds.HostingDeclaration &&
            NodeHasEvidenceFrom(first, node, "Oversized.svc"));
        AssertAbbreviation(host.Properties[WcfVocabulary.Properties.Service], service);
        AssertAbbreviation(host.Properties[WcfVocabulary.Properties.Factory], factory);
        var hostEdge = Assert.Single(first.Edges, edge =>
            edge.Kind == WcfVocabulary.EdgeKinds.HostsService &&
            edge.FromNodeId == host.NodeId);
        Assert.Null(hostEdge.ToNodeId);
        AssertAbbreviation(hostEdge.UnresolvedTarget!, service);

        var extension = Assert.Single(first.Nodes, node =>
            node.Kind == WcfVocabulary.NodeKinds.ExtensionDeclaration &&
            NodeHasEvidenceFrom(first, node, "Oversized.config"));
        AssertAbbreviation(
            extension.Properties[WcfVocabulary.Properties.ExtensionType],
            extensionType);
        var extensionDiagnostic = Assert.Single(first.Diagnostics, candidate =>
            candidate.Code == WcfVocabulary.Diagnostics.ExtensionDetected &&
            candidate.RelativePath == "Oversized.config");
        AssertAbbreviation(
            extensionDiagnostic.Properties[WcfVocabulary.Properties.ExtensionType],
            extensionType);

        Assert.Contains(first.Diagnostics, diagnostic =>
            diagnostic.Code == WcfVocabulary.Diagnostics.PersistedTextAbbreviated &&
            diagnostic.Properties.GetValueOrDefault("ruleId") ==
            WcfVocabulary.Rules.PersistedTextAbbreviation);
        Assert.Equal(AnalysisStatus.PartialSuccess, first.Status);
        AssertBoundedWcfOutput(first);
        foreach (var original in new[]
                 {
                     firstContract,
                     secondContract,
                     firstName,
                     secondName,
                     firstAddress,
                     secondAddress,
                     service,
                     factory,
                     extensionType,
                 })
        {
            Assert.DoesNotContain(original, AnalysisJson.Serialize(first, indented: false));
        }

        Assert.Equal(first.CanonicalHash, second.CanonicalHash);
        Assert.Equal(
            AnalysisJson.Serialize(first, indented: false),
            AnalysisJson.Serialize(second, indented: false));
        AssertValid(first);
        AssertValid(second);
    }

    [Fact]
    public async Task Unpaired_utf16_source_literal_is_json_safe_explicit_and_round_trip_valid()
    {
        using var fixture = TemporaryWcfFixture.Copy("WcfBasic");
        var sourcePath = System.IO.Path.Combine(fixture.Path, "Contracts.cs");
        var source = await File.ReadAllTextAsync(sourcePath);
        await File.WriteAllTextAsync(
            sourcePath,
            source.Replace("CustomerContract", "Customer\\uD800Contract", StringComparison.Ordinal));
        var original = "Customer" + '\ud800' + "Contract";

        var document = await AnalyzeAsync(fixture.Path);

        var contract = Assert.Single(document.Nodes, node =>
            node.QualifiedName == "Fixtures.Wcf.Basic.ICustomerService");
        var persisted = contract.Properties[WcfVocabulary.Properties.ServiceContractName];
        Assert.DoesNotContain(persisted, char.IsSurrogate);
        Assert.Contains("\\uD800", persisted, StringComparison.Ordinal);
        Assert.Contains("invalidUtf16=true", persisted, StringComparison.Ordinal);
        Assert.Contains(Sha256Utf16(original), persisted, StringComparison.Ordinal);
        Assert.InRange(persisted.Length, 1, MaximumPersistedCharacters);

        var diagnostic = Assert.Single(document.Diagnostics, candidate =>
            candidate.Code == WcfVocabulary.Diagnostics.PersistedTextAbbreviated &&
            candidate.RelativePath == "Contracts.cs" &&
            candidate.Properties.GetValueOrDefault("field") ==
            $"node.properties.{WcfVocabulary.Properties.ServiceContractName}");
        Assert.Equal("true", diagnostic.Properties["invalidUtf16"]);
        Assert.Equal(Sha256Utf16(original), diagnostic.Properties["sha256Utf16"]);
        Assert.Equal(AnalysisStatus.PartialSuccess, document.Status);

        var json = AnalysisJson.Serialize(document, indented: false);
        Assert.DoesNotContain(json, char.IsSurrogate);
        Assert.Contains("\\\\uD800", json, StringComparison.Ordinal);
        var restored = AnalysisJson.Deserialize(json);
        Assert.Equal(document.CanonicalHash, restored.CanonicalHash);
        Assert.Equal(persisted, Assert.Single(restored.Nodes, node =>
            node.QualifiedName == "Fixtures.Wcf.Basic.ICustomerService").Properties[
                WcfVocabulary.Properties.ServiceContractName]);
        AssertValid(document);
        AssertValid(restored);
    }

    private static async Task<AnalysisDocument> AnalyzeAsync(string repositoryPath)
    {
        var baseline = await new RepositoryScanner().AnalyzeAsync(new ScannerOptions(repositoryPath));
        Assert.NotEqual(AnalysisStatus.Failure, baseline.Status);
        return await new ClassicWcfAnalyzer().AnalyzeAsync(repositoryPath, baseline);
    }

    private static bool NodeHasEvidenceFrom(
        AnalysisDocument document,
        EvidenceNode node,
        string relativePath) =>
        node.EvidenceIds
            .Select(id => document.Evidence.Single(evidence => evidence.EvidenceId == id))
            .Any(evidence => string.Equals(evidence.RelativePath, relativePath, StringComparison.Ordinal));

    private static void AssertAbbreviation(string persisted, string original)
    {
        Assert.InRange(persisted.Length, 1, MaximumPersistedCharacters);
        Assert.StartsWith(original[..64], persisted, StringComparison.Ordinal);
        Assert.Contains("domainlens:truncated=true", persisted, StringComparison.Ordinal);
        Assert.Contains($"originalLengthUtf16={original.Length}", persisted, StringComparison.Ordinal);
        Assert.Contains(Sha256Utf16(original), persisted, StringComparison.Ordinal);
    }

    private static void AssertAbbreviationDiagnostic(
        AnalysisDiagnostic diagnostic,
        string original)
    {
        Assert.Equal("true", diagnostic.Properties["truncated"]);
        Assert.Equal("false", diagnostic.Properties["invalidUtf16"]);
        Assert.Equal("false", diagnostic.Properties["reservedMarkerEscaped"]);
        Assert.Equal(original.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            diagnostic.Properties["originalLengthUtf16"]);
        Assert.Equal(Sha256Utf16(original), diagnostic.Properties["sha256Utf16"]);
        Assert.Equal(
            WcfVocabulary.Rules.PersistedTextAbbreviation,
            diagnostic.Properties["ruleId"]);
        Assert.Equal(
            MaximumPersistedCharacters.ToString(System.Globalization.CultureInfo.InvariantCulture),
            diagnostic.Properties["maximumPersistedCharacters"]);
    }

    private static void AssertBoundedWcfOutput(AnalysisDocument document)
    {
        foreach (var node in document.Nodes)
        {
            if (node.Kind.StartsWith("Wcf", StringComparison.Ordinal))
            {
                Assert.InRange(node.Name.Length, 1, MaximumPersistedCharacters);
                Assert.InRange(node.QualifiedName.Length, 1, MaximumPersistedCharacters);
            }

            foreach (var property in node.Properties.Where(pair =>
                         pair.Key.StartsWith(WcfVocabulary.Properties.Prefix, StringComparison.Ordinal)))
            {
                Assert.InRange(property.Value.Length, 0, MaximumPersistedCharacters);
            }
        }

        foreach (var edge in document.Edges.Where(edge =>
                     edge.Kind.StartsWith("Wcf", StringComparison.Ordinal)))
        {
            if (edge.UnresolvedTarget is not null)
            {
                Assert.InRange(edge.UnresolvedTarget.Length, 1, MaximumPersistedCharacters);
            }

            if (edge.Resolution.Details is not null)
            {
                Assert.InRange(edge.Resolution.Details.Length, 1, MaximumPersistedCharacters);
            }
        }

        foreach (var evidence in document.Evidence.Where(evidence =>
                     evidence.Provenance.ExtractorId == WcfVocabulary.ExtractorId &&
                     evidence.Resolution.Details is not null))
        {
            Assert.InRange(evidence.Resolution.Details!.Length, 1, MaximumPersistedCharacters);
        }

        foreach (var diagnostic in document.Diagnostics.Where(diagnostic =>
                     diagnostic.Code.StartsWith("DL4", StringComparison.Ordinal)))
        {
            Assert.InRange(diagnostic.Message.Length, 1, MaximumPersistedCharacters);
            Assert.All(diagnostic.Properties.Values, value =>
                Assert.InRange(value.Length, 0, MaximumPersistedCharacters));
        }
    }

    private static string Sha256Utf16(string value)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var bytes = new byte[value.Length * sizeof(char)];
        for (var index = 0; index < value.Length; index++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(index * sizeof(char)), value[index]);
        }

        hash.AppendData(bytes);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void AssertValid(AnalysisDocument document)
    {
        AnalysisGraphValidator.Validate(document).ThrowIfInvalid();
        Assert.True(AnalysisJson.VerifyCanonicalHash(document));
    }
}
