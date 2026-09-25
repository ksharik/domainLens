using DomainLens.Core;

namespace DomainLens.Semantics;

/// <summary>A structural integrity problem in a legacy semantic-analysis result.</summary>
public sealed record SemanticResultValidationIssue(
    string Code,
    DiagnosticSeverity Severity,
    string Message,
    string? SubjectId = null);

/// <summary>The complete structural validation outcome for a semantic-analysis result.</summary>
public sealed record SemanticResultValidationResult(IReadOnlyList<SemanticResultValidationIssue> Issues)
{
    public bool IsValid => Issues.All(issue => issue.Severity != DiagnosticSeverity.Error);

    public void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            throw new InvalidDataException(
                "The legacy semantic-analysis result is invalid: " +
                string.Join("; ", Issues
                    .Where(issue => issue.Severity == DiagnosticSeverity.Error)
                    .Select(issue => $"{issue.Code}: {issue.Message}")));
        }
    }
}

/// <summary>
/// Validates untrusted semantic-result structures against the trusted scanner manifest before
/// they are persisted or admitted back into trusted application state.
/// </summary>
public static class LegacySemanticAnalysisValidator
{
    public const string Net472ReferenceSetId =
        TrustedNet472ReferenceCatalog.ReferenceSetId;

    /// <summary>
    /// Validates the result's intrinsic structure and safe repository-relative path forms when a
    /// scanner manifest is not available at the serialization boundary.
    /// </summary>
    public static SemanticResultValidationResult Validate(LegacySemanticAnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var issues = new List<SemanticResultValidationIssue>();
        var expectedReferences = GetExpectedReferences(issues);
        ValidateCompilationEnvelope(result, issues);
        ValidateReferenceSet(result, expectedReferences, issues);
        ValidateObservations(result.Observations, null, expectedReferences, null, issues);
        ValidateDiagnostics(result.Diagnostics, null, issues);
        return Result(issues);
    }

    public static SemanticResultValidationResult Validate(
        LegacySemanticAnalysisResult result,
        AnalysisDocument analysisDocument)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(analysisDocument);

        var issues = new List<SemanticResultValidationIssue>();
        var manifest = BuildManifestIndex(analysisDocument, issues);
        var expectedReferences = GetExpectedReferences(issues);
        var expectedSourceAssembly = analysisDocument.Snapshot is null
            ? null
            : $"DomainLens.Legacy.{analysisDocument.Snapshot.SnapshotId.Replace(':', '.')}";

        ValidateCompilationEnvelope(result, issues);
        ValidateReferenceSet(result, expectedReferences, issues);
        ValidateObservations(
            result.Observations,
            manifest,
            expectedReferences,
            expectedSourceAssembly,
            issues);
        ValidateDiagnostics(result.Diagnostics, manifest, issues);

        return Result(issues);
    }

    public static void ValidateAndThrow(LegacySemanticAnalysisResult result) =>
        Validate(result).ThrowIfInvalid();

    public static void ValidateAndThrow(
        LegacySemanticAnalysisResult result,
        AnalysisDocument analysisDocument) =>
        Validate(result, analysisDocument).ThrowIfInvalid();

    private static IReadOnlyDictionary<string, ManifestEntry> BuildManifestIndex(
        AnalysisDocument analysisDocument,
        ICollection<SemanticResultValidationIssue> issues)
    {
        var index = new Dictionary<string, ManifestEntry>(StringComparer.Ordinal);
        if (analysisDocument.Snapshot?.Manifest is null)
        {
            Error(issues, "manifest.missing", "The analysis document requires a snapshot manifest.");
            return index;
        }

        foreach (var entry in analysisDocument.Snapshot.Manifest)
        {
            if (entry is null || !TryGetCanonicalRelativePath(entry.Path, out var path) ||
                !string.Equals(entry.Path, path, StringComparison.Ordinal))
            {
                Error(issues, "manifest.path.invalid", "The analysis manifest contains a noncanonical path.");
                continue;
            }

            if (!index.TryAdd(path, entry))
            {
                Error(issues, "manifest.path.duplicate", "The analysis manifest contains a duplicate path.", path);
            }
        }

        return index;
    }

    private static IReadOnlyList<TrustedMetadataReference>? GetExpectedReferences(
        ICollection<SemanticResultValidationIssue> issues)
    {
        try
        {
            return TrustedNet472ReferenceCatalog.GetReferenceDescriptors();
        }
        catch (InvalidOperationException)
        {
            Error(
                issues,
                "references.catalog.unavailable",
                "The versioned tool-owned metadata-reference catalog is unavailable or corrupt.");
            return null;
        }
    }

    private static void ValidateCompilationEnvelope(
        LegacySemanticAnalysisResult result,
        ICollection<SemanticResultValidationIssue> issues)
    {
        if (!Enum.IsDefined(result.CompilationScope) ||
            result.CompilationScope != SemanticCompilationScope.RepositoryManifestCSharpSources)
        {
            Error(
                issues,
                "compilation.scope.invalid",
                "The semantic result has an unsupported or undefined compilation scope.");
        }

        if (!Enum.IsDefined(result.CompilationResolutionQuality) ||
            result.CompilationResolutionQuality != ResolutionQuality.Partial)
        {
            Error(
                issues,
                "compilation.quality.invalid",
                "Repository-wide synthetic compilation results must declare partial resolution quality.");
        }
    }

    private static void ValidateReferenceSet(
        LegacySemanticAnalysisResult result,
        IReadOnlyList<TrustedMetadataReference>? expectedReferences,
        ICollection<SemanticResultValidationIssue> issues)
    {
        if (!string.Equals(result.ReferenceSetId, Net472ReferenceSetId, StringComparison.Ordinal))
        {
            Error(
                issues,
                "references.set.invalid",
                "The semantic result has an unsupported or malformed trusted reference-set identity.");
        }

        if (result.MetadataReferences is null)
        {
            Error(issues, "references.missing", "The metadata-reference collection is required.");
            return;
        }

        var paths = new HashSet<string>(StringComparer.Ordinal);
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < result.MetadataReferences.Count; index++)
        {
            var reference = result.MetadataReferences[index];
            var subject = $"metadataReferences[{index}]";
            if (reference is null)
            {
                Error(issues, "reference.null", "The metadata-reference collection cannot contain null values.", subject);
                continue;
            }

            if (!IsSimpleAssemblyName(reference.AssemblyName))
            {
                Error(issues, "reference.assembly.invalid", "A metadata reference has an invalid assembly identity.", subject);
            }

            if (!TryGetCanonicalRelativePath(reference.RelativePath, out var relativePath) ||
                !string.Equals(reference.RelativePath, relativePath, StringComparison.Ordinal) ||
                !IsTrustedReferenceRelativePath(relativePath))
            {
                Error(
                    issues,
                    "reference.path.invalid",
                    "A metadata reference path must be a canonical filename in the trusted reference set.",
                    subject);
                continue;
            }

            if (!paths.Add(relativePath))
            {
                Error(issues, "reference.path.duplicate", "A metadata reference path appears more than once.", subject);
            }

            if (!identities.Add(reference.AssemblyName))
            {
                Error(issues, "reference.assembly.duplicate", "A metadata assembly identity appears more than once.", subject);
            }

            var expectedFileName = reference.AssemblyName + ".dll";
            if (!string.Equals(Path.GetFileName(relativePath), expectedFileName, StringComparison.OrdinalIgnoreCase))
            {
                Error(
                    issues,
                    "reference.identity.mismatch",
                    "A metadata-reference filename does not match its assembly identity.",
                    subject);
            }
        }

        if (result.MetadataReferences.Count == 0 ||
            !result.MetadataReferences.Any(reference =>
                reference is not null &&
                string.Equals(reference.AssemblyName, "mscorlib", StringComparison.OrdinalIgnoreCase)))
        {
            Error(issues, "references.core.missing", "The trusted metadata references must include mscorlib.");
        }

        if (expectedReferences is null)
        {
            return;
        }

        if (result.MetadataReferences.Count != expectedReferences.Count)
        {
            Error(
                issues,
                "references.catalog.count",
                "The metadata-reference collection does not match the exact versioned tool catalog.");
        }

        var expectedByPath = expectedReferences.ToDictionary(
            reference => reference.RelativePath,
            StringComparer.Ordinal);
        var actualPaths = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < result.MetadataReferences.Count; index++)
        {
            var reference = result.MetadataReferences[index];
            if (reference is null)
            {
                continue;
            }

            actualPaths.Add(reference.RelativePath);
            if (!expectedByPath.TryGetValue(reference.RelativePath, out var expected))
            {
                Error(
                    issues,
                    "reference.catalog.unknown",
                    "A metadata reference is not part of the exact versioned tool catalog.",
                    $"metadataReferences[{index}]");
            }
            else if (!string.Equals(reference.AssemblyName, expected.AssemblyName, StringComparison.Ordinal))
            {
                Error(
                    issues,
                    "reference.catalog.identity",
                    "A metadata reference does not match the catalog assembly identity for its path.",
                    $"metadataReferences[{index}]");
            }
        }

        foreach (var expected in expectedReferences)
        {
            if (!actualPaths.Contains(expected.RelativePath))
            {
                Error(
                    issues,
                    "reference.catalog.missing",
                    "A required metadata reference from the exact versioned tool catalog is missing.",
                    expected.RelativePath);
            }
        }
    }

    private static void ValidateObservations(
        IReadOnlyList<SemanticBindingObservation>? observations,
        IReadOnlyDictionary<string, ManifestEntry>? manifest,
        IReadOnlyList<TrustedMetadataReference>? expectedReferences,
        string? expectedSourceAssembly,
        ICollection<SemanticResultValidationIssue> issues)
    {
        if (observations is null)
        {
            Error(issues, "observations.missing", "The semantic-observation collection is required.");
            return;
        }

        for (var index = 0; index < observations.Count; index++)
        {
            var observation = observations[index];
            var subject = $"observations[{index}]";
            if (observation is null)
            {
                Error(issues, "observation.null", "The semantic-observation collection cannot contain null values.", subject);
                continue;
            }

            if (!Enum.IsDefined(observation.Kind))
            {
                Error(issues, "observation.kind.invalid", "A semantic observation has an undefined kind.", subject);
            }

            if (!Enum.IsDefined(observation.Quality))
            {
                Error(issues, "observation.quality.invalid", "A semantic observation has an undefined resolution quality.", subject);
            }

            if (!TryGetCanonicalRelativePath(observation.RelativePath, out var canonicalPath) ||
                !string.Equals(observation.RelativePath, canonicalPath, StringComparison.Ordinal))
            {
                Error(
                    issues,
                    "observation.path.invalid",
                    "A semantic-observation path must be canonical and repository-relative.",
                    subject);
            }
            else
            {
                if (!string.Equals(Path.GetExtension(observation.RelativePath), ".cs", StringComparison.OrdinalIgnoreCase))
                {
                    Error(issues, "observation.path.not-csharp", "A semantic observation must refer to a C# source file.", subject);
                }

                ManifestEntry? entry = null;
                if (manifest is not null && !manifest.TryGetValue(canonicalPath, out entry))
                {
                    Error(
                        issues,
                        "observation.path.unresolved",
                        "A semantic-observation path must resolve to the snapshot manifest.",
                        subject);
                }

                ValidateSpan(observation.Span, entry?.Length, "observation", subject, issues);
            }

            if (string.IsNullOrWhiteSpace(observation.SourceExpression))
            {
                Error(issues, "observation.expression.missing", "A semantic observation requires its source expression.", subject);
            }

            ValidateCandidates(observation.CandidateSymbols, subject, issues);
            ValidateResolutionShape(observation, subject, issues);
            ValidateResolvedAssembly(
                observation,
                expectedReferences,
                expectedSourceAssembly,
                subject,
                issues);
        }
    }

    private static void ValidateResolvedAssembly(
        SemanticBindingObservation observation,
        IReadOnlyList<TrustedMetadataReference>? expectedReferences,
        string? expectedSourceAssembly,
        string subject,
        ICollection<SemanticResultValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(observation.ResolvedAssembly) || expectedReferences is null)
        {
            return;
        }

        var isTrustedMetadataAssembly = expectedReferences.Any(reference =>
            string.Equals(
                reference.AssemblyName,
                observation.ResolvedAssembly,
                StringComparison.Ordinal));
        var isSyntheticSourceAssembly = expectedSourceAssembly is null
            ? IsSyntheticSourceAssemblyName(observation.ResolvedAssembly)
            : string.Equals(
                observation.ResolvedAssembly,
                expectedSourceAssembly,
                StringComparison.Ordinal);
        if (isSyntheticSourceAssembly && observation.Quality == ResolutionQuality.Exact)
        {
            Error(
                issues,
                "observation.source-quality.invalid",
                "A repository-source binding from the synthetic compilation cannot claim exact resolution quality.",
                subject);
        }

        if (!isTrustedMetadataAssembly && !isSyntheticSourceAssembly)
        {
            Error(
                issues,
                "observation.assembly.untrusted",
                "A resolved assembly must be the snapshot-specific synthetic source assembly or an assembly in the exact trusted catalog.",
                subject);
        }
    }

    private static void ValidateDiagnostics(
        IReadOnlyList<SemanticAnalysisDiagnostic>? diagnostics,
        IReadOnlyDictionary<string, ManifestEntry>? manifest,
        ICollection<SemanticResultValidationIssue> issues)
    {
        if (diagnostics is null)
        {
            Error(issues, "diagnostics.missing", "The semantic-diagnostic collection is required.");
            return;
        }

        for (var index = 0; index < diagnostics.Count; index++)
        {
            var diagnostic = diagnostics[index];
            var subject = $"diagnostics[{index}]";
            if (diagnostic is null)
            {
                Error(issues, "diagnostic.null", "The semantic-diagnostic collection cannot contain null values.", subject);
                continue;
            }

            if (!Enum.IsDefined(diagnostic.Code))
            {
                Error(issues, "diagnostic.code.invalid", "A semantic diagnostic has an undefined code.", subject);
            }

            if (!Enum.IsDefined(diagnostic.Severity))
            {
                Error(issues, "diagnostic.severity.invalid", "A semantic diagnostic has an undefined severity.", subject);
            }

            if (string.IsNullOrWhiteSpace(diagnostic.Message))
            {
                Error(issues, "diagnostic.message.missing", "A semantic diagnostic requires a message.", subject);
            }

            ManifestEntry? entry = null;
            var pathIsValid = diagnostic.RelativePath is null;
            if (diagnostic.RelativePath is not null)
            {
                if (!TryGetCanonicalRelativePath(diagnostic.RelativePath, out var canonicalPath) ||
                    !string.Equals(diagnostic.RelativePath, canonicalPath, StringComparison.Ordinal))
                {
                    Error(
                        issues,
                        "diagnostic.path.invalid",
                        "A semantic-diagnostic path must be canonical and repository-relative.",
                        subject);
                }
                else
                {
                    pathIsValid = true;
                    if (manifest is not null && !manifest.TryGetValue(canonicalPath, out entry))
                    {
                        pathIsValid = false;
                        Error(
                            issues,
                            "diagnostic.path.unresolved",
                            "A semantic-diagnostic path must resolve to the snapshot manifest.",
                            subject);
                    }
                }
            }

            if (diagnostic.Span is not null)
            {
                if (diagnostic.RelativePath is null || !pathIsValid)
                {
                    Error(issues, "diagnostic.span.unbound", "A diagnostic span requires a valid manifest path.", subject);
                }
                else
                {
                    ValidateSpan(diagnostic.Span, entry?.Length, "diagnostic", subject, issues);
                }
            }

            var compilerDiagnostic = diagnostic.Code is
                SemanticDiagnosticCode.ParseError or
                SemanticDiagnosticCode.CompilationError or
                SemanticDiagnosticCode.CompilationWarning;
            if (compilerDiagnostic != !string.IsNullOrWhiteSpace(diagnostic.CompilerDiagnosticId))
            {
                Error(
                    issues,
                    "diagnostic.compiler-id.invalid",
                    "Compiler diagnostics require a compiler ID and non-compiler diagnostics must not carry one.",
                    subject);
            }
            else if (compilerDiagnostic && !IsCompilerDiagnosticId(diagnostic.CompilerDiagnosticId!))
            {
                Error(issues, "diagnostic.compiler-id.format", "A compiler diagnostic has a malformed compiler ID.", subject);
            }

            var expectedSeverity = diagnostic.Code == SemanticDiagnosticCode.CompilationWarning
                ? DiagnosticSeverity.Warning
                : DiagnosticSeverity.Error;
            if (Enum.IsDefined(diagnostic.Code) && diagnostic.Severity != expectedSeverity)
            {
                Error(issues, "diagnostic.severity.mismatch", "A diagnostic severity does not match its typed code.", subject);
            }
        }
    }

    private static void ValidateCandidates(
        IReadOnlyList<string>? candidates,
        string subject,
        ICollection<SemanticResultValidationIssue> issues)
    {
        if (candidates is null)
        {
            Error(issues, "observation.candidates.missing", "A semantic observation requires a candidate-symbol collection.", subject);
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                Error(issues, "observation.candidate.invalid", "Candidate symbols cannot be empty.", subject);
            }
            else if (!seen.Add(candidate))
            {
                Error(issues, "observation.candidate.duplicate", "Candidate symbols cannot be repeated.", subject);
            }
        }
    }

    private static void ValidateResolutionShape(
        SemanticBindingObservation observation,
        string subject,
        ICollection<SemanticResultValidationIssue> issues)
    {
        var candidateCount = observation.CandidateSymbols?.Count ?? 0;
        var hasSymbol = !string.IsNullOrWhiteSpace(observation.ResolvedSymbol);
        var hasAssembly = !string.IsNullOrWhiteSpace(observation.ResolvedAssembly);
        var valid = observation.Quality switch
        {
            ResolutionQuality.Exact => hasSymbol && hasAssembly && candidateCount == 0,
            ResolutionQuality.Partial =>
                (hasSymbol && hasAssembly && candidateCount == 0) ||
                (!hasSymbol && !hasAssembly && candidateCount == 1),
            ResolutionQuality.Ambiguous => !hasSymbol && !hasAssembly && candidateCount > 1,
            ResolutionQuality.Unresolved => !hasSymbol && !hasAssembly && candidateCount == 0,
            _ => false
        };

        if (!valid)
        {
            Error(
                issues,
                "observation.resolution.invalid",
                "An observation's resolved symbol, assembly, candidates, and quality are inconsistent.",
                subject);
        }
    }

    private static void ValidateSpan(
        SourceSpan? span,
        long? manifestLength,
        string owner,
        string subject,
        ICollection<SemanticResultValidationIssue> issues)
    {
        if (span is null)
        {
            Error(issues, $"{owner}.span.missing", "A source-backed result requires a span.", subject);
            return;
        }

        var endOffset = (long)span.StartOffset + span.Length;
        var valid = span.StartOffset >= 0 &&
                    span.Length >= 0 &&
                    (manifestLength is null || endOffset <= manifestLength.Value) &&
                    span.StartLine >= 1 &&
                    span.StartColumn >= 1 &&
                    span.EndLine >= span.StartLine &&
                    span.EndColumn >= 1 &&
                    (span.EndLine != span.StartLine || span.EndColumn >= span.StartColumn);
        if (!valid)
        {
            Error(issues, $"{owner}.span.invalid", "A source span has invalid coordinates or exceeds its manifest file.", subject);
        }
    }

    private static bool TryGetCanonicalRelativePath(string? candidate, out string canonical)
    {
        canonical = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate) || Path.IsPathRooted(candidate))
        {
            return false;
        }

        try
        {
            canonical = CanonicalIdentity.NormalizeRepositoryPath(candidate);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsTrustedReferenceRelativePath(string relativePath)
    {
        var segments = relativePath.Split('/');
        return segments.Length switch
        {
            1 => segments[0].EndsWith(".dll", StringComparison.OrdinalIgnoreCase),
            2 => string.Equals(segments[0], "Facades", StringComparison.Ordinal) &&
                 segments[1].EndsWith(".dll", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static bool IsSimpleAssemblyName(string? assemblyName) =>
        !string.IsNullOrWhiteSpace(assemblyName) &&
        !assemblyName.Contains('/') &&
        !assemblyName.Contains('\\') &&
        !assemblyName.Contains(':') &&
        !assemblyName.Contains(',') &&
        assemblyName is not "." and not "..";

    private static bool IsCompilerDiagnosticId(string value) =>
        value.Length >= 3 &&
        char.IsAsciiLetter(value[0]) &&
        char.IsAsciiLetter(value[1]) &&
        value[2..].All(char.IsAsciiDigit);

    private static bool IsSyntheticSourceAssemblyName(string value)
    {
        const string prefix = "DomainLens.Legacy.snapshot.";
        return value.StartsWith(prefix, StringComparison.Ordinal) &&
               value.Length == prefix.Length + 64 &&
               value[prefix.Length..].All(character =>
                   character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }

    private static SemanticResultValidationResult Result(
        IEnumerable<SemanticResultValidationIssue> issues) =>
        new(issues
            .OrderBy(issue => issue.Severity)
            .ThenBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.SubjectId, StringComparer.Ordinal)
            .ThenBy(issue => issue.Message, StringComparer.Ordinal)
            .ToArray());

    private static void Error(
        ICollection<SemanticResultValidationIssue> issues,
        string code,
        string message,
        string? subjectId = null) =>
        issues.Add(new SemanticResultValidationIssue(code, DiagnosticSeverity.Error, message, subjectId));
}
