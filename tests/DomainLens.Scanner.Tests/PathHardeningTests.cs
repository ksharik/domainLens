using System.Security.Cryptography;
using DomainLens.Core;
using DomainLens.Scanner;

namespace DomainLens.Scanner.Tests;

public sealed class PathHardeningTests
{
    [Fact]
    public async Task Solution_reader_rejects_portable_absolute_unc_and_escaping_project_paths()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(
            "Safe/Safe.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>");
        repository.Write("Safe/SafeType.cs", "namespace Safe; public sealed class SafeType { }");

        var solutionText = string.Join('\n',
            "Microsoft Visual Studio Solution File, Format Version 12.00",
            "Project(\"{11111111-1111-1111-1111-111111111111}\") = \"Safe\", \"Safe\\Safe.csproj\", \"{AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}\"",
            "Project(\"{11111111-1111-1111-1111-111111111111}\") = \"Drive\", \"C:\\outside\\Drive.csproj\", \"{BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB}\"",
            "Project(\"{11111111-1111-1111-1111-111111111111}\") = \"Unc\", \"\\\\server\\share\\Unc.csproj\", \"{CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCCC}\"",
            "Project(\"{11111111-1111-1111-1111-111111111111}\") = \"Unix\", \"/outside/Unix.csproj\", \"{DDDDDDDD-DDDD-DDDD-DDDD-DDDDDDDDDDDD}\"",
            "Project(\"{11111111-1111-1111-1111-111111111111}\") = \"Escape\", \"..\\outside\\Escape.csproj\", \"{EEEEEEEE-EEEE-EEEE-EEEE-EEEEEEEEEEEE}\"");
        repository.Write("Repository.sln", solutionText);

        var document = await new RepositoryScanner().AnalyzeAsync(
            new ScannerOptions(repository.Path, "Repository.sln"));

        Assert.Equal(AnalysisStatus.PartialSuccess, document.Status);
        var project = Assert.Single(document.Nodes, node => node.Kind == "Project");
        Assert.Equal("Safe/Safe.csproj", project.QualifiedName);
        Assert.Equal(4, document.Diagnostics.Count(diagnostic => diagnostic.Code == "DL2001"));

        var solution = Assert.Single(document.Nodes, node => node.Kind == "Solution");
        var solutionEvidence = Assert.Single(document.Evidence, evidence =>
            solution.EvidenceIds.Contains(evidence.EvidenceId, StringComparer.Ordinal) &&
            evidence.Provenance.RuleId == "solution.declaration");
        Assert.Equal(solutionText.Length, solutionEvidence.Span.Length);
        Assert.Equal(6, solutionEvidence.Span.EndLine);
        Assert.Equal(solutionText[(solutionText.LastIndexOf('\n') + 1)..].Length + 1, solutionEvidence.Span.EndColumn);
    }

    [Fact]
    public async Task Inventory_hashes_the_exact_bounded_bytes_and_excludes_oversized_files()
    {
        using var repository = TemporaryRepository.Create();
        repository.Write(
            "Bounded.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>");
        repository.Write("BoundedType.cs", "namespace Bounded; public sealed class BoundedType { }");
        repository.WriteBytes("data.bin", Enumerable.Range(0, 64).Select(value => (byte)value).ToArray());
        repository.WriteBytes("oversized.bin", new byte[129]);

        var document = await new RepositoryScanner().AnalyzeAsync(
            new ScannerOptions(repository.Path, MaximumFileSizeBytes: 128));

        Assert.Equal(AnalysisStatus.PartialSuccess, document.Status);
        Assert.DoesNotContain(document.Snapshot.Manifest, item => item.Path == "oversized.bin");
        Assert.Contains(document.Diagnostics, diagnostic =>
            diagnostic.Code == "DL1004" &&
            diagnostic.Properties.TryGetValue("subjectPath", out var subject) &&
            subject == "oversized.bin");

        foreach (var manifestEntry in document.Snapshot.Manifest)
        {
            var bytes = await File.ReadAllBytesAsync(
                System.IO.Path.Combine(repository.Path, manifestEntry.Path.Replace('/', System.IO.Path.DirectorySeparatorChar)));
            Assert.Equal(bytes.LongLength, manifestEntry.Length);
            Assert.Equal(
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                manifestEntry.ContentHash);
        }
    }

    [Fact]
    public async Task Inventory_enforces_the_aggregate_read_budget()
    {
        using var repository = TemporaryRepository.Create();
        var project = System.Text.Encoding.UTF8.GetBytes(
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>");
        var source = System.Text.Encoding.UTF8.GetBytes(
            "namespace Bounded; public sealed class BoundedType { }");
        repository.WriteBytes("Bounded.csproj", project);
        repository.WriteBytes("BoundedType.cs", source);

        var document = await new RepositoryScanner().AnalyzeAsync(
            new ScannerOptions(
                repository.Path,
                MaximumFileSizeBytes: 1024,
                MaximumTotalBytesRead: project.LongLength + source.LongLength - 1));

        Assert.Equal(AnalysisStatus.Failure, document.Status);
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "DL0001");
        Assert.Empty(document.Snapshot.Manifest);
    }

    private sealed class TemporaryRepository : IDisposable
    {
        private TemporaryRepository(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryRepository Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "DomainLens.PathHardening.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TemporaryRepository(path);
        }

        public void Write(string relativePath, string content) =>
            WriteBytes(relativePath, System.Text.Encoding.UTF8.GetBytes(content));

        public void WriteBytes(string relativePath, byte[] content)
        {
            var path = System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, content);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup for a test-owned temporary repository.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup for a test-owned temporary repository.
            }
        }
    }
}
