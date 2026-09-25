using DomainLens.Analyzer.Protocol;
using DomainLens.Analyzer.Wcf;
using DomainLens.Core;
using DomainLens.Scanner;
using DomainLens.Semantics;

namespace DomainLens.Analyzer.Worker;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (!TryReadJobArgument(args, out var jobPath))
        {
            Console.Error.WriteLine("Expected exactly: --job <absolute-job-path>.");
            return 2;
        }

        AnalyzerJobDescriptor? job = null;
        string? resultPath = null;
        try
        {
            var fullJobPath = Path.GetFullPath(jobPath);
            if (!Path.IsPathFullyQualified(jobPath) ||
                !string.Equals(
                    Path.GetFileName(fullJobPath),
                    AnalyzerProtocol.JobFileName,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("The job descriptor path is not a valid protocol path.");
            }

            var workspacePath = Path.GetDirectoryName(fullJobPath)
                ?? throw new InvalidDataException("The job descriptor has no workspace directory.");
            job = await AnalyzerProtocol.ReadJobAsync(fullJobPath).ConfigureAwait(false);
            ValidateJob(job);

            var repositoryPath = Path.Combine(workspacePath, job.RepositoryRelativePath);
            resultPath = Path.Combine(workspacePath, job.ResultRelativePath);
            EnsureDirectWorkspaceChild(repositoryPath, workspacePath, AnalyzerProtocol.RepositoryDirectoryName);
            EnsureDirectWorkspaceChild(resultPath, workspacePath, AnalyzerProtocol.ResultFileName);

            var scanner = new RepositoryScanner();
            var baselineAnalysis = await scanner.AnalyzeAsync(
                    new ScannerOptions(
                        repositoryPath,
                        job.Scanner.Solution,
                        job.Scanner.MaximumFileCount,
                        job.Scanner.MaximumFileSizeBytes,
                        job.Scanner.MaximumTotalBytesRead))
                .ConfigureAwait(false);

            var semanticContext = await new LegacySemanticCompilationService()
                .CreateAsync(repositoryPath, baselineAnalysis)
                .ConfigureAwait(false);
            var semanticAnalysis = await new LegacySemanticAnalyzer()
                .AnalyzeAsync(semanticContext)
                .ConfigureAwait(false);
            var analysis = await new ClassicWcfAnalyzer()
                .AnalyzeAsync(repositoryPath, baselineAnalysis, semanticContext)
                .ConfigureAwait(false);
            AnalysisGraphValidator.ValidateAndThrow(analysis);
            if (!AnalysisJson.VerifyCanonicalHash(analysis))
            {
                throw new InvalidDataException(
                    "The final WCF-enriched analysis canonical hash is invalid.");
            }

            var analysisJson = AnalysisJson.Serialize(analysis, indented: false);
            var semanticAnalysisJson = LegacySemanticAnalysisJson.Serialize(
                semanticAnalysis,
                analysis,
                indented: false);
            EnsureNoRepositoryAssemblyWasLoaded(repositoryPath);
            var envelope = new AnalyzerWorkerResultEnvelope(
                AnalyzerProtocol.CurrentVersion,
                job.JobId,
                Environment.ProcessId,
                AnalyzerWorkerTerminalOutcome.Completed,
                analysisJson,
                AnalyzerProtocol.ComputeUtf8Sha256(analysisJson),
                null,
                null,
                semanticAnalysisJson,
                AnalyzerProtocol.ComputeUtf8Sha256(semanticAnalysisJson));
            await AnalyzerProtocol.WriteResultAtomicallyAsync(resultPath, envelope).ConfigureAwait(false);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Analyzer worker failure: {Sanitize(exception.Message)}");
            if (job is not null && resultPath is not null)
            {
                try
                {
                    var failure = new AnalyzerWorkerResultEnvelope(
                        AnalyzerProtocol.CurrentVersion,
                        job.JobId,
                        Environment.ProcessId,
                        AnalyzerWorkerTerminalOutcome.Failed,
                        null,
                        null,
                        "worker.failure",
                        Sanitize(exception.Message));
                    await AnalyzerProtocol.WriteResultAtomicallyAsync(resultPath, failure).ConfigureAwait(false);
                    return 0;
                }
                catch (Exception writeException)
                {
                    Console.Error.WriteLine(
                        $"Analyzer worker could not write its failure envelope: {Sanitize(writeException.Message)}");
                }
            }

            return 3;
        }
    }

    private static bool TryReadJobArgument(string[] args, out string jobPath)
    {
        jobPath = string.Empty;
        if (args.Length != 2 || !string.Equals(args[0], "--job", StringComparison.Ordinal))
        {
            return false;
        }

        jobPath = args[1];
        return !string.IsNullOrWhiteSpace(jobPath);
    }

    private static void ValidateJob(AnalyzerJobDescriptor job)
    {
        if (!string.Equals(job.ProtocolVersion, AnalyzerProtocol.CurrentVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The job protocol version is unsupported.");
        }

        if (!Guid.TryParseExact(job.JobId, "N", out _))
        {
            throw new InvalidDataException("The job identifier is invalid.");
        }

        if (!string.Equals(
                job.RepositoryRelativePath,
                AnalyzerProtocol.RepositoryDirectoryName,
                StringComparison.Ordinal) ||
            !string.Equals(
                job.ResultRelativePath,
                AnalyzerProtocol.ResultFileName,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("The job uses an unsupported protocol path.");
        }

        if (job.Scanner is null ||
            job.Scanner.MaximumFileCount <= 0 ||
            job.Scanner.MaximumFileSizeBytes <= 0 ||
            job.Scanner.MaximumTotalBytesRead <= 0)
        {
            throw new InvalidDataException("The job scanner limits are invalid.");
        }
    }

    private static void EnsureDirectWorkspaceChild(string candidate, string workspace, string expectedName)
    {
        var fullCandidate = Path.GetFullPath(candidate);
        var expected = Path.GetFullPath(Path.Combine(workspace, expectedName));
        if (!string.Equals(fullCandidate, expected, PathComparison))
        {
            throw new InvalidDataException("A protocol path escaped the job workspace.");
        }
    }

    private static void EnsureNoRepositoryAssemblyWasLoaded(string repositoryPath)
    {
        var repositoryRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryPath));
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic || string.IsNullOrWhiteSpace(assembly.Location))
            {
                continue;
            }

            var assemblyPath = Path.GetFullPath(assembly.Location);
            var relative = Path.GetRelativePath(repositoryRoot, assemblyPath);
            if (!Path.IsPathRooted(relative) &&
                !string.Equals(relative, "..", StringComparison.Ordinal) &&
                !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "A repository-owned assembly was loaded into the analyzer process.");
            }
        }
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static string Sanitize(string message) =>
        message.Replace('\r', ' ').Replace('\n', ' ').Trim();
}
