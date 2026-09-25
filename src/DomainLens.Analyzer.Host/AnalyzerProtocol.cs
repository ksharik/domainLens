using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DomainLens.Analyzer.Host;

/// <summary>Stable identifiers and limits for the analyzer process protocol.</summary>
public static class AnalyzerProtocol
{
    public const string CurrentVersion = "domainlens.analyzer-process.v1";
    public const int MaximumJobDescriptorBytes = 64 * 1024;
    public const string JobFileName = "job.json";
    public const string ResultFileName = "result.json";
    public const string RepositoryDirectoryName = "repository";
    public const string TemporaryDirectoryName = "temp";

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static byte[] SerializeJob(AnalyzerJobDescriptor descriptor) =>
        JsonSerializer.SerializeToUtf8Bytes(descriptor, JsonOptions);

    public static AnalyzerJobDescriptor DeserializeJob(ReadOnlySpan<byte> json) =>
        JsonSerializer.Deserialize<AnalyzerJobDescriptor>(json, JsonOptions)
        ?? throw new JsonException("The analyzer job descriptor was empty.");

    public static byte[] SerializeResult(AnalyzerWorkerResultEnvelope envelope) =>
        JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);

    public static AnalyzerWorkerResultEnvelope DeserializeResult(ReadOnlySpan<byte> json) =>
        JsonSerializer.Deserialize<AnalyzerWorkerResultEnvelope>(json, JsonOptions)
        ?? throw new JsonException("The analyzer result envelope was empty.");

    public static string ComputeUtf8Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public static async Task<AnalyzerJobDescriptor> ReadJobAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var bytes = await ReadBoundedFileAsync(
                path,
                MaximumJobDescriptorBytes,
                cancellationToken)
            .ConfigureAwait(false);
        return DeserializeJob(bytes);
    }

    public static async Task WriteResultAtomicallyAsync(
        string path,
        AnalyzerWorkerResultEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("The result path must have a parent directory.", nameof(path));
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var bytes = SerializeResult(envelope);
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             16 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, fullPath, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task<byte[]> ReadBoundedFileAsync(
        string path,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > maximumBytes)
        {
            throw new InvalidDataException(
                $"The analyzer job descriptor exceeds the {maximumBytes} byte limit.");
        }

        using var content = new MemoryStream(capacity: checked((int)stream.Length));
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (content.Length + read > maximumBytes)
            {
                throw new InvalidDataException(
                    $"The analyzer job descriptor exceeds the {maximumBytes} byte limit.");
            }

            content.Write(buffer, 0, read);
        }

        return content.ToArray();
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectRequiredConstructorParameters = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(
            JsonNamingPolicy.CamelCase,
            allowIntegerValues: false));
        return options;
    }
}

/// <summary>Scanner bounds passed to the child without exposing host filesystem paths.</summary>
public sealed record AnalyzerScannerSettings(
    string? Solution,
    int MaximumFileCount,
    long MaximumFileSizeBytes,
    long MaximumTotalBytesRead);

/// <summary>
/// Versioned job descriptor. All paths are fixed repository-relative protocol paths so an
/// untrusted descriptor cannot select host files.
/// </summary>
public sealed record AnalyzerJobDescriptor(
    string ProtocolVersion,
    string JobId,
    string RepositoryRelativePath,
    string ResultRelativePath,
    string ExpectedSnapshotId,
    AnalyzerScannerSettings Scanner);

/// <summary>A worker protocol outcome, deliberately separate from analysis graph status.</summary>
public enum AnalyzerWorkerTerminalOutcome
{
    Completed,
    Failed,
}

/// <summary>Atomic child-to-host result envelope.</summary>
public sealed record AnalyzerWorkerResultEnvelope(
    string ProtocolVersion,
    string JobId,
    int WorkerProcessId,
    AnalyzerWorkerTerminalOutcome Outcome,
    string? AnalysisJson,
    string? AnalysisJsonSha256,
    string? ErrorCode,
    string? ErrorMessage,
    string? SemanticAnalysisJson = null,
    string? SemanticAnalysisJsonSha256 = null);
