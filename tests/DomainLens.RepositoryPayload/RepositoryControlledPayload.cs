using System;
using System.Collections.Immutable;
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DomainLens.RepositoryPayload;

/// <summary>
/// A controlled payload used to prove that repository-declared analyzers remain inert.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RepositoryControlledAnalyzer : DiagnosticAnalyzer
{
    public RepositoryControlledAnalyzer()
    {
        PayloadMarker.RecordInstantiation(nameof(RepositoryControlledAnalyzer));
    }

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray<DiagnosticDescriptor>.Empty;

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
    }
}

/// <summary>
/// A controlled payload used to prove that repository-declared generators remain inert.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class RepositoryControlledGenerator : IIncrementalGenerator
{
    public RepositoryControlledGenerator()
    {
        PayloadMarker.RecordInstantiation(nameof(RepositoryControlledGenerator));
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
    }
}

internal static class PayloadMarker
{
    internal const string MarkerPathFileName = "DomainLens.RepositoryPayload.marker-path.txt";

    internal static void RecordInstantiation(string componentName)
    {
        var assemblyPath = typeof(PayloadMarker).Assembly.Location;
        var assemblyDirectory = Path.GetDirectoryName(assemblyPath);
        if (string.IsNullOrWhiteSpace(assemblyDirectory))
        {
            return;
        }

        var sidecarPath = Path.Combine(assemblyDirectory, MarkerPathFileName);
        if (!File.Exists(sidecarPath))
        {
            return;
        }

        var markerPath = File.ReadAllText(sidecarPath).Trim();
        if (string.IsNullOrWhiteSpace(markerPath))
        {
            return;
        }

        File.AppendAllText(markerPath, componentName + Environment.NewLine);
    }
}
