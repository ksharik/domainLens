using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DomainLens.Core;
using DomainLens.Semantics;

namespace DomainLens.Analyzer.Host;

/// <summary>
/// Stages one immutable repository copy and invokes a preconfigured analyzer worker in a separate
/// process. This is a process boundary and result-validation gate; operating-system containment is
/// a separate deployment responsibility.
/// </summary>
public sealed class AnalyzerProcessHost
{
    private static readonly TimeSpan ProcessReapTimeout = TimeSpan.FromSeconds(10);

    private readonly AnalyzerWorkerCommand _worker;
    private readonly AnalyzerProcessLimits _limits;
    private readonly string _workspaceRoot;

    public AnalyzerProcessHost(
        AnalyzerWorkerCommand worker,
        AnalyzerProcessLimits? limits = null,
        string? workspaceRoot = null)
    {
        _worker = worker ?? throw new ArgumentNullException(nameof(worker));
        _limits = limits ?? new AnalyzerProcessLimits();
        _workspaceRoot = Path.GetFullPath(
            workspaceRoot ?? Path.Combine(Path.GetTempPath(), "DomainLens", "analyzer-jobs"));
    }

    public async Task<AnalyzerProcessResult> RunAsync(
        AnalyzerProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<AnalyzerHostDiagnostic>();
        if (!TryValidateConfiguration(request, diagnostics))
        {
            return CreateResult(
                AnalyzerProcessTerminalOutcome.InvalidRequest,
                null,
                null,
                null,
                null,
                CapturedOutput.Empty,
                CapturedOutput.Empty,
                null,
                AnalyzerWorkspaceCleanupStatus.NotRequired,
                diagnostics);
        }

        var jobId = Guid.NewGuid().ToString("N");
        var workspacePath = Path.Combine(_workspaceRoot, $"job-{jobId}");
        AnalyzerProcessResult primaryResult;
        var workspaceCreated = false;
        var cleanup = (Status: AnalyzerWorkspaceCleanupStatus.NotRequired, Message: (string?)null);

        using var deadlineCancellation = new CancellationTokenSource(_limits.WallClockTimeout);
        using var executionCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            deadlineCancellation.Token);

        try
        {
            Directory.CreateDirectory(_workspaceRoot);
            Directory.CreateDirectory(workspacePath);
            workspaceCreated = true;

            ValidateWorkerOutsideWorkspace(workspacePath);

            var repositoryPath = Path.Combine(workspacePath, AnalyzerProtocol.RepositoryDirectoryName);
            var temporaryPath = Path.Combine(workspacePath, AnalyzerProtocol.TemporaryDirectoryName);
            Directory.CreateDirectory(temporaryPath);

            var stagedRepository = await StagedWorkspace.CopyRepositoryAsync(
                    request.RepositoryPath,
                    repositoryPath,
                    _limits,
                    executionCancellation.Token)
                .ConfigureAwait(false);

            var descriptor = new AnalyzerJobDescriptor(
                AnalyzerProtocol.CurrentVersion,
                jobId,
                AnalyzerProtocol.RepositoryDirectoryName,
                AnalyzerProtocol.ResultFileName,
                stagedRepository.ExpectedAnalysisSnapshot.SnapshotId,
                new AnalyzerScannerSettings(
                    request.Solution,
                    _limits.MaximumStagedFileCount,
                    _limits.MaximumStagedFileSizeBytes,
                    _limits.MaximumStagedTotalBytes));
            var jobPath = Path.Combine(workspacePath, AnalyzerProtocol.JobFileName);
            var jobBytes = AnalyzerProtocol.SerializeJob(descriptor);
            if (jobBytes.Length > AnalyzerProtocol.MaximumJobDescriptorBytes)
            {
                throw new InvalidOperationException("The generated analyzer job descriptor is too large.");
            }

            await using (var jobFile = new FileStream(
                             jobPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.Read,
                             16 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await jobFile.WriteAsync(jobBytes, executionCancellation.Token).ConfigureAwait(false);
                await jobFile.FlushAsync(executionCancellation.Token).ConfigureAwait(false);
            }

            primaryResult = await ExecuteWorkerAsync(
                    descriptor,
                    jobPath,
                    workspacePath,
                    repositoryPath,
                    temporaryPath,
                    stagedRepository,
                    deadlineCancellation,
                    cancellationToken,
                    executionCancellation.Token,
                    diagnostics)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            var outcome = cancellationToken.IsCancellationRequested
                ? AnalyzerProcessTerminalOutcome.Cancelled
                : AnalyzerProcessTerminalOutcome.TimedOut;
            diagnostics.Add(new AnalyzerHostDiagnostic(
                outcome == AnalyzerProcessTerminalOutcome.Cancelled ? "host.cancelled" : "host.deadline.exceeded",
                AnalyzerHostDiagnosticSeverity.Error,
                outcome == AnalyzerProcessTerminalOutcome.Cancelled
                    ? "The analyzer job was cancelled by its caller."
                    : "The analyzer job exceeded its wall-clock deadline."));
            primaryResult = CreateResult(
                outcome,
                null,
                jobId,
                null,
                null,
                CapturedOutput.Empty,
                CapturedOutput.Empty,
                workspacePath,
                AnalyzerWorkspaceCleanupStatus.NotRequired,
                diagnostics);
        }
        catch (StagingRejectedException exception)
        {
            diagnostics.Add(Error("host.staging.rejected", exception.Message));
            primaryResult = CreateResult(
                AnalyzerProcessTerminalOutcome.StagingRejected,
                null,
                jobId,
                null,
                null,
                CapturedOutput.Empty,
                CapturedOutput.Empty,
                workspacePath,
                AnalyzerWorkspaceCleanupStatus.NotRequired,
                diagnostics);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException or
                ArgumentException or NotSupportedException or PathTooLongException)
        {
            diagnostics.Add(Error(
                "host.failure",
                $"The analyzer host could not complete the job: {Sanitize(exception.Message)}"));
            primaryResult = CreateResult(
                AnalyzerProcessTerminalOutcome.HostFailure,
                null,
                jobId,
                null,
                null,
                CapturedOutput.Empty,
                CapturedOutput.Empty,
                workspacePath,
                AnalyzerWorkspaceCleanupStatus.NotRequired,
                diagnostics);
        }
        catch (Exception exception)
        {
            diagnostics.Add(Error(
                "host.failure",
                $"The analyzer host encountered an unexpected failure: {Sanitize(exception.Message)}"));
            primaryResult = CreateResult(
                AnalyzerProcessTerminalOutcome.HostFailure,
                null,
                jobId,
                null,
                null,
                CapturedOutput.Empty,
                CapturedOutput.Empty,
                workspacePath,
                AnalyzerWorkspaceCleanupStatus.NotRequired,
                diagnostics);
        }
        finally
        {
            if (workspaceCreated)
            {
                cleanup = StagedWorkspace.TryDelete(workspacePath, GetCleanupEntryLimit());
            }
        }

        if (cleanup.Status == AnalyzerWorkspaceCleanupStatus.Failed)
        {
            diagnostics.Add(Error(
                "host.workspace.cleanup-failed",
                cleanup.Message ?? "The job workspace could not be removed."));
        }

        var finalOutcome = primaryResult.Outcome;
        var finalAnalysis = primaryResult.Analysis;
        var finalSemanticAnalysis = primaryResult.SemanticAnalysis;
        if (cleanup.Status == AnalyzerWorkspaceCleanupStatus.Failed &&
            finalOutcome == AnalyzerProcessTerminalOutcome.Succeeded)
        {
            finalOutcome = AnalyzerProcessTerminalOutcome.CleanupFailed;
            finalAnalysis = null;
            finalSemanticAnalysis = null;
        }

        if (cancellationToken.IsCancellationRequested || deadlineCancellation.IsCancellationRequested)
        {
            var controlOutcome = cancellationToken.IsCancellationRequested
                ? AnalyzerProcessTerminalOutcome.Cancelled
                : AnalyzerProcessTerminalOutcome.TimedOut;
            if (finalOutcome is not AnalyzerProcessTerminalOutcome.ProcessReapFailed and
                not AnalyzerProcessTerminalOutcome.CleanupFailed)
            {
                finalOutcome = controlOutcome;
            }

            finalAnalysis = null;
            finalSemanticAnalysis = null;
            if (!diagnostics.Any(item => item.Code is
                    "host.cancelled" or
                    "host.deadline.exceeded" or
                    "host.worker.cancelled" or
                    "host.worker.timed-out" or
                    "host.worker.result-rejected-after-control"))
            {
                diagnostics.Add(Error(
                    "host.worker.result-rejected-after-control",
                    "A cancellation or deadline was terminal before the host returned; output was not accepted."));
            }
        }

        return primaryResult with
        {
            Outcome = finalOutcome,
            Analysis = finalAnalysis,
            SemanticAnalysis = finalSemanticAnalysis,
            CleanupStatus = cleanup.Status,
            Diagnostics = diagnostics.ToArray(),
        };
    }

    private async Task<AnalyzerProcessResult> ExecuteWorkerAsync(
        AnalyzerJobDescriptor descriptor,
        string jobPath,
        string workspacePath,
        string repositoryPath,
        string temporaryPath,
        StagedRepositoryState stagedRepository,
        CancellationTokenSource deadlineCancellation,
        CancellationToken callerCancellation,
        CancellationToken executionCancellation,
        List<AnalyzerHostDiagnostic> diagnostics)
    {
        using var process = new Process
        {
            StartInfo = CreateStartInfo(jobPath, workspacePath, temporaryPath),
            EnableRaisingEvents = true,
        };

        try
        {
            if (!process.Start())
            {
                diagnostics.Add(Error("host.worker.launch-failed", "The analyzer worker did not start."));
                return CreateResult(
                    AnalyzerProcessTerminalOutcome.LaunchFailed,
                    null,
                    descriptor.JobId,
                    null,
                    null,
                    CapturedOutput.Empty,
                    CapturedOutput.Empty,
                    workspacePath,
                    AnalyzerWorkspaceCleanupStatus.NotRequired,
                    diagnostics);
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            diagnostics.Add(Error(
                "host.worker.launch-failed",
                $"The analyzer worker could not be started: {Sanitize(exception.Message)}"));
            return CreateResult(
                AnalyzerProcessTerminalOutcome.LaunchFailed,
                null,
                descriptor.JobId,
                null,
                null,
                CapturedOutput.Empty,
                CapturedOutput.Empty,
                workspacePath,
                AnalyzerWorkspaceCleanupStatus.NotRequired,
                diagnostics);
        }

        await using var processGuard = new ProcessTerminationGuard(process, diagnostics);
        var workerProcessId = process.Id;
        using var outputCancellation = new CancellationTokenSource();
        var standardOutputTask = CaptureOutputAsync(
            process.StandardOutput.BaseStream,
            _limits.MaximumCapturedOutputBytes,
            outputCancellation.Token);
        var standardErrorTask = CaptureOutputAsync(
            process.StandardError.BaseStream,
            _limits.MaximumCapturedOutputBytes,
            outputCancellation.Token);

        AnalyzerProcessTerminalOutcome? forcedOutcome = null;
        try
        {
            await process.WaitForExitAsync(executionCancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            forcedOutcome = callerCancellation.IsCancellationRequested
                ? AnalyzerProcessTerminalOutcome.Cancelled
                : AnalyzerProcessTerminalOutcome.TimedOut;
        }

        if (forcedOutcome is not null)
        {
            var reaped = await TerminateAndReapAsync(process, diagnostics).ConfigureAwait(false);
            if (!reaped)
            {
                outputCancellation.Cancel();
            }

            var cancelledOutput = await CompleteOutputCaptureAsync(
                    standardOutputTask,
                    standardErrorTask,
                    diagnostics,
                    CancellationToken.None)
                .ConfigureAwait(false);
            diagnostics.Add(Error(
                forcedOutcome == AnalyzerProcessTerminalOutcome.Cancelled
                    ? "host.worker.cancelled"
                    : "host.worker.timed-out",
                forcedOutcome == AnalyzerProcessTerminalOutcome.Cancelled
                    ? "Caller cancellation forced rejection of the worker result."
                    : "The wall-clock deadline forced rejection of the worker result."));
            return CreateResult(
                reaped ? forcedOutcome.Value : AnalyzerProcessTerminalOutcome.ProcessReapFailed,
                null,
                descriptor.JobId,
                workerProcessId,
                TryGetExitCode(process),
                cancelledOutput.StandardOutput,
                cancelledOutput.StandardError,
                workspacePath,
                AnalyzerWorkspaceCleanupStatus.NotRequired,
                diagnostics);
        }

        var output = await CompleteOutputCaptureAsync(
                standardOutputTask,
                standardErrorTask,
                diagnostics,
                executionCancellation)
            .ConfigureAwait(false);
        if (executionCancellation.IsCancellationRequested)
        {
            outputCancellation.Cancel();
        }
        var exitCode = process.ExitCode;

        try
        {
            if (!await StagedWorkspace.IsRepositoryUnchangedAsync(
                    repositoryPath,
                    stagedRepository,
                    _limits,
                    executionCancellation)
                .ConfigureAwait(false))
            {
                diagnostics.Add(Error(
                    "host.worker.repository-modified",
                    "The analyzer worker changed the staged repository; its result was rejected."));
                return CreateResult(
                    AnalyzerProcessTerminalOutcome.StagedRepositoryModified,
                    null,
                    descriptor.JobId,
                    workerProcessId,
                    exitCode,
                    output.StandardOutput,
                    output.StandardError,
                    workspacePath,
                    AnalyzerWorkspaceCleanupStatus.NotRequired,
                    diagnostics);
            }
        }
        catch (Exception exception) when (
            exception is StagingRejectedException or IOException or UnauthorizedAccessException or
                PathTooLongException or InvalidOperationException)
        {
            diagnostics.Add(Error(
                "host.worker.repository-modified",
                $"The staged repository could not be verified after worker execution: {exception.Message}"));
            return CreateResult(
                AnalyzerProcessTerminalOutcome.StagedRepositoryModified,
                null,
                descriptor.JobId,
                workerProcessId,
                exitCode,
                output.StandardOutput,
                output.StandardError,
                workspacePath,
                AnalyzerWorkspaceCleanupStatus.NotRequired,
                diagnostics);
        }

        if (output.StandardOutput.Truncated || output.StandardError.Truncated)
        {
            diagnostics.Add(Error(
                "host.worker.output-limit-exceeded",
                "The analyzer worker exceeded the bounded stdout/stderr capture limit."));
            return CreateResult(
                AnalyzerProcessTerminalOutcome.OutputLimitExceeded,
                null,
                descriptor.JobId,
                workerProcessId,
                exitCode,
                output.StandardOutput,
                output.StandardError,
                workspacePath,
                AnalyzerWorkspaceCleanupStatus.NotRequired,
                diagnostics);
        }

        if (exitCode != 0)
        {
            diagnostics.Add(Error(
                "host.worker.crashed",
                $"The analyzer worker exited with code {exitCode}."));
            return CreateResult(
                AnalyzerProcessTerminalOutcome.WorkerCrashed,
                null,
                descriptor.JobId,
                workerProcessId,
                exitCode,
                output.StandardOutput,
                output.StandardError,
                workspacePath,
                AnalyzerWorkspaceCleanupStatus.NotRequired,
                diagnostics);
        }

        // Re-check both independent controls immediately before touching an output artifact.
        if (callerCancellation.IsCancellationRequested || deadlineCancellation.IsCancellationRequested)
        {
            var outcome = callerCancellation.IsCancellationRequested
                ? AnalyzerProcessTerminalOutcome.Cancelled
                : AnalyzerProcessTerminalOutcome.TimedOut;
            diagnostics.Add(Error(
                "host.worker.result-rejected-after-control",
                "A cancellation or deadline was observed before result validation; output was not accepted."));
            return CreateResult(
                outcome,
                null,
                descriptor.JobId,
                workerProcessId,
                exitCode,
                output.StandardOutput,
                output.StandardError,
                workspacePath,
                AnalyzerWorkspaceCleanupStatus.NotRequired,
                diagnostics);
        }

        var resultPath = Path.Combine(workspacePath, AnalyzerProtocol.ResultFileName);
        if (!File.Exists(resultPath) && !Directory.Exists(resultPath))
        {
            diagnostics.Add(Error("host.worker.result-missing", "The analyzer worker produced no result envelope."));
            return CreateResult(
                AnalyzerProcessTerminalOutcome.ResultMissing,
                null,
                descriptor.JobId,
                workerProcessId,
                exitCode,
                output.StandardOutput,
                output.StandardError,
                workspacePath,
                AnalyzerWorkspaceCleanupStatus.NotRequired,
                diagnostics);
        }

        byte[] resultBytes;
        try
        {
            resultBytes = await ReadBoundedFileAsync(
                    resultPath,
                    _limits.MaximumResultBytes,
                    executionCancellation)
                .ConfigureAwait(false);
        }
        catch (BoundedFileExceededException exception)
        {
            diagnostics.Add(Error("host.worker.result-too-large", exception.Message));
            return CreateResult(
                AnalyzerProcessTerminalOutcome.ResultTooLarge,
                null,
                descriptor.JobId,
                workerProcessId,
                exitCode,
                output.StandardOutput,
                output.StandardError,
                workspacePath,
                AnalyzerWorkspaceCleanupStatus.NotRequired,
                diagnostics);
        }
        catch (UnsafeResultFileException exception)
        {
            diagnostics.Add(Error("host.worker.result-unsafe", exception.Message));
            return CreateResult(
                AnalyzerProcessTerminalOutcome.ResultUnsafe,
                null,
                descriptor.JobId,
                workerProcessId,
                exitCode,
                output.StandardOutput,
                output.StandardError,
                workspacePath,
                AnalyzerWorkspaceCleanupStatus.NotRequired,
                diagnostics);
        }

        AnalyzerWorkerResultEnvelope envelope;
        try
        {
            envelope = AnalyzerProtocol.DeserializeResult(resultBytes);
        }
        catch (JsonException exception)
        {
            diagnostics.Add(Error(
                "host.worker.result-malformed",
                $"The analyzer result envelope is malformed: {Sanitize(exception.Message)}"));
            return CreateResult(
                AnalyzerProcessTerminalOutcome.ResultMalformed,
                null,
                descriptor.JobId,
                workerProcessId,
                exitCode,
                output.StandardOutput,
                output.StandardError,
                workspacePath,
                AnalyzerWorkspaceCleanupStatus.NotRequired,
                diagnostics);
        }

        return ValidateEnvelope(
            descriptor,
            envelope,
            workerProcessId,
            exitCode,
            workspacePath,
            output,
            callerCancellation,
            deadlineCancellation,
            diagnostics);
    }

    private AnalyzerProcessResult ValidateEnvelope(
        AnalyzerJobDescriptor descriptor,
        AnalyzerWorkerResultEnvelope envelope,
        int workerProcessId,
        int exitCode,
        string workspacePath,
        (CapturedOutput StandardOutput, CapturedOutput StandardError) output,
        CancellationToken callerCancellation,
        CancellationTokenSource deadlineCancellation,
        List<AnalyzerHostDiagnostic> diagnostics)
    {
        AnalyzerProcessResult Rejected(AnalyzerProcessTerminalOutcome outcome, string code, string message)
        {
            if (callerCancellation.IsCancellationRequested || deadlineCancellation.IsCancellationRequested)
            {
                outcome = callerCancellation.IsCancellationRequested
                    ? AnalyzerProcessTerminalOutcome.Cancelled
                    : AnalyzerProcessTerminalOutcome.TimedOut;
                code = "host.worker.result-rejected-after-control";
                message = "A cancellation or deadline was observed during validation; output was not accepted.";
            }

            diagnostics.Add(Error(code, message));
            return CreateResult(
                outcome,
                null,
                descriptor.JobId,
                workerProcessId,
                exitCode,
                output.StandardOutput,
                output.StandardError,
                workspacePath,
                AnalyzerWorkspaceCleanupStatus.NotRequired,
                diagnostics);
        }

        if (!string.Equals(
                envelope.ProtocolVersion,
                AnalyzerProtocol.CurrentVersion,
                StringComparison.Ordinal))
        {
            return Rejected(
                AnalyzerProcessTerminalOutcome.ProtocolMismatch,
                "host.worker.protocol-mismatch",
                "The analyzer result protocol version does not match the requested version.");
        }

        if (!string.Equals(envelope.JobId, descriptor.JobId, StringComparison.Ordinal))
        {
            return Rejected(
                AnalyzerProcessTerminalOutcome.JobMismatch,
                "host.worker.job-mismatch",
                "The analyzer result job identifier does not match the request.");
        }

        if (envelope.WorkerProcessId != workerProcessId)
        {
            return Rejected(
                AnalyzerProcessTerminalOutcome.ProcessIdentityMismatch,
                "host.worker.process-mismatch",
                "The analyzer result process identifier does not match the launched worker.");
        }

        if (envelope.Outcome == AnalyzerWorkerTerminalOutcome.Failed)
        {
            return Rejected(
                AnalyzerProcessTerminalOutcome.WorkerReportedFailure,
                "host.worker.reported-failure",
                Sanitize(envelope.ErrorMessage ?? "The analyzer worker reported a failure."));
        }

        if (envelope.Outcome != AnalyzerWorkerTerminalOutcome.Completed)
        {
            return Rejected(
                AnalyzerProcessTerminalOutcome.ResultMalformed,
                "host.worker.outcome-invalid",
                "The analyzer worker returned an unsupported terminal outcome.");
        }

        if (string.IsNullOrWhiteSpace(envelope.AnalysisJson) ||
            string.IsNullOrWhiteSpace(envelope.AnalysisJsonSha256))
        {
            return Rejected(
                AnalyzerProcessTerminalOutcome.ResultMalformed,
                "host.worker.analysis-missing",
                "A completed result must include analysis JSON and its SHA-256 digest.");
        }

        var actualHash = AnalyzerProtocol.ComputeUtf8Sha256(envelope.AnalysisJson);
        if (!string.Equals(actualHash, envelope.AnalysisJsonSha256, StringComparison.OrdinalIgnoreCase))
        {
            return Rejected(
                AnalyzerProcessTerminalOutcome.AnalysisHashMismatch,
                "host.worker.analysis-hash-mismatch",
                "The analysis JSON SHA-256 digest does not match the result envelope.");
        }

        AnalysisDocument analysis;
        try
        {
            analysis = AnalysisJson.Deserialize(envelope.AnalysisJson);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return Rejected(
                AnalyzerProcessTerminalOutcome.AnalysisMalformed,
                "host.worker.analysis-malformed",
                $"The analysis JSON could not be decoded: {Sanitize(exception.Message)}");
        }

        var graphValidation = AnalysisGraphValidator.Validate(analysis);
        if (!graphValidation.IsValid)
        {
            var issueCodes = string.Join(
                ", ",
                graphValidation.Issues
                    .Where(issue => issue.Severity == DiagnosticSeverity.Error)
                    .Select(issue => issue.Code)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(code => code, StringComparer.Ordinal));
            return Rejected(
                AnalyzerProcessTerminalOutcome.AnalysisGraphInvalid,
                "host.worker.analysis-invalid",
                $"The trusted graph validator rejected the analysis JSON ({issueCodes}).");
        }

        if (!string.Equals(
                analysis.Snapshot.SnapshotId,
                descriptor.ExpectedSnapshotId,
                StringComparison.Ordinal))
        {
            return Rejected(
                AnalyzerProcessTerminalOutcome.AnalysisSnapshotMismatch,
                "host.worker.snapshot-mismatch",
                "The analysis snapshot does not match the trusted identity of the staged repository.");
        }

        if (string.IsNullOrWhiteSpace(envelope.SemanticAnalysisJson) ||
            string.IsNullOrWhiteSpace(envelope.SemanticAnalysisJsonSha256))
        {
            return Rejected(
                AnalyzerProcessTerminalOutcome.ResultMalformed,
                "host.worker.semantic-analysis-missing",
                "A completed result must include semantic-analysis JSON and its SHA-256 digest.");
        }

        var actualSemanticHash = AnalyzerProtocol.ComputeUtf8Sha256(envelope.SemanticAnalysisJson);
        if (!string.Equals(
                actualSemanticHash,
                envelope.SemanticAnalysisJsonSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            return Rejected(
                AnalyzerProcessTerminalOutcome.SemanticAnalysisHashMismatch,
                "host.worker.semantic-analysis-hash-mismatch",
                "The semantic-analysis JSON SHA-256 digest does not match the result envelope.");
        }

        LegacySemanticAnalysisResult semanticAnalysis;
        try
        {
            // The semantic artifact crossed a process boundary. Strict JSON decoding and
            // manifest-aware validation occur in this trusted process before acceptance.
            semanticAnalysis = LegacySemanticAnalysisJson.Deserialize(
                envelope.SemanticAnalysisJson,
                analysis);
        }
        catch (JsonException exception)
        {
            return Rejected(
                AnalyzerProcessTerminalOutcome.SemanticAnalysisMalformed,
                "host.worker.semantic-analysis-malformed",
                $"The semantic-analysis JSON could not be decoded: {Sanitize(exception.Message)}");
        }
        catch (InvalidDataException exception)
        {
            return Rejected(
                AnalyzerProcessTerminalOutcome.SemanticAnalysisInvalid,
                "host.worker.semantic-analysis-invalid",
                $"The trusted semantic validator rejected the result: {Sanitize(exception.Message)}");
        }
        catch (ArgumentException exception)
        {
            return Rejected(
                AnalyzerProcessTerminalOutcome.SemanticAnalysisMalformed,
                "host.worker.semantic-analysis-malformed",
                $"The semantic-analysis JSON could not be decoded: {Sanitize(exception.Message)}");
        }

        // A completed process is still rejected if either control became terminal during validation.
        if (callerCancellation.IsCancellationRequested || deadlineCancellation.IsCancellationRequested)
        {
            return Rejected(
                callerCancellation.IsCancellationRequested
                    ? AnalyzerProcessTerminalOutcome.Cancelled
                    : AnalyzerProcessTerminalOutcome.TimedOut,
                "host.worker.result-rejected-after-control",
                "A cancellation or deadline was observed during validation; output was not accepted.");
        }

        return CreateResult(
            AnalyzerProcessTerminalOutcome.Succeeded,
            analysis,
            descriptor.JobId,
            workerProcessId,
            exitCode,
            output.StandardOutput,
            output.StandardError,
            workspacePath,
            AnalyzerWorkspaceCleanupStatus.NotRequired,
            diagnostics,
            semanticAnalysis);
    }

    private ProcessStartInfo CreateStartInfo(
        string jobPath,
        string workspacePath,
        string temporaryPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.GetFullPath(_worker.AllowlistedExecutablePath),
            WorkingDirectory = workspacePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            CreateNoWindow = true,
        };

        if (!string.IsNullOrWhiteSpace(_worker.AllowlistedManagedEntryPointPath))
        {
            startInfo.ArgumentList.Add(Path.GetFullPath(_worker.AllowlistedManagedEntryPointPath));
        }

        foreach (var argument in _worker.FixedArguments ?? Array.Empty<string>())
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.ArgumentList.Add("--job");
        startInfo.ArgumentList.Add(jobPath);

        // Do not inherit repository-controlled or caller-secret environment state. Only fixed
        // runtime settings and the minimum Windows directory values are supplied.
        startInfo.Environment.Clear();
        if (OperatingSystem.IsWindows())
        {
            var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrWhiteSpace(windowsDirectory))
            {
                startInfo.Environment["SystemRoot"] = windowsDirectory;
                startInfo.Environment["WINDIR"] = windowsDirectory;
            }
        }

        startInfo.Environment["TEMP"] = temporaryPath;
        startInfo.Environment["TMP"] = temporaryPath;
        startInfo.Environment["DOTNET_CLI_HOME"] = temporaryPath;
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        startInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        startInfo.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["NUGET_XMLDOC_MODE"] = "skip";
        return startInfo;
    }

    private bool TryValidateConfiguration(
        AnalyzerProcessRequest? request,
        ICollection<AnalyzerHostDiagnostic> diagnostics)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RepositoryPath))
        {
            diagnostics.Add(Error("host.request.invalid", "A repository path is required."));
            return false;
        }

        if (!Path.IsPathFullyQualified(_worker.AllowlistedExecutablePath) ||
            !File.Exists(_worker.AllowlistedExecutablePath))
        {
            diagnostics.Add(Error(
                "host.worker.invalid",
                "The allowlisted worker executable must be an existing absolute file path."));
            return false;
        }

        if (!string.IsNullOrWhiteSpace(_worker.AllowlistedManagedEntryPointPath) &&
            (!Path.IsPathFullyQualified(_worker.AllowlistedManagedEntryPointPath) ||
             !File.Exists(_worker.AllowlistedManagedEntryPointPath)))
        {
            diagnostics.Add(Error(
                "host.worker.entrypoint.invalid",
                "The allowlisted managed worker entry point must be an existing absolute file path."));
            return false;
        }

        if ((_worker.FixedArguments ?? Array.Empty<string>()).Any(argument => argument is null))
        {
            diagnostics.Add(Error("host.worker.arguments.invalid", "Fixed worker arguments cannot contain null."));
            return false;
        }

        if (_limits.MaximumStagedFileCount <= 0 ||
            _limits.MaximumStagedEntryCount <= 0 ||
            _limits.MaximumStagedRelativeDepth <= 0 ||
            _limits.MaximumStagedFileSizeBytes <= 0 ||
            _limits.MaximumStagedTotalBytes <= 0 ||
            _limits.MaximumResultBytes <= 0 ||
            _limits.MaximumCapturedOutputBytes <= 0 ||
            _limits.WallClockTimeout <= TimeSpan.Zero ||
            _limits.WallClockTimeout > TimeSpan.FromHours(24))
        {
            diagnostics.Add(Error("host.limits.invalid", "All analyzer process limits must be positive and bounded."));
            return false;
        }

        try
        {
            var sourcePath = Path.GetFullPath(request.RepositoryPath);
            if (!Directory.Exists(sourcePath))
            {
                diagnostics.Add(Error("host.repository.missing", "The repository directory does not exist."));
                return false;
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            diagnostics.Add(Error(
                "host.repository.invalid",
                $"The repository path is invalid: {Sanitize(exception.Message)}"));
            return false;
        }

        return true;
    }

    private void ValidateWorkerOutsideWorkspace(string workspacePath)
    {
        var executablePath = Path.GetFullPath(_worker.AllowlistedExecutablePath);
        if (StagedWorkspace.IsWithin(executablePath, workspacePath))
        {
            throw new InvalidOperationException("The allowlisted worker executable must remain outside the job workspace.");
        }

        if (!string.IsNullOrWhiteSpace(_worker.AllowlistedManagedEntryPointPath) &&
            StagedWorkspace.IsWithin(
                Path.GetFullPath(_worker.AllowlistedManagedEntryPointPath),
                workspacePath))
        {
            throw new InvalidOperationException(
                "The allowlisted managed worker entry point must remain outside the job workspace.");
        }
    }

    private static async Task<(CapturedOutput StandardOutput, CapturedOutput StandardError)>
        CompleteOutputCaptureAsync(
            Task<CapturedOutput> standardOutputTask,
            Task<CapturedOutput> standardErrorTask,
            ICollection<AnalyzerHostDiagnostic> diagnostics,
            CancellationToken cancellationToken)
    {
        try
        {
            await Task.WhenAll(standardOutputTask, standardErrorTask)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            return (await standardOutputTask.ConfigureAwait(false), await standardErrorTask.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            diagnostics.Add(new AnalyzerHostDiagnostic(
                "host.worker.output-capture-cancelled",
                AnalyzerHostDiagnosticSeverity.Warning,
                "Worker output capture was cancelled because the process could not be reaped promptly."));
            return (CapturedOutput.Empty, CapturedOutput.Empty);
        }
        catch (IOException exception)
        {
            diagnostics.Add(new AnalyzerHostDiagnostic(
                "host.worker.output-capture-failed",
                AnalyzerHostDiagnosticSeverity.Warning,
                $"Worker output capture ended unexpectedly: {Sanitize(exception.Message)}"));
            return (CapturedOutput.Empty, CapturedOutput.Empty);
        }
    }

    private static async Task<CapturedOutput> CaptureOutputAsync(
        Stream stream,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        using var retained = new MemoryStream(maximumBytes);
        var buffer = new byte[4096];
        var truncated = false;
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            var remaining = maximumBytes - checked((int)retained.Length);
            if (remaining > 0)
            {
                retained.Write(buffer, 0, Math.Min(remaining, read));
            }

            if (read > remaining)
            {
                truncated = true;
            }
        }

        return new CapturedOutput(Encoding.UTF8.GetString(retained.ToArray()), truncated);
    }

    private static async Task<bool> TerminateAndReapAsync(
        Process process,
        ICollection<AnalyzerHostDiagnostic> diagnostics)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            diagnostics.Add(Error(
                "host.worker.kill-failed",
                $"The analyzer process tree could not be terminated: {Sanitize(exception.Message)}"));
        }

        try
        {
            await process.WaitForExitAsync(CancellationToken.None)
                .WaitAsync(ProcessReapTimeout)
                .ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            diagnostics.Add(Error(
                "host.worker.reap-failed",
                "The analyzer worker was not reaped within the bounded termination interval."));
            return false;
        }
        catch (InvalidOperationException exception)
        {
            diagnostics.Add(Error(
                "host.worker.reap-failed",
                $"The analyzer worker could not be reaped: {Sanitize(exception.Message)}"));
            return false;
        }
    }

    private static async Task<byte[]> ReadBoundedFileAsync(
        string path,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = SafeResultFile.OpenRead(path);
        if (stream.Length > maximumBytes || stream.Length > int.MaxValue)
        {
            throw new BoundedFileExceededException(
                $"The analyzer result exceeds the {maximumBytes} byte limit.");
        }

        using var content = new MemoryStream(capacity: checked((int)stream.Length));
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (content.Length + read > maximumBytes || content.Length + read > int.MaxValue)
            {
                throw new BoundedFileExceededException(
                    $"The analyzer result exceeds the {maximumBytes} byte limit.");
            }

            content.Write(buffer, 0, read);
        }

        return content.ToArray();
    }

    private static AnalyzerProcessResult CreateResult(
        AnalyzerProcessTerminalOutcome outcome,
        AnalysisDocument? analysis,
        string? jobId,
        int? workerProcessId,
        int? exitCode,
        CapturedOutput standardOutput,
        CapturedOutput standardError,
        string? workspacePath,
        AnalyzerWorkspaceCleanupStatus cleanupStatus,
        IEnumerable<AnalyzerHostDiagnostic> diagnostics,
        LegacySemanticAnalysisResult? semanticAnalysis = null) =>
        new(
            outcome,
            analysis,
            jobId,
            workerProcessId,
            exitCode,
            standardOutput.Text,
            standardError.Text,
            standardOutput.Truncated,
            standardError.Truncated,
            workspacePath,
            cleanupStatus,
            diagnostics.ToArray(),
            semanticAnalysis);

    private static int? TryGetExitCode(Process process)
    {
        try
        {
            return process.HasExited ? process.ExitCode : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private int GetCleanupEntryLimit() =>
        (int)Math.Min(int.MaxValue, (long)_limits.MaximumStagedEntryCount + 4096L);

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static AnalyzerHostDiagnostic Error(string code, string message) =>
        new(code, AnalyzerHostDiagnosticSeverity.Error, Sanitize(message));

    private static string Sanitize(string message) =>
        message.Replace('\r', ' ').Replace('\n', ' ').Trim();

    private sealed record CapturedOutput(string Text, bool Truncated)
    {
        public static readonly CapturedOutput Empty = new(string.Empty, false);
    }

    private sealed class ProcessTerminationGuard(
        Process process,
        ICollection<AnalyzerHostDiagnostic> diagnostics) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            if (HasExited(process))
            {
                return;
            }

            try
            {
                await TerminateAndReapAsync(process, diagnostics).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                diagnostics.Add(Error(
                    "host.worker.guard-failed",
                    $"The analyzer process termination guard failed: {Sanitize(exception.Message)}"));
            }
        }
    }

    private sealed class BoundedFileExceededException : Exception
    {
        public BoundedFileExceededException(string message)
            : base(message)
        {
        }
    }
}
