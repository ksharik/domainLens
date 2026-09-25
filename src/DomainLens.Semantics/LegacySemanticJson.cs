using System.Text.Json;
using System.Text.Json.Serialization;
using DomainLens.Core;

namespace DomainLens.Semantics;

/// <summary>
/// Strict JSON persistence for validated semantic-analysis results. Repository content cannot add
/// fields or numeric enum values to the trusted result contract.
/// </summary>
public static class LegacySemanticAnalysisJson
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    public static string Serialize(
        LegacySemanticAnalysisResult result,
        bool indented = true)
    {
        LegacySemanticAnalysisValidator.ValidateAndThrow(result);
        return JsonSerializer.Serialize(Normalize(result), SerializerOptionsWithIndent(indented));
    }

    public static string Serialize(
        LegacySemanticAnalysisResult result,
        AnalysisDocument analysisDocument,
        bool indented = true)
    {
        LegacySemanticAnalysisValidator.ValidateAndThrow(result, analysisDocument);
        return JsonSerializer.Serialize(Normalize(result), SerializerOptionsWithIndent(indented));
    }

    public static LegacySemanticAnalysisResult Deserialize(string json)
    {
        var result = DeserializeCore(json);
        LegacySemanticAnalysisValidator.ValidateAndThrow(result);
        return Normalize(result);
    }

    public static LegacySemanticAnalysisResult Deserialize(
        string json,
        AnalysisDocument analysisDocument)
    {
        ArgumentNullException.ThrowIfNull(analysisDocument);
        var result = DeserializeCore(json);
        LegacySemanticAnalysisValidator.ValidateAndThrow(result, analysisDocument);
        return Normalize(result);
    }

    /// <summary>Returns a stable ordering and defensive copies for all result collections.</summary>
    public static LegacySemanticAnalysisResult Normalize(LegacySemanticAnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var observations = (result.Observations ?? Array.Empty<SemanticBindingObservation>())
            .Select(observation => observation with
            {
                CandidateSymbols = (observation.CandidateSymbols ?? Array.Empty<string>())
                    .OrderBy(candidate => candidate, StringComparer.Ordinal)
                    .ToArray()
            })
            .OrderBy(observation => observation.RelativePath, StringComparer.Ordinal)
            .ThenBy(observation => observation.Span?.StartOffset ?? -1)
            .ThenBy(observation => observation.Kind)
            .ThenBy(observation => observation.ResolvedSymbol, StringComparer.Ordinal)
            .ToArray();
        var diagnostics = (result.Diagnostics ?? Array.Empty<SemanticAnalysisDiagnostic>())
            .OrderBy(diagnostic => diagnostic.RelativePath, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Span?.StartOffset ?? -1)
            .ThenBy(diagnostic => diagnostic.Code)
            .ThenBy(diagnostic => diagnostic.CompilerDiagnosticId, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal)
            .ToArray();
        var references = (result.MetadataReferences ?? Array.Empty<TrustedMetadataReference>())
            .OrderBy(reference => reference.RelativePath, StringComparer.Ordinal)
            .ThenBy(reference => reference.AssemblyName, StringComparer.Ordinal)
            .ToArray();

        return result with
        {
            Observations = observations,
            Diagnostics = diagnostics,
            MetadataReferences = references
        };
    }

    private static JsonSerializerOptions SerializerOptionsWithIndent(bool indented) =>
        new(SerializerOptions) { WriteIndented = indented };

    private static LegacySemanticAnalysisResult DeserializeCore(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<LegacySemanticAnalysisResult>(json, SerializerOptions)
            ?? throw new JsonException("The JSON did not contain a semantic-analysis result.");
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectRequiredConstructorParameters = true,
            AllowTrailingCommas = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 64
        };
        options.Converters.Add(new JsonStringEnumConverter(
            JsonNamingPolicy.CamelCase,
            allowIntegerValues: false));
        return options;
    }
}
