namespace DomainLens.Scanner;

public sealed record ScannerOptions(
    string RepositoryPath,
    string? Solution = null,
    int MaximumFileCount = 100_000,
    long MaximumFileSizeBytes = 16 * 1024 * 1024,
    long MaximumTotalBytesRead = 1024L * 1024 * 1024);
