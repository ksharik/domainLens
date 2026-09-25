using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace DomainLens.Core;

/// <summary>Canonical JSON persistence for deterministic DomainLens evidence documents.</summary>
public static class AnalysisJson
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    /// <summary>
    /// Serializes a normalized document with a freshly computed canonical hash. Object properties,
    /// dictionaries, graph collections, evidence references, and attributes have stable ordering.
    /// </summary>
    public static string Serialize(AnalysisDocument document, bool indented = true)
    {
        var completed = WithCanonicalHash(document);
        return Encoding.UTF8.GetString(WriteCanonicalJson(completed, indented));
    }

    /// <summary>Deserializes JSON and returns a normalized in-memory representation.</summary>
    public static AnalysisDocument Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var document = JsonSerializer.Deserialize<AnalysisDocument>(json, SerializerOptions)
            ?? throw new JsonException("The JSON did not contain an analysis document.");

        ValidateRequiredShape(document);

        return Normalize(document);
    }

    /// <summary>
    /// Computes the canonical document hash. The CanonicalHash property itself is excluded from
    /// the digest, avoiding recursion and allowing verification after deserialization.
    /// </summary>
    public static string ComputeCanonicalHash(AnalysisDocument document)
    {
        var normalized = Normalize(document) with { CanonicalHash = null };
        return CanonicalIdentity.Sha256Hex(WriteCanonicalJson(normalized, indented: false));
    }

    /// <summary>Returns a normalized document carrying its current canonical hash.</summary>
    public static AnalysisDocument WithCanonicalHash(AnalysisDocument document)
    {
        var normalized = Normalize(document) with { CanonicalHash = null };
        return normalized with { CanonicalHash = ComputeCanonicalHash(normalized) };
    }

    /// <summary>Checks whether a populated CanonicalHash matches the canonical document content.</summary>
    public static bool VerifyCanonicalHash(AnalysisDocument document) =>
        !string.IsNullOrWhiteSpace(document.CanonicalHash) &&
        string.Equals(
            document.CanonicalHash,
            ComputeCanonicalHash(document),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Produces a stable ordering and defensive collection copies without changing graph meaning.
    /// Run-specific data cannot affect this operation because the model has no timestamp or
    /// absolute workspace path fields.
    /// </summary>
    public static AnalysisDocument Normalize(AnalysisDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(document.Snapshot);

        var snapshot = document.Snapshot with
        {
            Manifest = (document.Snapshot.Manifest ?? Array.Empty<ManifestEntry>())
                .Select(entry => entry with
                {
                    Path = NormalizePathForSerialization(entry.Path),
                    ContentHash = entry.ContentHash?.Trim().ToLowerInvariant() ?? string.Empty
                })
                .OrderBy(entry => entry.Path, StringComparer.Ordinal)
                .ThenBy(entry => entry.ContentHash, StringComparer.Ordinal)
                .ThenBy(entry => entry.Length)
                .ToArray()
        };

        var evidence = (document.Evidence ?? Array.Empty<EvidenceRecord>())
            .Select(item => item with
            {
                RelativePath = NormalizePathForSerialization(item.RelativePath),
                ContentHash = item.ContentHash?.Trim().ToLowerInvariant() ?? string.Empty
            })
            .OrderBy(item => item.EvidenceId, StringComparer.Ordinal)
            .ToArray();

        var nodes = (document.Nodes ?? Array.Empty<EvidenceNode>())
            .Select(node => node with
            {
                EvidenceIds = SortStrings(node.EvidenceIds),
                Attributes = SortStrings(node.Attributes),
                Properties = SortProperties(node.Properties)
            })
            .OrderBy(node => node.NodeId, StringComparer.Ordinal)
            .ToArray();

        var edges = (document.Edges ?? Array.Empty<EvidenceEdge>())
            .Select(edge => edge with { EvidenceIds = SortStrings(edge.EvidenceIds) })
            .OrderBy(edge => edge.EdgeId, StringComparer.Ordinal)
            .ToArray();

        var diagnostics = (document.Diagnostics ?? Array.Empty<AnalysisDiagnostic>())
            .Select(diagnostic => diagnostic with
            {
                RelativePath = diagnostic.RelativePath is null
                    ? null
                    : NormalizePathForSerialization(diagnostic.RelativePath),
                EvidenceIds = SortStrings(diagnostic.EvidenceIds),
                Properties = SortProperties(diagnostic.Properties)
            })
            .OrderBy(diagnostic => diagnostic.Severity)
            .ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.RelativePath, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal)
            .ToArray();

        return document with
        {
            Snapshot = snapshot,
            Nodes = nodes,
            Edges = edges,
            Evidence = evidence,
            Diagnostics = diagnostics,
            CanonicalHash = document.CanonicalHash?.Trim().ToLowerInvariant()
        };
    }

    private static IReadOnlyList<string> SortStrings(IReadOnlyList<string>? values) =>
        (values ?? Array.Empty<string>())
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyDictionary<string, string> SortProperties(
        IReadOnlyDictionary<string, string>? properties)
    {
        var sorted = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in properties ?? Empty.Properties)
        {
            sorted[property.Key] = property.Value;
        }

        return sorted;
    }

    private static string NormalizePathForSerialization(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path ?? string.Empty;
        }

        try
        {
            return CanonicalIdentity.NormalizeRepositoryPath(path);
        }
        catch (ArgumentException)
        {
            // Preserve invalid input deterministically so validation can report it to the caller.
            return path.Replace('\\', '/').Trim();
        }
    }

    private static byte[] WriteCanonicalJson(AnalysisDocument document, bool indented)
    {
        var root = JsonSerializer.SerializeToNode(document, SerializerOptions)
            ?? throw new JsonException("Could not create the canonical JSON tree.");

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(
                   stream,
                   new JsonWriterOptions
                   {
                       Indented = indented,
                       Encoder = JavaScriptEncoder.Default
                   }))
        {
            WriteNode(writer, root);
        }

        return stream.ToArray();
    }

    private static void WriteNode(Utf8JsonWriter writer, JsonNode? node)
    {
        switch (node)
        {
            case null:
                writer.WriteNullValue();
                return;

            case JsonObject jsonObject:
                writer.WriteStartObject();
                foreach (var property in jsonObject.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Key);
                    WriteNode(writer, property.Value);
                }

                writer.WriteEndObject();
                return;

            case JsonArray jsonArray:
                writer.WriteStartArray();
                foreach (var item in jsonArray)
                {
                    WriteNode(writer, item);
                }

                writer.WriteEndArray();
                return;

            default:
                node.WriteTo(writer, SerializerOptions);
                return;
        }
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
            MaxDepth = 128
        };
        options.Converters.Add(new JsonStringEnumConverter(
            JsonNamingPolicy.CamelCase,
            allowIntegerValues: false));
        return options;
    }

    private static void ValidateRequiredShape(AnalysisDocument document)
    {
        if (document.Snapshot is null ||
            document.Snapshot.Manifest is null ||
            document.Nodes is null ||
            document.Edges is null ||
            document.Evidence is null ||
            document.Diagnostics is null)
        {
            throw new JsonException("The analysis document contains a null required member.");
        }

        if (document.Snapshot.Manifest.Any(entry => entry is null) ||
            document.Nodes.Any(node =>
                node is null || node.EvidenceIds is null || node.Attributes is null || node.Properties is null) ||
            document.Edges.Any(edge =>
                edge is null || edge.EvidenceIds is null || edge.Resolution is null) ||
            document.Evidence.Any(evidence =>
                evidence is null || evidence.Span is null || evidence.Provenance is null ||
                evidence.Resolution is null) ||
            document.Diagnostics.Any(diagnostic =>
                diagnostic is null || diagnostic.EvidenceIds is null || diagnostic.Properties is null))
        {
            throw new JsonException("The analysis document contains a null required nested member.");
        }
    }
}
