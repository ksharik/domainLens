using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using DomainLens.Analyzer.Protocol;
using DomainLens.Core;
using DomainLens.Scanner;
using DomainLens.Semantics;

namespace DomainLens.Analyzer.TestWorker;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var mode = GetOption(args, "--mode") ?? "valid";
        if (string.Equals(mode, "leaf-hang", StringComparison.Ordinal))
        {
            await Task.Delay(Timeout.InfiniteTimeSpan).ConfigureAwait(false);
            return 0;
        }

        if (string.Equals(mode, "crash", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Intentional test-worker crash.");
            return 71;
        }

        if (string.Equals(mode, "hang", StringComparison.Ordinal))
        {
            await Task.Delay(Timeout.InfiniteTimeSpan).ConfigureAwait(false);
            return 0;
        }

        if (string.Equals(mode, "tree-hang", StringComparison.Ordinal))
        {
            using var child = StartHangingChild();
            Console.Out.WriteLine($"CHILD_PID={child.Id}");
            Console.Out.Flush();
            await Task.Delay(Timeout.InfiniteTimeSpan).ConfigureAwait(false);
            return 0;
        }

        var jobPath = GetOption(args, "--job")
            ?? throw new InvalidOperationException("The test worker requires --job.");
        var job = await AnalyzerProtocol.ReadJobAsync(jobPath).ConfigureAwait(false);
        var workspace = Path.GetDirectoryName(Path.GetFullPath(jobPath))
            ?? throw new InvalidOperationException("The test job has no workspace.");
        var repositoryPath = Path.Combine(workspace, job.RepositoryRelativePath);
        var resultPath = Path.Combine(workspace, job.ResultRelativePath);

        if (string.Equals(mode, "delayed-valid", StringComparison.Ordinal))
        {
            var delayText = GetOption(args, "--delay-ms") ?? "1000";
            await Task.Delay(int.Parse(delayText, System.Globalization.CultureInfo.InvariantCulture))
                .ConfigureAwait(false);
        }

        if (string.Equals(mode, "malformed-output", StringComparison.Ordinal))
        {
            await WriteBytesAtomicallyAsync(
                    resultPath,
                    Encoding.UTF8.GetBytes("{ malformed"))
                .ConfigureAwait(false);
            return 0;
        }

        if (string.Equals(mode, "reported-failure", StringComparison.Ordinal))
        {
            var failure = new AnalyzerWorkerResultEnvelope(
                AnalyzerProtocol.CurrentVersion,
                job.JobId,
                Environment.ProcessId,
                AnalyzerWorkerTerminalOutcome.Failed,
                null,
                null,
                "repository.controlled\r\ncode",
                "Intentional test-worker failure.\r\nSecond line.");
            await AnalyzerProtocol.WriteResultAtomicallyAsync(resultPath, failure).ConfigureAwait(false);
            return 0;
        }

        if (string.Equals(mode, "environment-probe", StringComparison.Ordinal))
        {
            var secret = Environment.GetEnvironmentVariable("DOMAINLENS_TEST_SECRET_DO_NOT_INHERIT");
            Console.Out.WriteLine(string.IsNullOrEmpty(secret) ? "SECRET_ABSENT" : "SECRET_PRESENT");
            Console.Out.WriteLine($"CWD={Environment.CurrentDirectory}");
            Console.Out.WriteLine($"TEMP={Environment.GetEnvironmentVariable("TEMP")}");
            Console.Out.WriteLine($"TMP={Environment.GetEnvironmentVariable("TMP")}");
            Console.Out.WriteLine(
                string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PATH"))
                    ? "PATH_ABSENT"
                    : "PATH_PRESENT");
        }

        if (string.Equals(mode, "large-output", StringComparison.Ordinal))
        {
            Console.Out.WriteLine(new string('x', 128 * 1024));
        }

        var analysis = await CreateAnalysisAsync(mode, repositoryPath, job.Scanner).ConfigureAwait(false);
        var analysisJson = AnalysisJson.Serialize(analysis, indented: false);
        var protocolVersion = string.Equals(mode, "protocol-mismatch", StringComparison.Ordinal)
            ? "domainlens.analyzer-process.future"
            : AnalyzerProtocol.CurrentVersion;
        var resultJobId = string.Equals(mode, "job-mismatch", StringComparison.Ordinal)
            ? Guid.NewGuid().ToString("N")
            : job.JobId;
        var workerProcessId = string.Equals(mode, "process-mismatch", StringComparison.Ordinal)
            ? Environment.ProcessId + 1
            : Environment.ProcessId;
        var resultHash = string.Equals(mode, "hash-mismatch", StringComparison.Ordinal)
            ? new string('0', 64)
            : AnalyzerProtocol.ComputeUtf8Sha256(analysisJson);
        var semanticAnalysis = await new LegacySemanticAnalyzer()
            .AnalyzeAsync(repositoryPath, analysis)
            .ConfigureAwait(false);
        var semanticAnalysisJson = LegacySemanticAnalysisJson.Serialize(
            semanticAnalysis,
            indented: false);
        if (string.Equals(mode, "semantic-malformed", StringComparison.Ordinal))
        {
            semanticAnalysisJson = "{ malformed";
        }
        else if (string.Equals(mode, "semantic-invalid", StringComparison.Ordinal))
        {
            semanticAnalysisJson = semanticAnalysisJson.Replace(
                LegacySemanticAnalysisValidator.Net472ReferenceSetId,
                "repository-controlled/reference-set",
                StringComparison.Ordinal);
        }

        var semanticAnalysisHash = string.Equals(mode, "semantic-hash-mismatch", StringComparison.Ordinal)
            ? new string('0', 64)
            : AnalyzerProtocol.ComputeUtf8Sha256(semanticAnalysisJson);
        var omitSemanticAnalysis = string.Equals(mode, "semantic-missing", StringComparison.Ordinal);
        if (string.Equals(mode, "repository-modified", StringComparison.Ordinal))
        {
            var objDirectory = Path.Combine(repositoryPath, "obj");
            Directory.CreateDirectory(objDirectory);
            await File.WriteAllTextAsync(
                    Path.Combine(objDirectory, "project.assets.json"),
                    "{\"simulatedRestore\":true}")
                .ConfigureAwait(false);
        }

        var envelope = new AnalyzerWorkerResultEnvelope(
            protocolVersion,
            resultJobId,
            workerProcessId,
            AnalyzerWorkerTerminalOutcome.Completed,
            analysisJson,
            resultHash,
            null,
            null,
            omitSemanticAnalysis ? null : semanticAnalysisJson,
            omitSemanticAnalysis ? null : semanticAnalysisHash);
        if (string.Equals(mode, "result-reparse", StringComparison.Ordinal))
        {
            await CreateDirectoryJunctionResultAsync(workspace, resultPath).ConfigureAwait(false);
            return 0;
        }

        if (string.Equals(mode, "result-hardlink", StringComparison.Ordinal))
        {
            await CreateHardLinkedResultAsync(workspace, resultPath, envelope).ConfigureAwait(false);
            return 0;
        }

        if (string.Equals(mode, "missing-outcome", StringComparison.Ordinal))
        {
            var serializedEnvelope = Encoding.UTF8.GetString(AnalyzerProtocol.SerializeResult(envelope));
            var withoutOutcome = serializedEnvelope.Replace(
                "\"outcome\":\"completed\",",
                string.Empty,
                StringComparison.Ordinal);
            if (string.Equals(serializedEnvelope, withoutOutcome, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The test envelope did not contain its outcome member.");
            }

            await WriteBytesAtomicallyAsync(resultPath, Encoding.UTF8.GetBytes(withoutOutcome))
                .ConfigureAwait(false);
            return 0;
        }

        await AnalyzerProtocol.WriteResultAtomicallyAsync(resultPath, envelope).ConfigureAwait(false);
        return 0;
    }

    private static async Task<AnalysisDocument> CreateAnalysisAsync(
        string mode,
        string repositoryPath,
        AnalyzerScannerSettings scannerSettings)
    {
        var document = await new RepositoryScanner()
            .AnalyzeAsync(new ScannerOptions(
                repositoryPath,
                scannerSettings.Solution,
                scannerSettings.MaximumFileCount,
                scannerSettings.MaximumFileSizeBytes,
                scannerSettings.MaximumTotalBytesRead))
            .ConfigureAwait(false);
        if (string.Equals(mode, "wrong-snapshot", StringComparison.Ordinal))
        {
            document = AnalysisDocument.Empty(
                RepositorySnapshot.Create(null, Array.Empty<ManifestEntry>()));
        }

        if (string.Equals(mode, "invalid-graph", StringComparison.Ordinal))
        {
            document = document with { SchemaVersion = "domainlens.evidence.unsupported" };
        }

        return AnalysisJson.WithCanonicalHash(document);
    }

    private static Process StartHangingChild()
    {
        var processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("The test worker executable path is unavailable.");
        var startInfo = new ProcessStartInfo
        {
            FileName = processPath,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (string.Equals(
                Path.GetFileNameWithoutExtension(processPath),
                "dotnet",
                StringComparison.OrdinalIgnoreCase))
        {
            startInfo.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        }

        startInfo.ArgumentList.Add("--mode");
        startInfo.ArgumentList.Add("leaf-hang");
        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("The hanging child process did not start.");
    }

    private static async Task CreateDirectoryJunctionResultAsync(
        string workspace,
        string resultPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The result-reparse test mode requires Windows.");
        }

        var targetPath = Path.GetFullPath(
            Path.Combine(workspace, "..", "..", "linked-result-directory"));
        Directory.CreateDirectory(targetPath);
        var windowsDirectory = Environment.GetEnvironmentVariable("SystemRoot")
            ?? throw new InvalidOperationException("SystemRoot is required for the junction test.");
        var commandPath = Path.Combine(windowsDirectory, "System32", "cmd.exe");
        var startInfo = new ProcessStartInfo
        {
            FileName = commandPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(resultPath);
        startInfo.ArgumentList.Add(targetPath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The junction helper process did not start.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);
        await Task.WhenAll(standardOutput, standardError).ConfigureAwait(false);
        if (process.ExitCode != 0 || !Directory.Exists(resultPath))
        {
            throw new InvalidOperationException("The test result junction could not be created.");
        }
    }

    private static async Task CreateHardLinkedResultAsync(
        string workspace,
        string resultPath,
        AnalyzerWorkerResultEnvelope envelope)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The result-hardlink test mode requires Windows.");
        }

        var targetPath = Path.GetFullPath(
            Path.Combine(workspace, "..", "..", "linked-result.json"));
        await AnalyzerProtocol.WriteResultAtomicallyAsync(targetPath, envelope).ConfigureAwait(false);
        if (!CreateHardLink(resultPath, targetPath, IntPtr.Zero))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "The hard-linked test result could not be created.");
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(
        string fileName,
        string existingFileName,
        IntPtr securityAttributes);

    private static async Task WriteBytesAtomicallyAsync(string resultPath, byte[] bytes)
    {
        var temporaryPath = $"{resultPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, bytes).ConfigureAwait(false);
            File.Move(temporaryPath, resultPath, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string? GetOption(IReadOnlyList<string> args, string name)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.Ordinal))
            {
                return args[index + 1];
            }
        }

        return null;
    }
}
