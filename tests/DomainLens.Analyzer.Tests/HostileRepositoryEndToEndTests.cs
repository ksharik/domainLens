using System.Security;
using System.Text;
using DomainLens.Analyzer.Host;
using DomainLens.Core;

namespace DomainLens.Analyzer.Tests;

public sealed class HostileRepositoryEndToEndTests
{
    private const string MarkerPlaceholder = "__DOMAINLENS_EXTERNAL_MARKER__";

    [Fact]
    public async Task RealWorkerKeepsRepositoryControlledBuildAndCompilerPayloadsInert()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = HostileRepositoryFixture.Create();
        var limits = new AnalyzerProcessLimits
        {
            MaximumStagedFileCount = 64,
            MaximumStagedFileSizeBytes = 512 * 1024,
            MaximumStagedTotalBytes = 2 * 1024 * 1024,
            MaximumResultBytes = 2 * 1024 * 1024,
            MaximumCapturedOutputBytes = 16 * 1024,
            WallClockTimeout = TimeSpan.FromSeconds(30),
        };
        var host = fixture.CreateRealHost(limits);

        var result = await host.RunAsync(new AnalyzerProcessRequest(fixture.RepositoryPath));

        Assert.True(result.IsAccepted, FormatFailure(result));
        Assert.Equal(AnalyzerProcessTerminalOutcome.Succeeded, result.Outcome);
        Assert.NotEqual(Environment.ProcessId, result.WorkerProcessId);
        Assert.NotNull(result.Analysis);
        Assert.True(AnalysisJson.VerifyCanonicalHash(result.Analysis));
        Assert.True(AnalysisGraphValidator.Validate(result.Analysis).IsValid);
        Assert.InRange(result.Analysis.Snapshot.Manifest.Count, 1, limits.MaximumStagedFileCount);
        Assert.All(result.Analysis.Snapshot.Manifest, entry =>
            Assert.InRange(entry.Length, 0, limits.MaximumStagedFileSizeBytes));
        Assert.InRange(
            result.Analysis.Snapshot.Manifest.Sum(entry => entry.Length),
            1,
            limits.MaximumStagedTotalBytes);
        Assert.True(Encoding.UTF8.GetByteCount(result.StandardOutput) <= limits.MaximumCapturedOutputBytes);
        Assert.True(Encoding.UTF8.GetByteCount(result.StandardError) <= limits.MaximumCapturedOutputBytes);

        Assert.Contains(result.Analysis.Snapshot.Manifest, entry =>
            string.Equals(
                entry.Path.Replace('\\', '/'),
                "RepositoryPayload/DomainLens.RepositoryPayload.dll",
                StringComparison.Ordinal));

        Assert.NotNull(result.SemanticAnalysis);
        Assert.Equal(
            "microsoft.netframework.referenceassemblies.net472/1.0.3",
            result.SemanticAnalysis.ReferenceSetId);
        Assert.All(result.SemanticAnalysis.MetadataReferences, reference =>
        {
            Assert.False(Path.IsPathRooted(reference.RelativePath));
            Assert.DoesNotContain("..", reference.RelativePath, StringComparison.Ordinal);
        });
        Assert.DoesNotContain(result.SemanticAnalysis.MetadataReferences, reference =>
            reference.AssemblyName.Contains("DomainLens.RepositoryPayload", StringComparison.OrdinalIgnoreCase) ||
            reference.RelativePath.Contains("DomainLens.RepositoryPayload", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.SemanticAnalysis.Observations, observation =>
            observation.ResolvedAssembly?.Contains(
                "DomainLens.RepositoryPayload",
                StringComparison.OrdinalIgnoreCase) == true);

        fixture.AssertRepositoryRemainsInert();
        Assert.Equal(AnalyzerWorkspaceCleanupStatus.Succeeded, result.CleanupStatus);
        Assert.NotNull(result.WorkspacePath);
        Assert.False(Directory.Exists(result.WorkspacePath));
        Assert.False(File.Exists(result.WorkspacePath));
    }

    private static string FormatFailure(AnalyzerProcessResult result) =>
        $"Outcome={result.Outcome}; Exit={result.ExitCode}; " +
        $"Diagnostics={string.Join(" | ", result.Diagnostics.Select(item => $"{item.Code}: {item.Message}"))}; " +
        $"StdOut={result.StandardOutput}; StdErr={result.StandardError}";

    private sealed class HostileRepositoryFixture : IDisposable
    {
        private HostileRepositoryFixture(
            string rootPath,
            string repositoryRoot,
            string configuration,
            string repositoryPath,
            string markerPath,
            string originalFixturePath)
        {
            RootPath = rootPath;
            RepositoryRoot = repositoryRoot;
            Configuration = configuration;
            RepositoryPath = repositoryPath;
            MarkerPath = markerPath;
            OriginalFixturePath = originalFixturePath;
        }

        private string RootPath { get; }

        private string RepositoryRoot { get; }

        private string Configuration { get; }

        private string MarkerPath { get; }

        private string OriginalFixturePath { get; }

        public string RepositoryPath { get; }

        public static HostileRepositoryFixture Create()
        {
            var repositoryRoot = FindRepositoryRoot();
            var configuration = GetBuildConfiguration();
            var rootPath = Path.Combine(
                Path.GetTempPath(),
                "DomainLens.Analyzer.HostileTests",
                Guid.NewGuid().ToString("N"));
            var repositoryPath = Path.Combine(rootPath, "repository-source");
            var markerPath = Path.Combine(rootPath, "side-effects", "repository-code-executed.marker");
            var originalFixturePath = Path.Combine(repositoryRoot, "tests", "Fixtures", "MaliciousBuild");

            Directory.CreateDirectory(repositoryPath);
            Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
            CopyDirectory(originalFixturePath, repositoryPath);
            CopyPayload(repositoryRoot, configuration, repositoryPath);

            var replacements = ReplaceMarkerPlaceholders(repositoryPath, markerPath);
            Assert.True(replacements >= 8, $"Expected expanded hostile fixture markers; replaced {replacements}.");
            Assert.False(File.Exists(markerPath));
            Assert.False(File.Exists(Path.Combine(repositoryPath, "SHOULD_NOT_EXIST.marker")));

            return new HostileRepositoryFixture(
                rootPath,
                repositoryRoot,
                configuration,
                repositoryPath,
                markerPath,
                originalFixturePath);
        }

        public AnalyzerProcessHost CreateRealHost(AnalyzerProcessLimits limits)
        {
            var workerAssembly = Path.Combine(
                RepositoryRoot,
                "src",
                "DomainLens.Analyzer.Worker",
                "bin",
                Configuration,
                "net10.0",
                "DomainLens.Analyzer.Worker.dll");
            Assert.True(File.Exists(workerAssembly), $"Worker assembly not found: {workerAssembly}");

            var command = new AnalyzerWorkerCommand(
                FindDotNetHost(RepositoryRoot),
                workerAssembly,
                Array.Empty<string>());
            return new AnalyzerProcessHost(command, limits, Path.Combine(RootPath, "jobs"));
        }

        public void AssertRepositoryRemainsInert()
        {
            Assert.False(File.Exists(MarkerPath), "Repository-controlled code wrote the external marker.");
            Assert.False(File.Exists(Path.Combine(RepositoryPath, "SHOULD_NOT_EXIST.marker")));
            Assert.False(File.Exists(Path.Combine(OriginalFixturePath, "SHOULD_NOT_EXIST.marker")));
            Assert.Empty(Directory.EnumerateDirectories(RepositoryPath, "bin", SearchOption.AllDirectories));
            Assert.Empty(Directory.EnumerateDirectories(RepositoryPath, "obj", SearchOption.AllDirectories));
            Assert.Empty(Directory.EnumerateFiles(
                RepositoryPath,
                "project.assets.json",
                SearchOption.AllDirectories));
            Assert.Contains(
                MarkerPlaceholder,
                File.ReadAllText(Path.Combine(OriginalFixturePath, "MaliciousBuild.csproj")),
                StringComparison.Ordinal);
        }

        public void Dispose()
        {
            if (!Directory.Exists(RootPath))
            {
                return;
            }

            var expectedParent = Path.GetFullPath(Path.Combine(
                Path.GetTempPath(),
                "DomainLens.Analyzer.HostileTests"));
            var candidate = Path.GetFullPath(RootPath);
            var relative = Path.GetRelativePath(expectedParent, candidate);
            if (Path.IsPathRooted(relative) ||
                relative.StartsWith("..", StringComparison.Ordinal) ||
                relative.Length == 0)
            {
                throw new InvalidOperationException("Refusing to remove an unexpected hostile test directory.");
            }

            Directory.Delete(candidate, recursive: true);
        }

        private static void CopyPayload(
            string repositoryRoot,
            string configuration,
            string repositoryPath)
        {
            var payloadOutput = Path.Combine(
                repositoryRoot,
                "tests",
                "DomainLens.RepositoryPayload",
                "bin",
                configuration,
                "netstandard2.0");
            var payloadDirectory = Path.Combine(repositoryPath, "RepositoryPayload");
            Directory.CreateDirectory(payloadDirectory);

            foreach (var fileName in new[]
                     {
                         "DomainLens.RepositoryPayload.dll",
                         "DomainLens.RepositoryPayload.marker-path.txt",
                     })
            {
                var sourcePath = Path.Combine(payloadOutput, fileName);
                Assert.True(File.Exists(sourcePath), $"Controlled payload output not found: {sourcePath}");
                File.Copy(sourcePath, Path.Combine(payloadDirectory, fileName));
            }
        }

        private static int ReplaceMarkerPlaceholders(string repositoryPath, string markerPath)
        {
            var replacementCount = 0;
            foreach (var path in Directory.EnumerateFiles(repositoryPath, "*", SearchOption.AllDirectories)
                         .Where(IsTextFixtureFile))
            {
                var content = File.ReadAllText(path);
                var count = CountOccurrences(content, MarkerPlaceholder);
                if (count == 0)
                {
                    continue;
                }

                var replacement = string.Equals(Path.GetExtension(path), ".ps1", StringComparison.OrdinalIgnoreCase)
                    ? EscapePowerShellDoubleQuotedLiteral(markerPath)
                    : IsXmlFile(path)
                        ? SecurityElement.Escape(markerPath) ?? markerPath
                        : markerPath;
                File.WriteAllText(path, content.Replace(MarkerPlaceholder, replacement, StringComparison.Ordinal));
                replacementCount += count;
            }

            return replacementCount;
        }

        private static bool IsTextFixtureFile(string path) =>
            IsXmlFile(path) ||
            string.Equals(Path.GetExtension(path), ".ps1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetExtension(path), ".txt", StringComparison.OrdinalIgnoreCase);

        private static bool IsXmlFile(string path)
        {
            var extension = Path.GetExtension(path);
            return extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".props", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".targets", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".config", StringComparison.OrdinalIgnoreCase);
        }

        private static string EscapePowerShellDoubleQuotedLiteral(string value) =>
            value.Replace("`", "``", StringComparison.Ordinal)
                .Replace("$", "`$", StringComparison.Ordinal)
                .Replace("\"", "`\"", StringComparison.Ordinal);

        private static int CountOccurrences(string value, string search)
        {
            var count = 0;
            var index = 0;
            while ((index = value.IndexOf(search, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += search.Length;
            }

            return count;
        }

        private static void CopyDirectory(string source, string destination)
        {
            foreach (var sourcePath in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(source, sourcePath);
                var targetPath = Path.Combine(destination, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                File.Copy(sourcePath, targetPath);
            }
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
