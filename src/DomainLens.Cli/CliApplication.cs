using System.Text.Json;
using DomainLens.Core;
using DomainLens.Scanner;

namespace DomainLens.Cli;

public static class CliApplication
{
    public const int SuccessExitCode = 0;
    public const int FailureExitCode = 1;
    public const int PartialSuccessExitCode = 2;
    public const int NotFoundExitCode = 3;
    public const int UsageExitCode = 64;

    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (args.Length == 0 || IsHelp(args[0]))
        {
            await output.WriteLineAsync(Usage).ConfigureAwait(false);
            return args.Length == 0 ? UsageExitCode : SuccessExitCode;
        }

        if (args.Length > 1 && IsHelp(args[1]))
        {
            await output.WriteLineAsync(Usage).ConfigureAwait(false);
            return SuccessExitCode;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "scan" => await RunScanAsync(args[1..], output, error, cancellationToken).ConfigureAwait(false),
                "inspect" => await RunInspectAsync(args[1..], output, error, cancellationToken).ConfigureAwait(false),
                _ => await UnknownCommandAsync(args[0], error).ConfigureAwait(false),
            };
        }
        catch (OperationCanceledException)
        {
            await error.WriteLineAsync("Analysis was cancelled.").ConfigureAwait(false);
            return FailureExitCode;
        }
        catch (CliUsageException exception)
        {
            await error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            await error.WriteLineAsync(Usage).ConfigureAwait(false);
            return UsageExitCode;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or JsonException)
        {
            await error.WriteLineAsync($"DomainLens failed safely: {Sanitize(exception.Message)}").ConfigureAwait(false);
            return FailureExitCode;
        }
    }

    private static async Task<int> RunScanAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var options = ParseOptions(args, "--repository", "--solution", "--output");
        if (!options.TryGetValue("--repository", out var repository) ||
            !options.TryGetValue("--output", out var outputPath))
        {
            await error.WriteLineAsync("scan requires --repository <path> and --output <file>.").ConfigureAwait(false);
            return UsageExitCode;
        }

        options.TryGetValue("--solution", out var solution);
        var document = await new RepositoryScanner()
            .AnalyzeAsync(new ScannerOptions(repository, solution), cancellationToken)
            .ConfigureAwait(false);
        var json = AnalysisJson.Serialize(document);
        await WriteAtomicallyAsync(outputPath, json, cancellationToken).ConfigureAwait(false);

        await output.WriteLineAsync($"Status: {document.Status}").ConfigureAwait(false);
        await output.WriteLineAsync($"Snapshot: {document.Snapshot.SnapshotId}").ConfigureAwait(false);
        await output.WriteLineAsync($"Canonical hash: {document.CanonicalHash}").ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Discovered {document.Nodes.Count} nodes, {document.Edges.Count} edges, and {document.Evidence.Count} evidence records.")
            .ConfigureAwait(false);
        await output.WriteLineAsync($"Diagnostics: {document.Diagnostics.Count}").ConfigureAwait(false);
        await output.WriteLineAsync($"Output: {Path.GetFullPath(outputPath)}").ConfigureAwait(false);

        return document.Status switch
        {
            AnalysisStatus.Success => SuccessExitCode,
            AnalysisStatus.PartialSuccess => PartialSuccessExitCode,
            _ => FailureExitCode,
        };
    }

    private static async Task<int> RunInspectAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var options = ParseOptions(args, "--analysis", "--symbol");
        if (!options.TryGetValue("--analysis", out var analysisPath) ||
            !options.TryGetValue("--symbol", out var symbolQuery))
        {
            await error.WriteLineAsync("inspect requires --analysis <file> and --symbol <id-or-name>.").ConfigureAwait(false);
            return UsageExitCode;
        }

        var json = await File.ReadAllTextAsync(analysisPath, cancellationToken).ConfigureAwait(false);
        var document = AnalysisJson.Deserialize(json);
        if (!AnalysisJson.VerifyCanonicalHash(document))
        {
            await error.WriteLineAsync("The analysis artifact canonical hash is missing or invalid.").ConfigureAwait(false);
            return FailureExitCode;
        }

        var validation = AnalysisGraphValidator.Validate(document);
        if (!validation.IsValid)
        {
            await error.WriteLineAsync("The analysis artifact failed Evidence Graph integrity validation.").ConfigureAwait(false);
            return FailureExitCode;
        }

        var matches = FindNodes(document, symbolQuery);
        if (matches.Count == 0)
        {
            await error.WriteLineAsync($"No symbol matched '{symbolQuery}'.").ConfigureAwait(false);
            return NotFoundExitCode;
        }

        if (matches.Count > 1)
        {
            await error.WriteLineAsync($"The symbol query '{symbolQuery}' is ambiguous. Matching node IDs:").ConfigureAwait(false);
            foreach (var match in matches)
            {
                await error.WriteLineAsync($"  {match.NodeId}  {match.Kind}  {match.QualifiedName}").ConfigureAwait(false);
            }

            return NotFoundExitCode;
        }

        await WriteNodeAsync(document, matches[0], output).ConfigureAwait(false);
        return SuccessExitCode;
    }

    private static IReadOnlyList<EvidenceNode> FindNodes(AnalysisDocument document, string query)
    {
        var exactIdentity = document.Nodes.Where(node =>
                string.Equals(node.NodeId, query, StringComparison.Ordinal) ||
                string.Equals(node.LogicalId, query, StringComparison.Ordinal))
            .ToArray();
        if (exactIdentity.Length > 0)
        {
            return exactIdentity;
        }

        var exactQualifiedName = document.Nodes.Where(node =>
                string.Equals(node.QualifiedName, query, StringComparison.Ordinal))
            .ToArray();
        if (exactQualifiedName.Length > 0)
        {
            return exactQualifiedName;
        }

        return document.Nodes.Where(node =>
                string.Equals(node.Name, query, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(node.QualifiedName, query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(node => node.QualifiedName, StringComparer.Ordinal)
            .ToArray();
    }

    private static async Task WriteNodeAsync(
        AnalysisDocument document,
        EvidenceNode node,
        TextWriter output)
    {
        await output.WriteLineAsync($"{node.Kind}: {node.QualifiedName}").ConfigureAwait(false);
        await output.WriteLineAsync($"Node ID: {node.NodeId}").ConfigureAwait(false);
        await output.WriteLineAsync($"Logical ID: {node.LogicalId}").ConfigureAwait(false);
        if (node.Attributes.Count > 0)
        {
            await output.WriteLineAsync($"Attributes: {string.Join(", ", node.Attributes)}").ConfigureAwait(false);
        }

        foreach (var property in node.Properties.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            await output.WriteLineAsync($"{property.Key}: {property.Value}").ConfigureAwait(false);
        }

        await output.WriteLineAsync("Evidence:").ConfigureAwait(false);
        var evidenceById = document.Evidence.ToDictionary(item => item.EvidenceId, StringComparer.Ordinal);
        if (node.EvidenceIds.Count == 0)
        {
            await output.WriteLineAsync("  (no source-backed evidence; synthetic graph node)").ConfigureAwait(false);
            return;
        }

        foreach (var evidenceId in node.EvidenceIds.OrderBy(value => value, StringComparer.Ordinal))
        {
            var evidence = evidenceById[evidenceId];
            await output.WriteLineAsync(
                $"  {evidence.RelativePath}:{evidence.Span.StartLine}:{evidence.Span.StartColumn}-" +
                $"{evidence.Span.EndLine}:{evidence.Span.EndColumn}")
                .ConfigureAwait(false);
            await output.WriteLineAsync($"    content sha256: {evidence.ContentHash}").ConfigureAwait(false);
            await output.WriteLineAsync(
                $"    extractor: {evidence.Provenance.ExtractorId}@{evidence.Provenance.ExtractorVersion} / {evidence.Provenance.RuleId}")
                .ConfigureAwait(false);
            await output.WriteLineAsync(
                $"    resolution: {evidence.Resolution.Basis}/{evidence.Resolution.Quality}")
                .ConfigureAwait(false);
        }
    }

    private static IReadOnlyDictionary<string, string> ParseOptions(
        IReadOnlyList<string> args,
        params string[] allowedNames)
    {
        var allowed = allowedNames.ToHashSet(StringComparer.Ordinal);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Count; index++)
        {
            var name = args[index];
            if (IsHelp(name))
            {
                throw new CliUsageException("Help must immediately follow the command name.");
            }

            if (!allowed.Contains(name))
            {
                throw new CliUsageException($"Unknown option '{name}'.");
            }

            if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new CliUsageException($"Option '{name}' requires a value.");
            }

            if (!result.TryAdd(name, args[++index]))
            {
                throw new CliUsageException($"Option '{name}' was supplied more than once.");
            }
        }

        return result;
    }

    private static async Task WriteAtomicallyAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporaryPath, content, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task<int> UnknownCommandAsync(string command, TextWriter error)
    {
        await error.WriteLineAsync($"Unknown command '{command}'.").ConfigureAwait(false);
        await error.WriteLineAsync(Usage).ConfigureAwait(false);
        return UsageExitCode;
    }

    private static bool IsHelp(string value) => value is "-h" or "--help" or "help";

    private static string Sanitize(string message) =>
        message.Replace('\r', ' ').Replace('\n', ' ').Trim();

    private sealed class CliUsageException(string message) : Exception(message);

    private const string Usage = """
        DomainLens Repository Structure Scanner 0.1

        Usage:
          domainlens scan --repository <path> [--solution <repository-relative.sln>] --output <analysis.json>
          domainlens inspect --analysis <analysis.json> --symbol <node-id-or-name>

        Exit codes:
          0  success
          1  fatal failure or invalid artifact
          2  partial success with explicit diagnostics
          3  inspect query not found or ambiguous
         64  invalid command usage
        """;
}
