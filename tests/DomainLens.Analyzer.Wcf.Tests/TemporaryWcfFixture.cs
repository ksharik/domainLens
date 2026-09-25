namespace DomainLens.Analyzer.Wcf.Tests;

internal sealed class TemporaryWcfFixture : IDisposable
{
    internal const string ExtensionMarkerPlaceholder = "__DOMAINLENS_WCF_EXTENSION_MARKER__";

    private readonly string _temporaryRoot;

    private TemporaryWcfFixture(string path, string temporaryRoot, string extensionMarkerPath)
    {
        Path = path;
        _temporaryRoot = temporaryRoot;
        ExtensionMarkerPath = extensionMarkerPath;
    }

    public string Path { get; }

    public string ExtensionMarkerPath { get; }

    public static TemporaryWcfFixture Copy(string fixtureName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fixtureName);

        var source = System.IO.Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            fixtureName);
        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException(
                $"WCF test fixture '{fixtureName}' was not copied to the test output.");
        }

        var temporaryRoot = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "DomainLens.Wcf.Tests",
            Guid.NewGuid().ToString("N"));
        var destination = System.IO.Path.Combine(temporaryRoot, fixtureName);
        var extensionMarkerPath = System.IO.Path.Combine(
            temporaryRoot,
            "side-effects",
            "WCF_EXTENSION_SHOULD_NOT_EXIST.marker");
        Directory.CreateDirectory(destination);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(extensionMarkerPath)!);

        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = System.IO.Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(System.IO.Path.Combine(destination, relative));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = System.IO.Path.GetRelativePath(source, file);
            var target = System.IO.Path.Combine(destination, relative);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);
        }

        var replacementCount = InjectExtensionMarkerPath(destination, extensionMarkerPath);
        if (string.Equals(fixtureName, "WcfAmbiguousHostile", StringComparison.Ordinal) &&
            replacementCount != 1)
        {
            throw new InvalidDataException(
                $"Expected exactly one hostile WCF extension marker placeholder, but found {replacementCount}.");
        }

        return new TemporaryWcfFixture(destination, temporaryRoot, extensionMarkerPath);
    }

    private static int InjectExtensionMarkerPath(string destination, string markerPath)
    {
        var replacementCount = 0;
        var escapedMarkerPath = markerPath
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
        foreach (var path in Directory.EnumerateFiles(destination, "*.cs", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(path);
            var count = content.Split(
                ExtensionMarkerPlaceholder,
                StringSplitOptions.None).Length - 1;
            if (count == 0)
            {
                continue;
            }

            File.WriteAllText(
                path,
                content.Replace(
                    ExtensionMarkerPlaceholder,
                    escapedMarkerPath,
                    StringComparison.Ordinal));
            replacementCount += count;
        }

        return replacementCount;
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryRoot))
        {
            Directory.Delete(_temporaryRoot, recursive: true);
        }
    }
}
