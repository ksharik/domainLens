using DomainLens.Core;
using DomainLens.Semantics;

namespace DomainLens.Analyzer.Host;

/// <summary>Trusted, allowlisted command used to launch a fixed analyzer worker.</summary>
public sealed record AnalyzerWorkerCommand(
    string AllowlistedExecutablePath,
    string? AllowlistedManagedEntryPointPath = null,
    IReadOnlyList<string>? FixedArguments = null);

/// <summary>One request to analyze a local repository snapshot in a child process.</summary>
public sealed record AnalyzerProcessRequest(string RepositoryPath, string? Solution = null);

/// <summary>Hard resource bounds enforced before and while invoking a worker.</summary>
public sealed record AnalyzerProcessLimits
{
    public int MaximumStagedFileCount { get; init; } = 100_000;

    public int MaximumStagedEntryCount { get; init; } = 400_000;

    public int MaximumStagedRelativeDepth { get; init; } = 64;

    public long MaximumStagedFileSizeBytes { get; init; } = 16 * 1024 * 1024;

    public long MaximumStagedTotalBytes { get; init; } = 1024L * 1024 * 1024;

    public long MaximumResultBytes { get; init; } = 64L * 1024 * 1024;

    public int MaximumCapturedOutputBytes { get; init; } = 64 * 1024;

    public TimeSpan WallClockTimeout { get; init; } = TimeSpan.FromMinutes(2);
}

/// <summary>A host/process terminal outcome, deliberately separate from AnalysisStatus.</summary>
public enum AnalyzerProcessTerminalOutcome
{
    Succeeded,
    InvalidRequest,
    StagingRejected,
    LaunchFailed,
    WorkerCrashed,
    WorkerReportedFailure,
    TimedOut,
    Cancelled,
    OutputLimitExceeded,
    ResultMissing,
    ResultUnsafe,
    ResultTooLarge,
    ResultMalformed,
    ProtocolMismatch,
    JobMismatch,
    ProcessIdentityMismatch,
    AnalysisHashMismatch,
    AnalysisMalformed,
    AnalysisGraphInvalid,
    AnalysisSnapshotMismatch,
    StagedRepositoryModified,
    SemanticAnalysisHashMismatch,
    SemanticAnalysisMalformed,
    SemanticAnalysisInvalid,
    ProcessReapFailed,
    CleanupFailed,
    HostFailure,
}

public enum AnalyzerWorkspaceCleanupStatus
{
    NotRequired,
    Succeeded,
    Failed,
}

public enum AnalyzerHostDiagnosticSeverity
{
    Information,
    Warning,
    Error,
}

public sealed record AnalyzerHostDiagnostic(
    string Code,
    AnalyzerHostDiagnosticSeverity Severity,
    string Message);

/// <summary>The trusted host's complete operational result.</summary>
public sealed record AnalyzerProcessResult(
    AnalyzerProcessTerminalOutcome Outcome,
    AnalysisDocument? Analysis,
    string? JobId,
    int? WorkerProcessId,
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    bool StandardOutputTruncated,
    bool StandardErrorTruncated,
    string? WorkspacePath,
    AnalyzerWorkspaceCleanupStatus CleanupStatus,
    IReadOnlyList<AnalyzerHostDiagnostic> Diagnostics,
    LegacySemanticAnalysisResult? SemanticAnalysis = null)
{
    public bool IsAccepted =>
        Outcome == AnalyzerProcessTerminalOutcome.Succeeded &&
        CleanupStatus == AnalyzerWorkspaceCleanupStatus.Succeeded &&
        Analysis is not null &&
        SemanticAnalysis is not null;
}
