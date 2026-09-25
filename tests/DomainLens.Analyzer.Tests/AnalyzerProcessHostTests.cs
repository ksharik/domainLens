using System.Diagnostics;
using DomainLens.Analyzer.Host;
using DomainLens.Core;
using DomainLens.Semantics;

namespace DomainLens.Analyzer.Tests;

public sealed class AnalyzerProcessHostTests
{
    [Fact]
    public async Task RealScannerRunsInDifferentProcessAndReturnsValidatedGraph()
    {
        using var harness = TestHarness.Create();
        var host = harness.CreateRealHost();

        var result = await host.RunAsync(new AnalyzerProcessRequest(harness.RepositoryPath));

        Assert.True(result.IsAccepted, FormatFailure(result));
        Assert.Equal(AnalyzerProcessTerminalOutcome.Succeeded, result.Outcome);
        Assert.NotEqual(Environment.ProcessId, result.WorkerProcessId);
        Assert.NotNull(result.Analysis);
        Assert.True(AnalysisJson.VerifyCanonicalHash(result.Analysis));
        Assert.True(AnalysisGraphValidator.Validate(result.Analysis).IsValid);
        Assert.NotNull(result.SemanticAnalysis);
        Assert.True(
            LegacySemanticAnalysisValidator.Validate(result.SemanticAnalysis, result.Analysis).IsValid);
        Assert.Contains(result.Analysis.Nodes, node =>
            node.Kind == "Class" && node.QualifiedName == "Sample.Widget");
        Assert.DoesNotContain(harness.RepositoryPath, AnalysisJson.Serialize(result.Analysis), StringComparison.OrdinalIgnoreCase);
        AssertWorkspaceWasCleaned(result);
    }

    [Fact]
    public async Task WorkerCrashIsContainedAndWorkspaceIsCleaned()
    {
        using var harness = TestHarness.Create();
        var result = await harness.CreateTestHost("crash")
            .RunAsync(new AnalyzerProcessRequest(harness.RepositoryPath));

        Assert.Equal(AnalyzerProcessTerminalOutcome.WorkerCrashed, result.Outcome);
        Assert.Null(result.Analysis);
        Assert.Equal(71, result.ExitCode);
        AssertWorkspaceWasCleaned(result);
    }

    [Fact]
    public async Task ValidFailureDocumentIsDistinctFromProcessFailure()
    {
        using var harness = TestHarness.Create();
        File.Delete(Path.Combine(harness.RepositoryPath, "Sample.csproj"));

        var result = await harness.CreateRealHost()
            .RunAsync(new AnalyzerProcessRequest(harness.RepositoryPath));

        Assert.True(result.IsAccepted, FormatFailure(result));
        Assert.Equal(AnalyzerProcessTerminalOutcome.Succeeded, result.Outcome);
        Assert.Equal(AnalysisStatus.Failure, result.Analysis!.Status);
        AssertWorkspaceWasCleaned(result);
    }

    [Fact]
    public async Task StagingBoundsRejectInputBeforeAWorkerStarts()
    {
        using var harness = TestHarness.Create();
        await File.WriteAllBytesAsync(
            Path.Combine(harness.RepositoryPath, "oversized.bin"),
            new byte[2048]);
        var limits = TestHarness.Limits(TimeSpan.FromSeconds(10)) with
        {
            MaximumStagedFileSizeBytes = 1024,
        };

        var result = await harness.CreateTestHost("valid", limits)
            .RunAsync(new AnalyzerProcessRequest(harness.RepositoryPath));

        Assert.Equal(AnalyzerProcessTerminalOutcome.StagingRejected, result.Outcome);
        Assert.Null(result.WorkerProcessId);
        Assert.Null(result.Analysis);
        AssertWorkspaceWasCleaned(result);
    }

    [Fact]
    public async Task FileCountBoundRejectsInputBeforeAWorkerStarts()
    {
        using var harness = TestHarness.Create();
        var limits = TestHarness.Limits(TimeSpan.FromSeconds(10)) with
        {
            MaximumStagedFileCount = 1,
        };

        var result = await harness.CreateTestHost("valid", limits)
            .RunAsync(new AnalyzerProcessRequest(harness.RepositoryPath));

        Assert.Equal(AnalyzerProcessTerminalOutcome.StagingRejected, result.Outcome);
        Assert.Null(result.WorkerProcessId);
        Assert.Contains(result.Diagnostics, item => item.Code == "host.staging.rejected");
        AssertWorkspaceWasCleaned(result);
    }

    [Fact]
    public async Task AggregateByteBoundRejectsInputBeforeAWorkerStarts()
    {
        using var harness = TestHarness.Create();
        var limits = TestHarness.Limits(TimeSpan.FromSeconds(10)) with
        {
            MaximumStagedTotalBytes = 1,
        };

        var result = await harness.CreateTestHost("valid", limits)
            .RunAsync(new AnalyzerProcessRequest(harness.RepositoryPath));

        Assert.Equal(AnalyzerProcessTerminalOutcome.StagingRejected, result.Outcome);
        Assert.Null(result.WorkerProcessId);
        Assert.Contains(result.Diagnostics, item => item.Code == "host.staging.rejected");
        AssertWorkspaceWasCleaned(result);
    }

    [Fact]
    public async Task EmptyDirectoriesCountTowardTheTotalStagingEntryBound()
    {
        using var harness = TestHarness.Create();
        Directory.CreateDirectory(Path.Combine(harness.RepositoryPath, "empty-directory"));
        var limits = TestHarness.Limits(TimeSpan.FromSeconds(10)) with
        {
            MaximumStagedEntryCount = 2,
        };

        var result = await harness.CreateTestHost("valid", limits)
            .RunAsync(new AnalyzerProcessRequest(harness.RepositoryPath));

        Assert.Equal(AnalyzerProcessTerminalOutcome.StagingRejected, result.Outcome);
        Assert.Null(result.WorkerProcessId);
        Assert.Contains(result.Diagnostics, item => item.Code == "host.staging.rejected");
        AssertWorkspaceWasCleaned(result);
    }

    [Fact]
    public async Task RelativeDirectoryDepthIsBoundedBeforeAWorkerStarts()
    {
        using var harness = TestHarness.Create();
        Directory.CreateDirectory(Path.Combine(harness.RepositoryPath, "one", "two", "three"));
        var limits = TestHarness.Limits(TimeSpan.FromSeconds(10)) with
        {
            MaximumStagedRelativeDepth = 2,
        };

        var result = await harness.CreateTestHost("valid", limits)
            .RunAsync(new AnalyzerProcessRequest(harness.RepositoryPath));

        Assert.Equal(AnalyzerProcessTerminalOutcome.StagingRejected, result.Outcome);
        Assert.Null(result.WorkerProcessId);
        Assert.Contains(result.Diagnostics, item => item.Code == "host.staging.rejected");
        AssertWorkspaceWasCleaned(result);
    }

    [Fact]
    public async Task WideDirectoryIsRejectedBeforeUnboundedMaterializationOrWorkerStart()
    {
        using var harness = TestHarness.Create();
        for (var index = 0; index < 40; index++)
        {
            await File.WriteAllTextAsync(
                Path.Combine(harness.RepositoryPath, $"wide-{index:D2}.txt"),
                string.Empty);
        }

        var limits = TestHarness.Limits(TimeSpan.FromSeconds(10)) with
        {
            MaximumStagedEntryCount = 32,
        };

        var result = await harness.CreateTestHost("valid", limits)
            .RunAsync(new AnalyzerProcessRequest(harness.RepositoryPath));

        Assert.Equal(AnalyzerProcessTerminalOutcome.StagingRejected, result.Outcome);
        Assert.Null(result.WorkerProcessId);
        Assert.Contains(result.Diagnostics, item => item.Code == "host.staging.rejected");
        AssertWorkspaceWasCleaned(result);
    }

    [Fact]
    public async Task LargeAnalyzerExcludedDirectoryTreeIsAbsentFromStagedRepository()
    {
        using var harness = TestHarness.Create();
        var includedDirectory = Path.Combine(harness.RepositoryPath, "src");
        var excludedDirectory = Path.Combine(includedDirectory, "obj");
        Directory.CreateDirectory(excludedDirectory);
        for (var index = 0; index < 128; index++)
        {
            await File.WriteAllTextAsync(
                Path.Combine(excludedDirectory, $"generated-{index:D3}.cs"),
                "namespace Generated; internal sealed class Artifact { }");
        }

        var stagedRepository = Path.Combine(harness.WorkspaceRoot, "staged-large-exclusion");
        var limits = TestHarness.Limits(TimeSpan.FromSeconds(10)) with
        {
            MaximumStagedFileCount = 2,
            MaximumStagedEntryCount = 4,
        };

        var state = await StagedWorkspace.CopyRepositoryAsync(
            harness.RepositoryPath,
            stagedRepository,
            limits,
            CancellationToken.None);

        Assert.True(Directory.Exists(Path.Combine(stagedRepository, "src")));
        Assert.False(Directory.Exists(Path.Combine(stagedRepository, "src", "obj")));
        Assert.Equal(3, state.EntryCount);
        Assert.DoesNotContain(
            state.ExpectedAnalysisSnapshot.Manifest,
            entry => entry.Path.Contains("obj", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AnalyzerExcludedContentsDoNotConsumeFileOrByteLimits()
    {
        using var harness = TestHarness.Create();
        var excludedDirectory = Path.Combine(harness.RepositoryPath, "bin");
        Directory.CreateDirectory(excludedDirectory);
        for (var index = 0; index < 8; index++)
        {
            await File.WriteAllBytesAsync(
                Path.Combine(excludedDirectory, $"ignored-{index:D2}.bin"),
                new byte[4096]);
        }

        var includedFiles = new[]
        {
            Path.Combine(harness.RepositoryPath, "Sample.csproj"),
            Path.Combine(harness.RepositoryPath, "Widget.cs"),
        };
        var includedBytes = includedFiles.Sum(path => new FileInfo(path).Length);
        var maximumIncludedFileBytes = includedFiles.Max(path => new FileInfo(path).Length);
        var stagedRepository = Path.Combine(harness.WorkspaceRoot, "staged-limit-exclusion");
        var limits = TestHarness.Limits(TimeSpan.FromSeconds(10)) with
        {
            MaximumStagedFileCount = includedFiles.Length,
            MaximumStagedEntryCount = includedFiles.Length + 1,
            MaximumStagedFileSizeBytes = maximumIncludedFileBytes,
            MaximumStagedTotalBytes = includedBytes,
        };

        var state = await StagedWorkspace.CopyRepositoryAsync(
            harness.RepositoryPath,
            stagedRepository,
            limits,
            CancellationToken.None);

        Assert.False(Directory.Exists(Path.Combine(stagedRepository, "bin")));
        Assert.Equal(includedFiles.Length, state.EntryCount);
        Assert.Equal(includedFiles.Length, state.ExpectedAnalysisSnapshot.Manifest.Count);
        Assert.Equal(
            includedBytes,
            state.ExpectedAnalysisSnapshot.Manifest.Sum(entry => entry.Length));
    }

    [Fact]
    public async Task StagedSnapshotIdentityMatchesTheScannerOutputWhenExclusionsExist()
    {
        using var harness = TestHarness.Create();
        var includedDirectory = Path.Combine(harness.RepositoryPath, "src");
        Directory.CreateDirectory(includedDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(includedDirectory, "Included.cs"),
            "namespace Sample; public sealed class Included { }");
        var excludedDirectory = Path.Combine(harness.RepositoryPath, "obj", "generated");
        Directory.CreateDirectory(excludedDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(excludedDirectory, "Ignored.cs"),
            "namespace Sample; public sealed class Ignored { }");

        var expectedSnapshot = RepositorySnapshot.Create(
            null,
            new[]
            {
                CreateManifestEntry(harness.RepositoryPath, "Sample.csproj"),
                CreateManifestEntry(harness.RepositoryPath, "Widget.cs"),
                CreateManifestEntry(harness.RepositoryPath, "src/Included.cs"),
            });

        var result = await harness.CreateRealHost()
            .RunAsync(new AnalyzerProcessRequest(harness.RepositoryPath));

        Assert.True(result.IsAccepted, FormatFailure(result));
        Assert.Equal(expectedSnapshot.SnapshotId, result.Analysis!.Snapshot.SnapshotId);
        Assert.Equal(
            expectedSnapshot.Manifest.ToArray(),
            result.Analysis.Snapshot.Manifest.ToArray());
        Assert.DoesNotContain(
            result.Analysis.Snapshot.Manifest,
            entry => entry.Path.Contains("obj", StringComparison.OrdinalIgnoreCase));
        AssertWorkspaceWasCleaned(result);
    }

    [Fact]
    public async Task NestedRepositoryReparsePointIsRejectedBeforeWorkerStarts()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var harness = TestHarness.Create();
        var targetPath = Path.GetFullPath(
            Path.Combine(harness.RepositoryPath, "..", "nested-junction-target"));
        var junctionPath = Path.Combine(harness.RepositoryPath, "obj");
        Directory.CreateDirectory(targetPath);
        await File.WriteAllTextAsync(Path.Combine(targetPath, "outside.txt"), "outside");
        await CreateDirectoryJunctionAsync(junctionPath, targetPath);

        try
        {
            var result = await harness.CreateTestHost("valid")
                .RunAsync(new AnalyzerProcessRequest(harness.RepositoryPath));

            Assert.Equal(AnalyzerProcessTerminalOutcome.StagingRejected, result.Outcome);
            Assert.Null(result.WorkerProcessId);
            Assert.Contains(result.Diagnostics, item =>
                item.Code == "host.staging.rejected" &&
                item.Message.Contains("reparse point", StringComparison.Ordinal));
            AssertWorkspaceWasCleaned(result);
        }
        finally
        {
            if (Directory.Exists(junctionPath))
            {
                Directory.Delete(junctionPath);
            }
        }
    }

    [Fact]
    public async Task RepositoryReparsePointRootIsRejectedBeforeWorkerStarts()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var harness = TestHarness.Create();
        var sourceTarget = Path.GetFullPath(
            Path.Combine(harness.RepositoryPath, "..", "source-junction-target"));
        Directory.Move(harness.RepositoryPath, sourceTarget);
        await CreateDirectoryJunctionAsync(harness.RepositoryPath, sourceTarget);

        try
        {
            var result = await harness.CreateTestHost("valid")
                .RunAsync(new AnalyzerProcessRequest(harness.RepositoryPath));

            Assert.Equal(AnalyzerProcessTerminalOutcome.StagingRejected, result.Outcome);
            Assert.Null(result.WorkerProcessId);
            Assert.Contains(result.Diagnostics, item =>
                item.Code == "host.staging.rejected" &&
                item.Message.Contains("root is a reparse point", StringComparison.Ordinal));
            AssertWorkspaceWasCleaned(result);
        }
        finally
        {
            if (Directory.Exists(harness.RepositoryPath))
            {
                Directory.Delete(harness.RepositoryPath);
            }

            if (Directory.Exists(sourceTarget))
            {
                Directory.Move(sourceTarget, harness.RepositoryPath);
            }
        }
    }

    [Fact]
    public async Task WallClockTimeoutForcesRejectionAndCleanupAfterDelayedOutput()
    {
        using var harness = TestHarness.Create();
        var limits = TestHarness.Limits(TimeSpan.FromMilliseconds(250));
        var result = await harness.CreateTestHost(
                "delayed-valid",
                limits,
                "--delay-ms",
                "3000")
            .RunAsync(new AnalyzerProcessRequest(harness.RepositoryPath));

        Assert.Equal(AnalyzerProcessTerminalOutcome.TimedOut, result.Outcome);
        Assert.Null(result.Analysis);
        AssertWorkspaceWasCleaned(result);
    }

    [Fact]
    public async Task CallerCancellationKillsWorkerAndRejectsAnyOutput()
    {
        using var harness = TestHarness.Create();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        var result = await harness.CreateTestHost(
                "hang",
                TestHarness.Limits(TimeSpan.FromSeconds(10)))
            .RunAsync(new AnalyzerProcessRequest(harness.RepositoryPath), cancellation.Token);

        Assert.Equal(AnalyzerProcessTerminalOutcome.Cancelled, result.Outcome);
        Assert.Null(result.Analysis);
        AssertWorkspaceWasCleaned(result);
    }

    [Theory]
    [InlineData("malformed-output", AnalyzerProcessTerminalOutcome.ResultMalformed)]
    [InlineData("missing-outcome", AnalyzerProcessTerminalOutcome.ResultMalformed)]
    [InlineData("invalid-graph", AnalyzerProcessTerminalOutcome.AnalysisGraphInvalid)]
    [InlineData("hash-mismatch", AnalyzerProcessTerminalOutcome.AnalysisHashMismatch)]
    [InlineData("protocol-mismatch", AnalyzerProcessTerminalOutcome.ProtocolMismatch)]
    [InlineData("job-mismatch", AnalyzerProcessTerminalOutcome.JobMismatch)]
    [InlineData("process-mismatch", AnalyzerProcessTerminalOutcome.ProcessIdentityMismatch)]
    [InlineData("wrong-snapshot", AnalyzerProcessTerminalOutcome.AnalysisSnapshotMismatch)]
    [InlineData("semantic-hash-mismatch", AnalyzerProcessTerminalOutcome.SemanticAnalysisHashMismatch)]
    [InlineData("semantic-malformed", AnalyzerProcessTerminalOutcome.SemanticAnalysisMalformed)]
    [InlineData("semantic-invalid", AnalyzerProcessTerminalOutcome.SemanticAnalysisInvalid)]
    [InlineData("semantic-missing", AnalyzerProcessTerminalOutcome.ResultMalformed)]
    public async Task UntrustedResultEnvelopeMustPassEveryTrustedGate(
        string mode,
        AnalyzerProcessTerminalOutcome expectedOutcome)
    {
        using var harness = TestHarness.Create();
        var result = await harness.CreateTestHost(mode)
            .RunAsync(new AnalyzerProcessRequest(harness.RepositoryPath));

        Assert.Equal(expectedOutcome, result.Outcome);
        Assert.Null(result.Analysis);
        AssertWorkspaceWasCleaned(result);
    }

    [Fact]
    public void JobProtocolRejectsAMissingRequiredConstructorMember()
    {
        var descriptor = new AnalyzerJobDescriptor(
            AnalyzerProtocol.CurrentVersion,
            "job-id",
            AnalyzerProtocol.RepositoryDirectoryName,
            AnalyzerProtocol.ResultFileName,
            new string('0', 64),
            new AnalyzerScannerSettings(null, 10, 1024, 4096));
        var serialized = System.Text.Encoding.UTF8.GetString(AnalyzerProtocol.SerializeJob(descriptor));
        var withoutProtocolVersion = serialized.Replace(
            $"\"protocolVersion\":\"{AnalyzerProtocol.CurrentVersion}\",",
            string.Empty,
            StringComparison.Ordinal);

        Assert.NotEqual(serialized, withoutProtocolVersion);
        Assert.Throws<System.Text.Json.JsonException>(() =>
            AnalyzerProtocol.DeserializeJob(System.Text.Encoding.UTF8.GetBytes(withoutProtocolVersion)));
    }

    [Fact]
    public async Task ResultEnvelopeIsRejectedBeforeAllocationBeyondTheConfiguredBound()
    {
        using var harness = TestHarness.Create();
        var limits = TestHarness.Limits(TimeSpan.FromSeconds(10)) with
        {
            MaximumResultBytes = 128,
        };

        var result = await harness.CreateTestHost("valid", limits)
            .RunAsync(new AnalyzerProcessRequest(harness.RepositoryPath));

        Assert.Equal(AnalyzerProcessTerminalOutcome.ResultTooLarge, result.Outcome);
        Assert.Null(result.Analysis);
        AssertWorkspaceWasCleaned(result);
    }

    [Fact]
    public async Task ReparsePointResultArtifactIsRejectedBeforeItIsRead()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var harness = TestHarness.Create();
        var result = await harness.CreateTestHost("result-reparse")
            .RunAsync(new AnalyzerProcessRequest(harness.RepositoryPath));

        Assert.Equal(AnalyzerProcessTerminalOutcome.ResultUnsafe, result.Outcome);
        Assert.Null(result.Analysis);
        Assert.Contains(result.Diagnostics, item =>
            item.Code == "host.worker.result-unsafe" &&
            item.Message.Contains("reparse point", StringComparison.Ordinal));
        AssertWorkspaceWasCleaned(result);
    }

    [Fact]
    public async Task HardLinkedResultArtifactIsRejectedBeforeItIsRead()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var harness = TestHarness.Create();
        var result = await harness.CreateTestHost("result-hardlink")
            .RunAsync(new AnalyzerProcessRequest(harness.RepositoryPath));

        Assert.Equal(AnalyzerProcessTerminalOutcome.ResultUnsafe, result.Outcome);
        Assert.Null(result.Analysis);
        Assert.Contains(result.Diagnostics, item =>
            item.Code == "host.worker.result-unsafe" &&
            item.Message.Contains("more than one hard link", StringComparison.Ordinal));
        AssertWorkspaceWasCleaned(result);
    }

    [Fact]
    public async Task WorkerCreatedAnalyzerExcludedTreeRejectsOtherwiseValidOutput()
    {
        using var harness = TestHarness.Create();

        var result = await harness.CreateTestHost("repository-modified")
            .RunAsync(new AnalyzerProcessRequest(harness.RepositoryPath));

        Assert.Equal(AnalyzerProcessTerminalOutcome.StagedRepositoryModified, result.Outcome);
        Assert.NotNull(result.WorkerProcessId);
        Assert.Null(result.Analysis);
        Assert.Contains(result.Diagnostics, item =>
            item.Code == "host.worker.repository-modified" &&
            item.Message.Contains("changed the staged repository", StringComparison.Ordinal));
        AssertWorkspaceWasCleaned(result);
    }

    [Fact]
    public async Task WorkerFailureUsesOnlyTheFixedTrustedDiagnosticCode()
    {
        using var harness = TestHarness.Create();

        var result = await harness.CreateTestHost("reported-failure")
            .RunAsync(new AnalyzerProcessRequest(harness.RepositoryPath));

        Assert.Equal(AnalyzerProcessTerminalOutcome.WorkerReportedFailure, result.Outcome);
        var diagnostic = Assert.Single(result.Diagnostics, item => item.Severity == AnalyzerHostDiagnosticSeverity.Error);
        Assert.Equal("host.worker.reported-failure", diagnostic.Code);
        Assert.DoesNotContain("repository.controlled", diagnostic.Code, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', diagnostic.Message);
        Assert.DoesNotContain('\n', diagnostic.Message);
        AssertWorkspaceWasCleaned(result);
    }

    [Fact]
    public async Task WorkerDoesNotInheritCallerSecretEnvironmentVariable()
    {
        const string variable = "DOMAINLENS_TEST_SECRET_DO_NOT_INHERIT";
        var original = Environment.GetEnvironmentVariable(variable);
        Environment.SetEnvironmentVariable(variable, "sensitive-test-value");
        try
        {
            using var harness = TestHarness.Create();
            var result = await harness.CreateTestHost("environment-probe")
                .RunAsync(new AnalyzerProcessRequest(harness.RepositoryPath));

            Assert.True(result.IsAccepted, FormatFailure(result));
            Assert.Contains("SECRET_ABSENT", result.StandardOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("SECRET_PRESENT", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("PATH_ABSENT", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains($"CWD={result.WorkspacePath}", result.StandardOutput, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                $"TEMP={Path.Combine(result.WorkspacePath!, AnalyzerProtocol.TemporaryDirectoryName)}",
                result.StandardOutput,
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                $"TMP={Path.Combine(result.WorkspacePath!, AnalyzerProtocol.TemporaryDirectoryName)}",
                result.StandardOutput,
                StringComparison.OrdinalIgnoreCase);
            AssertWorkspaceWasCleaned(result);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, original);
        }
    }

    [Fact]
    public async Task EveryJobUsesADistinctWorkspace()
    {
        using var harness = TestHarness.Create();
        var host = harness.CreateTestHost("valid");

        var first = await host.RunAsync(new AnalyzerProcessRequest(harness.RepositoryPath));
        var second = await host.RunAsync(new AnalyzerProcessRequest(harness.RepositoryPath));

        Assert.True(first.IsAccepted, FormatFailure(first));
        Assert.True(second.IsAccepted, FormatFailure(second));
        Assert.NotEqual(first.JobId, second.JobId);
        Assert.NotEqual(first.WorkspacePath, second.WorkspacePath);
        AssertWorkspaceWasCleaned(first);
        AssertWorkspaceWasCleaned(second);
    }

    [Fact]
    public void CleanupTraversalRejectsWideDirectoryAtConfiguredEntryBound()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "DomainLens.Analyzer.CleanupTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            for (var index = 0; index < 32; index++)
            {
                File.WriteAllText(Path.Combine(root, $"entry-{index:D2}.txt"), string.Empty);
            }

            var cleanup = StagedWorkspace.TryDelete(root, maximumEntryCount: 8);

            Assert.Equal(AnalyzerWorkspaceCleanupStatus.Failed, cleanup.Status);
            Assert.Contains("bounded 8 filesystem-entry limit", cleanup.Message, StringComparison.Ordinal);
            Assert.True(Directory.Exists(root));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ExcessWorkerOutputIsDrainedButRejectedAtTheBound()
    {
        using var harness = TestHarness.Create();
        var limits = TestHarness.Limits(TimeSpan.FromSeconds(10)) with
        {
            MaximumCapturedOutputBytes = 1024,
        };
        var result = await harness.CreateTestHost("large-output", limits)
            .RunAsync(new AnalyzerProcessRequest(harness.RepositoryPath));

        Assert.Equal(AnalyzerProcessTerminalOutcome.OutputLimitExceeded, result.Outcome);
        Assert.Null(result.Analysis);
        Assert.True(result.StandardOutputTruncated);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(result.StandardOutput) <= 1024);
        AssertWorkspaceWasCleaned(result);
    }

    [Fact]
    public async Task TimeoutTerminatesDescendantProcessTree()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var harness = TestHarness.Create();
        var result = await harness.CreateTestHost(
                "tree-hang",
                TestHarness.Limits(TimeSpan.FromSeconds(5)))
            .RunAsync(new AnalyzerProcessRequest(harness.RepositoryPath));

        Assert.Equal(AnalyzerProcessTerminalOutcome.TimedOut, result.Outcome);
        var childProcessId = ParseChildProcessId(result.StandardOutput);
        Assert.True(await WaitForProcessExitAsync(childProcessId, TimeSpan.FromSeconds(5)));
        AssertWorkspaceWasCleaned(result);
    }

    private static int ParseChildProcessId(string output)
    {
        var line = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Single(value => value.StartsWith("CHILD_PID=", StringComparison.Ordinal));
        return int.Parse(line["CHILD_PID=".Length..], System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<bool> WaitForProcessExitAsync(int processId, TimeSpan timeout)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < timeout)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (process.HasExited)
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                return true;
            }

            await Task.Delay(50);
        }

        return false;
    }

    private static async Task CreateDirectoryJunctionAsync(string junctionPath, string targetPath)
    {
        var windowsDirectory = Environment.GetEnvironmentVariable("SystemRoot")
            ?? throw new InvalidOperationException("SystemRoot is required for the junction test.");
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(windowsDirectory, "System32", "cmd.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(junctionPath);
        startInfo.ArgumentList.Add(targetPath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The junction helper process did not start.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await Task.WhenAll(standardOutput, standardError);
        Assert.True(
            process.ExitCode == 0 && Directory.Exists(junctionPath),
            "The test directory junction could not be created.");
    }

    private static void AssertWorkspaceWasCleaned(AnalyzerProcessResult result)
    {
        Assert.Equal(AnalyzerWorkspaceCleanupStatus.Succeeded, result.CleanupStatus);
        Assert.NotNull(result.WorkspacePath);
        Assert.False(Directory.Exists(result.WorkspacePath));
        Assert.False(File.Exists(result.WorkspacePath));
    }

    private static string FormatFailure(AnalyzerProcessResult result) =>
        $"Outcome={result.Outcome}; Exit={result.ExitCode}; " +
        $"Diagnostics={string.Join(" | ", result.Diagnostics.Select(item => $"{item.Code}: {item.Message}"))}; " +
        $"StdOut={result.StandardOutput}; StdErr={result.StandardError}";

    private static ManifestEntry CreateManifestEntry(string repositoryPath, string relativePath)
    {
        var normalizedPath = CanonicalIdentity.NormalizeRepositoryPath(relativePath);
        var fullPath = Path.Combine(
            repositoryPath,
            normalizedPath.Replace('/', Path.DirectorySeparatorChar));
        var content = File.ReadAllBytes(fullPath);
        return new ManifestEntry(
            normalizedPath,
            CanonicalIdentity.Sha256Hex(content),
            content.LongLength);
    }

    private sealed class TestHarness : IDisposable
    {
        private readonly string _rootPath;
        private readonly string _repositoryRoot;
        private readonly string _configuration;

        private TestHarness(string rootPath, string repositoryRoot, string configuration)
        {
            _rootPath = rootPath;
            _repositoryRoot = repositoryRoot;
            _configuration = configuration;
            RepositoryPath = Path.Combine(rootPath, "source");
            WorkspaceRoot = Path.Combine(rootPath, "jobs");
        }

        public string RepositoryPath { get; }

        public string WorkspaceRoot { get; }

        public static TestHarness Create()
        {
            var repositoryRoot = FindRepositoryRoot();
            var configuration = GetBuildConfiguration();
            var rootPath = Path.Combine(
                Path.GetTempPath(),
                "DomainLens.Analyzer.Tests",
                Guid.NewGuid().ToString("N"));
            var sourcePath = Path.Combine(rootPath, "source");
            Directory.CreateDirectory(sourcePath);
            File.WriteAllText(
                Path.Combine(sourcePath, "Sample.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
            File.WriteAllText(
                Path.Combine(sourcePath, "Widget.cs"),
                "namespace Sample; public sealed class Widget { public int Id { get; init; } }");
            return new TestHarness(rootPath, repositoryRoot, configuration);
        }

        public AnalyzerProcessHost CreateRealHost(AnalyzerProcessLimits? limits = null) =>
            CreateHost("src", "DomainLens.Analyzer.Worker", limits, Array.Empty<string>());

        public AnalyzerProcessHost CreateTestHost(
            string mode,
            AnalyzerProcessLimits? limits = null,
            params string[] extraArguments) =>
            CreateHost(
                "tests",
                "DomainLens.Analyzer.TestWorker",
                limits,
                new[] { "--mode", mode }.Concat(extraArguments).ToArray());

        public static AnalyzerProcessLimits Limits(TimeSpan timeout) =>
            new()
            {
                MaximumStagedFileCount = 100,
                MaximumStagedEntryCount = 256,
                MaximumStagedRelativeDepth = 16,
                MaximumStagedFileSizeBytes = 1024 * 1024,
                MaximumStagedTotalBytes = 4 * 1024 * 1024,
                MaximumResultBytes = 4 * 1024 * 1024,
                MaximumCapturedOutputBytes = 16 * 1024,
                WallClockTimeout = timeout,
            };

        public void Dispose()
        {
            if (!Directory.Exists(_rootPath))
            {
                return;
            }

            var expectedParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "DomainLens.Analyzer.Tests"));
            var candidate = Path.GetFullPath(_rootPath);
            var relative = Path.GetRelativePath(expectedParent, candidate);
            if (Path.IsPathRooted(relative) ||
                relative.StartsWith("..", StringComparison.Ordinal) ||
                relative.Length == 0)
            {
                throw new InvalidOperationException("Refusing to remove an unexpected test directory.");
            }

            Directory.Delete(candidate, recursive: true);
        }

        private AnalyzerProcessHost CreateHost(
            string projectParent,
            string projectName,
            AnalyzerProcessLimits? limits,
            IReadOnlyList<string> fixedArguments)
        {
            var workerAssembly = Path.Combine(
                _repositoryRoot,
                projectParent,
                projectName,
                "bin",
                _configuration,
                "net10.0",
                $"{projectName}.dll");
            Assert.True(File.Exists(workerAssembly), $"Worker assembly not found: {workerAssembly}");

            var command = new AnalyzerWorkerCommand(
                FindDotNetHost(_repositoryRoot),
                workerAssembly,
                fixedArguments);
            return new AnalyzerProcessHost(
                command,
                limits ?? Limits(TimeSpan.FromSeconds(20)),
                WorkspaceRoot);
        }

        private static string FindDotNetHost(string repositoryRoot)
        {
            var fromTestHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            if (!string.IsNullOrWhiteSpace(fromTestHost) && File.Exists(fromTestHost))
            {
                return Path.GetFullPath(fromTestHost);
            }

            var localSdk = Path.GetFullPath(Path.Combine(repositoryRoot, "..", ".dotnet10", "dotnet.exe"));
            Assert.True(File.Exists(localSdk), $".NET host not found: {localSdk}");
            return localSdk;
        }

        private static string FindRepositoryRoot()
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "global.json")) &&
                    Directory.Exists(Path.Combine(current.FullName, "src", "DomainLens.Core")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate the DomainLens repository root.");
        }

        private static string GetBuildConfiguration()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (string.Equals(directory.Name, "Debug", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(directory.Name, "Release", StringComparison.OrdinalIgnoreCase))
                {
                    return directory.Name;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("Could not determine the test build configuration.");
        }
    }
}
