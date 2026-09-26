using System.Text;
using DomainLens.Core;
using DomainLens.Scanner;

namespace DomainLens.Analyzer.Wcf.Tests;

public sealed class WcfTextEncodingTests
{
    private const string ServiceIdentity = "Fixtures.Wcf.Basic.CustomerService";

    [Fact]
    public async Task Supported_utf8_and_bom_backed_utf16_artifacts_are_parsed_with_decoded_spans()
    {
        using var fixture = TemporaryWcfFixture.Copy("WcfBasic");
        var configuration = Configuration("utf-8");
        var serviceHost = ServiceHostDirective();
        var artifacts = new[]
        {
            new EncodedArtifact("Utf8.config", configuration, WcfTestEncoding.Utf8),
            new EncodedArtifact("Utf8Bom.config", configuration, WcfTestEncoding.Utf8Bom),
            new EncodedArtifact("Utf16Le.config", Configuration("utf-16"), WcfTestEncoding.Utf16LittleEndianBom),
            new EncodedArtifact("Utf16Be.config", Configuration("utf-16"), WcfTestEncoding.Utf16BigEndianBom),
            new EncodedArtifact("Utf8.svc", serviceHost, WcfTestEncoding.Utf8),
            new EncodedArtifact("Utf8Bom.svc", serviceHost, WcfTestEncoding.Utf8Bom),
            new EncodedArtifact("Utf16Le.svc", serviceHost, WcfTestEncoding.Utf16LittleEndianBom),
            new EncodedArtifact("Utf16Be.svc", serviceHost, WcfTestEncoding.Utf16BigEndianBom),
        };
        foreach (var artifact in artifacts)
        {
            await WriteBytesAsync(fixture.Path, artifact.Path, Encode(artifact.Text, artifact.Encoding));
        }

        var document = await AnalyzeAsync(fixture.Path);

        foreach (var artifact in artifacts.Where(item => item.Path.EndsWith(".config", StringComparison.Ordinal)))
        {
            AssertEvidenceSpan(
                document,
                artifact.Path,
                artifact.Text,
                WcfVocabulary.Rules.ConfigurationService,
                "service name");
        }

        foreach (var artifact in artifacts.Where(item => item.Path.EndsWith(".svc", StringComparison.Ordinal)))
        {
            AssertEvidenceSpan(
                document,
                artifact.Path,
                artifact.Text,
                WcfVocabulary.Rules.SvcDirective,
                "<%@ ServiceHost");
        }

        Assert.DoesNotContain(document.Diagnostics, diagnostic =>
            diagnostic.Code is WcfVocabulary.Diagnostics.InvalidTextEncoding or
                WcfVocabulary.Diagnostics.UnsupportedTextEncoding or
                WcfVocabulary.Diagnostics.IncompatibleXmlEncodingDeclaration);
        AssertValid(document);
    }

    [Fact]
    public async Task Invalid_utf8_never_creates_wcf_evidence_from_replacement_characters()
    {
        using var fixture = TemporaryWcfFixture.Copy("WcfBasic");
        await WriteBytesAsync(
            fixture.Path,
            "Invalid.config",
            InvalidUtf8(Configuration("utf-8").Replace(ServiceIdentity, $"{ServiceIdentity}.INVALID", StringComparison.Ordinal)));
        await WriteBytesAsync(
            fixture.Path,
            "Invalid.svc",
            InvalidUtf8(ServiceHostDirective().Replace(ServiceIdentity, $"{ServiceIdentity}.INVALID", StringComparison.Ordinal)));

        var document = await AnalyzeAsync(fixture.Path);

        var diagnostics = document.Diagnostics
            .Where(item => item.Code == WcfVocabulary.Diagnostics.InvalidTextEncoding)
            .OrderBy(item => item.RelativePath, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(new[] { "Invalid.config", "Invalid.svc" }, diagnostics.Select(item => item.RelativePath));
        Assert.All(diagnostics, diagnostic => Assert.Equal("utf-8", diagnostic.Properties["detectedEncoding"]));
        Assert.DoesNotContain(document.Evidence, evidence =>
            evidence.Provenance.ExtractorId == WcfVocabulary.ExtractorId &&
            evidence.RelativePath is "Invalid.config" or "Invalid.svc");
        Assert.DoesNotContain("\ufffd", AnalysisJson.Serialize(document), StringComparison.Ordinal);
        Assert.Equal(AnalysisStatus.PartialSuccess, document.Status);
        AssertValid(document);
    }

    [Fact]
    public async Task Unsupported_utf32_and_bomless_utf16_artifacts_are_not_parsed()
    {
        using var fixture = TemporaryWcfFixture.Copy("WcfBasic");
        await WriteBytesAsync(
            fixture.Path,
            "Utf32.config",
            Encode(Configuration("utf-32"), WcfTestEncoding.Utf32LittleEndianBom));
        await WriteBytesAsync(
            fixture.Path,
            "BomlessUtf16.svc",
            new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true)
                .GetBytes($"  {ServiceHostDirective()}"));
        await WriteBytesAsync(
            fixture.Path,
            "DeepBomlessUtf16.config",
            new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true)
                .GetBytes(new string(' ', 512) + Configuration("utf-16")));

        var document = await AnalyzeAsync(fixture.Path);

        var diagnostics = document.Diagnostics
            .Where(item => item.Code == WcfVocabulary.Diagnostics.UnsupportedTextEncoding)
            .OrderBy(item => item.RelativePath, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            new[] { "BomlessUtf16.svc", "DeepBomlessUtf16.config", "Utf32.config" },
            diagnostics.Select(item => item.RelativePath));
        Assert.Equal("bomless-unicode", diagnostics[0].Properties["detectedEncoding"]);
        Assert.Equal("bomless-unicode", diagnostics[1].Properties["detectedEncoding"]);
        Assert.Equal("utf-32-bom", diagnostics[2].Properties["detectedEncoding"]);
        AssertNoWcfEvidence(
            document,
            "BomlessUtf16.svc",
            "DeepBomlessUtf16.config",
            "Utf32.config");
        Assert.Equal(AnalysisStatus.PartialSuccess, document.Status);
        AssertValid(document);
    }

    [Fact]
    public async Task Unsupported_xml_encoding_declaration_is_not_promoted()
    {
        using var fixture = TemporaryWcfFixture.Copy("WcfBasic");
        const string path = "LegacyEncoding.config";
        await WriteBytesAsync(
            fixture.Path,
            path,
            Encode(Configuration("windows-1252"), WcfTestEncoding.Utf8));

        var document = await AnalyzeAsync(fixture.Path);

        var diagnostic = Assert.Single(document.Diagnostics, candidate =>
            candidate.Code == WcfVocabulary.Diagnostics.UnsupportedTextEncoding &&
            candidate.RelativePath == path);
        Assert.Equal("utf-8", diagnostic.Properties["actualEncoding"]);
        Assert.Equal("windows-1252", diagnostic.Properties["declaredEncoding"]);
        AssertNoWcfEvidence(document, path);
        Assert.Equal(AnalysisStatus.PartialSuccess, document.Status);
        AssertValid(document);
    }

    [Fact]
    public async Task Incompatible_xml_encoding_declarations_are_not_promoted()
    {
        using var fixture = TemporaryWcfFixture.Copy("WcfBasic");
        await WriteBytesAsync(
            fixture.Path,
            "Utf8DeclaresUtf16.config",
            Encode(Configuration("utf-16"), WcfTestEncoding.Utf8));
        await WriteBytesAsync(
            fixture.Path,
            "Utf16LeDeclaresBe.config",
            Encode(Configuration("utf-16be"), WcfTestEncoding.Utf16LittleEndianBom));

        var document = await AnalyzeAsync(fixture.Path);

        var diagnostics = document.Diagnostics
            .Where(item => item.Code == WcfVocabulary.Diagnostics.IncompatibleXmlEncodingDeclaration)
            .OrderBy(item => item.RelativePath, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            new[] { "Utf16LeDeclaresBe.config", "Utf8DeclaresUtf16.config" },
            diagnostics.Select(item => item.RelativePath));
        Assert.Equal("utf-16le-bom", diagnostics[0].Properties["actualEncoding"]);
        Assert.Equal("utf-16be", diagnostics[0].Properties["declaredEncoding"]);
        Assert.Equal("utf-8", diagnostics[1].Properties["actualEncoding"]);
        Assert.Equal("utf-16", diagnostics[1].Properties["declaredEncoding"]);
        AssertNoWcfEvidence(document, "Utf16LeDeclaresBe.config", "Utf8DeclaresUtf16.config");
        Assert.Equal(AnalysisStatus.PartialSuccess, document.Status);
        AssertValid(document);
    }

    [Fact]
    public async Task Encoding_diagnostics_and_output_are_repeatable()
    {
        using var fixture = TemporaryWcfFixture.Copy("WcfBasic");
        await WriteBytesAsync(
            fixture.Path,
            "Unsupported.config",
            Encode(Configuration("windows-1252"), WcfTestEncoding.Utf8Bom));
        await WriteBytesAsync(
            fixture.Path,
            "Utf16.svc",
            Encode(ServiceHostDirective(), WcfTestEncoding.Utf16BigEndianBom));

        var first = await AnalyzeAsync(fixture.Path);
        var second = await AnalyzeAsync(fixture.Path);

        Assert.Equal(AnalysisJson.Serialize(first, indented: false), AnalysisJson.Serialize(second, indented: false));
        Assert.Equal(first.CanonicalHash, second.CanonicalHash);
        AssertValid(first);
        AssertValid(second);
    }

    private static string Configuration(string declaredEncoding) =>
        $"""
        <?xml version="1.0" encoding="{declaredEncoding}"?>
        <configuration>
          <system.serviceModel>
            <services>
              <service name="{ServiceIdentity}" />
            </services>
          </system.serviceModel>
        </configuration>
        """;

    private static string ServiceHostDirective() =>
        $"<%@ ServiceHost Language=\"C#\" Service=\"{ServiceIdentity}\" %>";

    private static byte[] Encode(string text, WcfTestEncoding encoding)
    {
        Encoding encoder = encoding switch
        {
            WcfTestEncoding.Utf8 or WcfTestEncoding.Utf8Bom =>
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: encoding == WcfTestEncoding.Utf8Bom,
                    throwOnInvalidBytes: true),
            WcfTestEncoding.Utf16LittleEndianBom =>
                new UnicodeEncoding(bigEndian: false, byteOrderMark: true, throwOnInvalidBytes: true),
            WcfTestEncoding.Utf16BigEndianBom =>
                new UnicodeEncoding(bigEndian: true, byteOrderMark: true, throwOnInvalidBytes: true),
            WcfTestEncoding.Utf32LittleEndianBom =>
                new UTF32Encoding(bigEndian: false, byteOrderMark: true, throwOnInvalidCharacters: true),
            _ => throw new ArgumentOutOfRangeException(nameof(encoding)),
        };
        return encoder.GetPreamble().Concat(encoder.GetBytes(text)).ToArray();
    }

    private static byte[] InvalidUtf8(string text)
    {
        const string marker = "INVALID";
        var markerOffset = text.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(markerOffset >= 0);
        return Encoding.UTF8.GetBytes(text[..markerOffset])
            .Concat(new byte[] { 0xc3, 0x28 })
            .Concat(Encoding.UTF8.GetBytes(text[(markerOffset + marker.Length)..]))
            .ToArray();
    }

    private static Task WriteBytesAsync(string root, string relativePath, byte[] bytes) =>
        File.WriteAllBytesAsync(System.IO.Path.Combine(root, relativePath), bytes);

    private static async Task<AnalysisDocument> AnalyzeAsync(string repositoryPath)
    {
        var baseline = await new RepositoryScanner().AnalyzeAsync(new ScannerOptions(repositoryPath));
        Assert.NotEqual(AnalysisStatus.Failure, baseline.Status);
        return await new ClassicWcfAnalyzer().AnalyzeAsync(repositoryPath, baseline);
    }

    private static void AssertEvidenceSpan(
        AnalysisDocument document,
        string path,
        string decodedText,
        string ruleId,
        string expected)
    {
        var evidence = Assert.Single(document.Evidence, candidate =>
            candidate.RelativePath == path &&
            candidate.Provenance.ExtractorId == WcfVocabulary.ExtractorId &&
            candidate.Provenance.RuleId == ruleId);
        var claimed = decodedText.Substring(evidence.Span.StartOffset, evidence.Span.Length);
        Assert.Contains(expected, claimed, StringComparison.Ordinal);
    }

    private static void AssertNoWcfEvidence(AnalysisDocument document, params string[] paths) =>
        Assert.DoesNotContain(document.Evidence, evidence =>
            evidence.Provenance.ExtractorId == WcfVocabulary.ExtractorId &&
            paths.Contains(evidence.RelativePath, StringComparer.Ordinal));

    private static void AssertValid(AnalysisDocument document)
    {
        AnalysisGraphValidator.Validate(document).ThrowIfInvalid();
        Assert.True(AnalysisJson.VerifyCanonicalHash(document));
    }

    private sealed record EncodedArtifact(
        string Path,
        string Text,
        WcfTestEncoding Encoding);

    private enum WcfTestEncoding
    {
        Utf8,
        Utf8Bom,
        Utf16LittleEndianBom,
        Utf16BigEndianBom,
        Utf32LittleEndianBom,
    }
}
