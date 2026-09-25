using System.Text;
using System.Text.Json;
using DomainLens.Analyzer.Protocol;

namespace DomainLens.Analyzer.Tests;

public sealed class AnalyzerProtocolTests
{
    [Fact]
    public void JobDescriptorRoundTripsThroughStrictProtocolSerializer()
    {
        var expected = CreateJobDescriptor();

        var actual = AnalyzerProtocol.DeserializeJob(AnalyzerProtocol.SerializeJob(expected));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ResultEnvelopeRoundTripsThroughStrictProtocolSerializer()
    {
        var expected = CreateResultEnvelope();

        var actual = AnalyzerProtocol.DeserializeResult(AnalyzerProtocol.SerializeResult(expected));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void JobDescriptorRejectsUnknownProperties()
    {
        var json = Encoding.UTF8.GetString(AnalyzerProtocol.SerializeJob(CreateJobDescriptor()));
        var malformed = json[..^1] + ",\"unexpected\":true}";

        Assert.Throws<JsonException>(() => AnalyzerProtocol.DeserializeJob(Encoding.UTF8.GetBytes(malformed)));
    }

    [Fact]
    public void ResultEnvelopeRejectsIncorrectPropertyCasing()
    {
        var json = Encoding.UTF8.GetString(AnalyzerProtocol.SerializeResult(CreateResultEnvelope()));
        var malformed = json.Replace("\"jobId\"", "\"JobId\"", StringComparison.Ordinal);

        Assert.Throws<JsonException>(() => AnalyzerProtocol.DeserializeResult(Encoding.UTF8.GetBytes(malformed)));
    }

    [Fact]
    public void ResultEnvelopeRejectsMissingConstructorProperties()
    {
        var json = Encoding.UTF8.GetString(AnalyzerProtocol.SerializeResult(CreateResultEnvelope()));
        var malformed = json.Replace("\"workerProcessId\":42,", string.Empty, StringComparison.Ordinal);

        Assert.Throws<JsonException>(() => AnalyzerProtocol.DeserializeResult(Encoding.UTF8.GetBytes(malformed)));
    }

    [Fact]
    public void ResultEnvelopeRejectsNumericEnumValues()
    {
        var json = Encoding.UTF8.GetString(AnalyzerProtocol.SerializeResult(CreateResultEnvelope()));
        var malformed = json.Replace("\"outcome\":\"completed\"", "\"outcome\":0", StringComparison.Ordinal);

        Assert.Throws<JsonException>(() => AnalyzerProtocol.DeserializeResult(Encoding.UTF8.GetBytes(malformed)));
    }

    [Fact]
    public void ProtocolAssemblyHasNoDomainLensProjectDependencies()
    {
        var references = typeof(AnalyzerProtocol)
            .Assembly
            .GetReferencedAssemblies();

        Assert.DoesNotContain(
            references,
            reference => reference.Name?.StartsWith("DomainLens.", StringComparison.Ordinal) is true);
    }

    private static AnalyzerJobDescriptor CreateJobDescriptor() =>
        new(
            AnalyzerProtocol.CurrentVersion,
            "job-1",
            AnalyzerProtocol.RepositoryDirectoryName,
            AnalyzerProtocol.ResultFileName,
            "snapshot-1",
            new AnalyzerScannerSettings("Sample.sln", 100, 1024, 4096));

    private static AnalyzerWorkerResultEnvelope CreateResultEnvelope() =>
        new(
            AnalyzerProtocol.CurrentVersion,
            "job-1",
            42,
            AnalyzerWorkerTerminalOutcome.Completed,
            "{}",
            AnalyzerProtocol.ComputeUtf8Sha256("{}"),
            null,
            null,
            "{}",
            AnalyzerProtocol.ComputeUtf8Sha256("{}"));
}
