# Evidence Architecture

## Purpose and non-negotiable boundary

The Evidence Graph is DomainLens's record of what deterministic analyzers can
establish from a specific repository snapshot. It exists so every later claim
can be traced to stable source provenance without asking a model to parse or
remember an entire repository.

> **Code establishes evidence. AI interprets evidence. The agent orchestrates
> the process.**

The current implementation is the authority for the Milestone 1 wire model.
This chapter labels unimplemented designs as **PLANNED — Product V1** or
**FUTURE**. See [Analysis Pipeline](04-analysis-pipeline.md) for stage ordering
and [Repository Structure Scanner 0.1](../11-milestone-1-repository-scanner.md)
for the scanner-specific summary.

## Evidence, findings, and knowledge are different records

```mermaid
flowchart LR
    Repo[Repository snapshot]
    Analyzer[Deterministic analyzer]
    EG[(Evidence Graph)]
    Context[Versioned ContextPack]
    Model[Model reasoning]
    Candidate[Candidate findings]
    Validator[Finding validator]
    FG[(Finding Graph)]
    Human[Human review]
    DKM[(Domain Knowledge Model)]

    Repo --> Analyzer -->|Observed only| EG
    EG --> Context --> Model -->|Inferred or Proposed| Candidate
    Candidate --> Validator --> FG --> Human --> DKM
    EG -. evidence references .-> FG
    EG -. durable traceability .-> DKM
```

These words have precise architectural meanings:

| Classification | Meaning | Who may create it |
|---|---|---|
| **Observed** | A deterministic statement tied to captured source/configuration bytes and an analyzer rule. It may be exact, partial, ambiguous, or unresolved. | Deterministic analyzer application code only. |
| **Inferred** | A semantic interpretation strongly supported by observed evidence, with reasoning, confidence, counterevidence, and limitations. | The reasoning runtime may propose it; validators and humans govern acceptance. |
| **Proposed** | A recommendation or target interpretation that is not claimed to describe the source as fact. | The reasoning runtime or a human may propose it. |

`ResolutionQuality` is not this classification. For example, an unresolved
declared type reference is still an **Observed** fact about source syntax; it is
not an AI inference. Conversely, a high-confidence bounded-context candidate is
still **Inferred** or **Proposed**, never Observed.

Milestone 1 implements only the Evidence Graph. Its schema does not yet carry
an `Observed` field because every graph record created by the scanner is
deterministic by construction. The Finding Graph and its explicit
classification contract are PLANNED.

### Model-output prohibition

An LLM/model response cannot create, update, or relabel an Evidence Graph
record as Observed. It may cite immutable evidence IDs in a structured finding.
If a semantic review exposes missing evidence, the coordinator may schedule a
deterministic analyzer; only that analyzer can add new evidence for a versioned
snapshot. Human-entered knowledge and clarification also remain distinct from
source evidence.

## CURRENT — Milestone 1 evidence document

The current schema identifier is `domainlens.evidence.v1`. The implementation
uses immutable-style C# records and canonical JSON; it does not publish a
separate JSON Schema artifact yet.

```mermaid
classDiagram
    class AnalysisDocument {
        schemaVersion
        status
        canonicalHash
    }
    class RepositorySnapshot {
        snapshotId
        revision
    }
    class ManifestEntry {
        path
        contentHash
        length
    }
    class EvidenceNode {
        nodeId
        logicalId
        kind
        name
        qualifiedName
        projectId
        evidenceIds
        attributes
        properties
    }
    class EvidenceEdge {
        edgeId
        kind
        fromNodeId
        toNodeId
        unresolvedTarget
        evidenceIds
        resolution
    }
    class EvidenceRecord {
        evidenceId
        snapshotId
        relativePath
        contentHash
        span
        provenance
        resolution
    }
    class SourceSpan {
        startOffset
        length
        startLine
        startColumn
        endLine
        endColumn
    }
    class ExtractorProvenance {
        extractorId
        extractorVersion
        ruleId
    }
    class EvidenceResolution {
        basis
        quality
        details
    }
    class AnalysisDiagnostic {
        code
        severity
        message
        relativePath
        evidenceIds
        properties
    }

    AnalysisDocument "1" *-- "1" RepositorySnapshot
    RepositorySnapshot "1" *-- "many" ManifestEntry
    AnalysisDocument "1" *-- "many" EvidenceNode
    AnalysisDocument "1" *-- "many" EvidenceEdge
    AnalysisDocument "1" *-- "many" EvidenceRecord
    AnalysisDocument "1" *-- "many" AnalysisDiagnostic
    EvidenceRecord "1" *-- "1" SourceSpan
    EvidenceRecord "1" *-- "1" ExtractorProvenance
    EvidenceRecord "1" *-- "1" EvidenceResolution
    EvidenceEdge "1" *-- "1" EvidenceResolution
```

### Repository snapshot and manifest

`RepositorySnapshot` contains a `SnapshotId`, an optional `Revision`, and a
sorted manifest. Each `ManifestEntry` stores:

- a normalized repository-relative path using `/` separators;
- the lowercase SHA-256 digest of the exact bytes read; and
- the number of bytes read.

Milestone 1 hashes every included readable file, not only C# files. It retains
file bytes for selected solution/project/source/configuration extensions so
they can be parsed without reopening them. Default inventory bounds are
100,000 files, 16 MiB per file, 1 GiB total bytes read, and a traversal bound
of the greater of 10,000 entries or four times the file-count limit.

The current scanner deliberately sets `Revision` to `null`. The manifest is the
authoritative identity of what it captured. It must not be described as a Git
commit snapshot, an atomic filesystem snapshot, or proof that files did not
change during enumeration. Git revision selection and acquisition are PLANNED.

### Source evidence and provenance

An `EvidenceRecord` binds one deterministic observation to:

- the snapshot ID;
- a repository-relative manifest path and matching content hash;
- a `SourceSpan`;
- extractor ID, extractor version, and rule ID; and
- resolution basis, quality, and optional explanatory details.

Offsets and lengths are zero-based UTF-16 code-unit positions in the decoded
text. Lines and columns are one-based, with exclusive end coordinates. The
content hash always covers original file bytes, not normalized text.

Current extractors identify themselves as
`domainlens.repository-structure@0.1.0` and
`domainlens.csharp-syntax@0.1.0`. The version identifies extraction behavior;
the rule ID identifies the particular observation, such as a project
declaration, source membership, type declaration, or method parameter type.

### Resolution basis and quality

The language-neutral basis vocabulary is:

- `Unknown`
- `Manifest`
- `DeclarativeConfiguration`
- `Syntax`
- `Semantic`
- `Metadata`
- `Composite`

Milestone 1 currently emits `DeclarativeConfiguration` and `Syntax`. Here,
`Semantic` is reserved for deterministic semantic analysis such as a compiler
binding operation; it does not mean free-form LLM reasoning.

The quality vocabulary is:

- `Exact` — the supported deterministic rule resolved the observation within
  its stated scope;
- `Partial` — the observation is useful but known coverage or selection is
  incomplete;
- `Ambiguous` — more than one supported target is possible; and
- `Unresolved` — a declaration was observed but no supported target could be
  established.

Exact does not mean globally or behaviorally complete. An exact syntax
observation says the supported source construct was read exactly; it does not
assert runtime behavior, semantic compilation success, or business meaning.

### Evidence nodes

An `EvidenceNode` contains canonical and logical identities, an analyzer-owned
stable `Kind`, display and qualified names, optional project scope, evidence
references, syntactic attribute names, and a string property bag. The core is
not WCF- or C#-specific; it does not enumerate allowed node kinds.

Milestone 1 currently emits these categories:

- repository structure: `Repository`, `Solution`, `Project`,
  `AssemblyReference`, and `PackageReference`;
- syntax containers/types: `Namespace`, `Class`, `Interface`, `Struct`,
  `Record`, `RecordStruct`, `Enum`, and `Delegate`; and
- members: `Constructor`, `Destructor`, `Method`, `Property`, `Indexer`,
  `Field`, `Event`, `Operator`, `ConversionOperator`, and `EnumMember`.

Generic type arity is represented in canonical qualified/metadata identities,
for example `Box` followed by `` `1 ``. Instance and static constructors use
`.ctor(...)` and `.cctor()` identities. Partial declarations merge source
evidence into one node when their stable key is the same.

The repository node is synthetic and currently has no source evidence. This is
valid and explicitly displayed as such by `inspect`. Architecture claims
should therefore say source-backed observations are traceable, not that every
node necessarily points to a source span.

### Evidence edges

Every edge has a source node and exactly one of:

- `ToNodeId` for a resolved target; or
- `UnresolvedTarget` for the stable textual target that could not be resolved.

It also carries evidence references and resolution. Milestone 1 emits:

- `Contains`
- `ReferencesProject`
- `ReferencesAssembly`
- `ReferencesPackage`
- `Inherits`
- `Implements`
- `DeclaredTypeDependency`

Declared type dependencies cover supported declaration signatures and base
lists. They are not a method-body call graph, data flow, runtime dependency
graph, or proof that an external type binds successfully.

### Diagnostics and analysis status

`AnalysisDiagnostic` represents coverage loss, degradation, informational
security observations, or fatal failures. It has a stable code, severity,
message, optional manifest path, optional evidence IDs, and properties. The
core model permits diagnostic evidence references; the Milestone 1 scanner's
converted diagnostics currently use paths, line/column properties, and
`subjectPath` where applicable rather than populating evidence IDs.

`Success`, `PartialSuccess`, and `Failure` summarize deterministic analysis
coverage as described in [Analysis Pipeline](04-analysis-pipeline.md). They are
not Finding Graph acceptance states.

## CURRENT — canonical identity

Canonical identities are application-owned strings with a type prefix and a
lowercase SHA-256 digest. Dedicated CLR wrapper types are not implemented. The
hash input uses NFC-normalized strings, an explicit null/value marker, and a
four-byte big-endian length before each UTF-8 value; this avoids delimiter and
component-boundary ambiguity.

```mermaid
flowchart TD
    Manifest[Sorted manifest plus optional revision]
    SnapshotId[snapshot ID]
    EvidenceFields[Snapshot, path, hash, offset, length, extractor, rule, basis, quality]
    EvidenceId[evidence ID]
    LogicalFields[Project qualified name, node kind, qualified name]
    LogicalId[logical-node ID]
    NodeFields[Snapshot ID plus logical ID]
    NodeId[node ID]
    EdgeFields[Snapshot, kind, endpoints or unresolved target, basis, quality, sorted evidence IDs]
    EdgeId[edge ID]

    Manifest --> SnapshotId
    SnapshotId --> EvidenceFields --> EvidenceId
    LogicalFields --> LogicalId
    SnapshotId --> NodeFields
    LogicalId --> NodeFields --> NodeId
    SnapshotId --> EdgeFields
    NodeId --> EdgeFields --> EdgeId
```

The prefixes are `snapshot:`, `evidence:`, `logical-node:`, `node:`, and
`edge:`. A logical node ID is intended to remain stable across snapshots when
the persisted project scope, kind, and qualified name remain stable. A node ID
is snapshot-specific. Edges and evidence are snapshot-specific.

Important precision points:

- Evidence identity includes offset and length but not human-readable
  line/column values or resolution details.
- Edge identity includes sorted evidence IDs and resolution basis/quality but
  not resolution details.
- Node identity does not include its display name, attributes, evidence IDs,
  or property bag.
- The graph validator recomputes identities from exactly these persisted
  identity fields. The document hash still covers the complete normalized
  document, including non-identity fields.

Changing an identity recipe is therefore a schema/versioning event, even when
the JSON field shapes remain the same.

## CURRENT — canonical JSON and document hash

Serialization normalizes the document before writing:

- manifest entries, evidence, nodes, edges, and diagnostics use stable ordinal
  ordering;
- evidence-reference lists and attributes are sorted;
- property dictionaries and every JSON object's properties are sorted
  ordinally;
- paths and hexadecimal hashes are normalized;
- property names and string enum values use camel case; and
- `canonicalHash` itself is set to `null` while its digest is computed.

The resulting `CanonicalHash` is lowercase SHA-256 over the unindented
normalized JSON bytes. Pretty printing does not change it. This is a
DomainLens canonical representation; it is not claimed to implement RFC 8785
or another external canonical-JSON standard.

The hash detects changes within the artifact. It is not a digital signature,
does not authenticate who produced the artifact, and does not revalidate files
in a repository.

## CURRENT — graph validation

`AnalysisGraphValidator` rejects an artifact when internal invariants fail. It
currently verifies:

- supported schema version and defined analysis status;
- presence of a snapshot and correctly derived snapshot ID;
- canonical, unique manifest paths; SHA-256 formats; and nonnegative lengths;
- unique evidence IDs, matching snapshot/path/hash, structurally valid spans,
  nonempty provenance, defined resolution values, and derived evidence IDs;
- unique node IDs, required node identity fields, resolvable project/evidence
  references, and derived logical/node IDs;
- unique edge IDs, valid source and resolved target references, exactly one
  resolved or unresolved target, defined resolution, valid evidence
  references, and derived edge IDs;
- valid diagnostic fields, severities, paths, and evidence references; and
- presence, format, and correctness of the canonical document hash.

Scanner output is canonicalized and validated before it is returned. The
`inspect` command verifies both the document hash and graph before displaying a
node.

Validation is deliberately internal. It does **not** reopen source files,
recompute manifest hashes from a repository, verify that source spans fit the
actual captured text, prove that a statement is semantically correct, enforce
that status matches diagnostics, authenticate the producer, or provide a
malware verdict. Those must not be implied by the word "validated."

## CURRENT — deterministic type-linking scope

The C# syntax extractor uses a conservative, analyzer-local name resolver. It
considers lexical namespace/nesting, visible ordinary/global using directives,
using aliases, generic arity, and source types in the current project or an
eligible **direct** literal project reference. Conditional references,
dependency selection affected by unevaluated MSBuild, references with
`ReferenceOutputAssembly` other than `true`, and alias-only references without
`global` are not treated as globally accessible type sources.

This is not Roslyn compilation or full C# binding. External types, metadata
binding, accessibility, all alias forms, generated code, preprocessor symbol
evaluation, and transitive compile behavior are not established. The extractor
preserves ambiguous/unresolved edges instead of upgrading a textual match to a
fact.

## PLANNED — Finding Graph traceability

A structured finding should minimally retain:

- a finding identity and revision;
- `Inferred` or `Proposed` classification;
- a typed concept and structured relationships;
- supporting evidence IDs and counterevidence IDs;
- reasoning summary, confidence, and stated limitations;
- unresolved questions;
- ContextPack identity;
- analyzer, model, prompt, and skill versions; and
- validation and human-review state.

The exact schema remains an OPEN DECISION. It must support multiple competing
interpretations without overwriting evidence or a previous finding revision.
Accepting a finding into the Domain Knowledge Model records a decision; it does
not change its origin classification.

## PLANNED — analyzer extension contract

Future deterministic analyzers for WCF, ASP.NET, Java, persistence, OpenAPI,
and messaging will add normalized graph records through the Evidence Kernel.
They must:

1. consume an identified repository snapshot or another explicitly versioned
   deterministic input;
2. declare analyzer and rule versions;
3. emit stable analyzer-owned kinds and relationships;
4. attach repository-relative provenance and resolution to source-backed
   observations;
5. preserve unresolved, ambiguous, partial, and contradictory evidence;
6. comply with canonical identity and graph validation; and
7. run without converting model output into evidence.

The analyzer interface, kind namespace/registry, compatibility negotiation,
and schema migration mechanism are not implemented yet. The current open
string `Kind` and property-bag model is an interoperability base, not a complete
plugin system.

## Open decisions

1. **Schema publication:** whether to publish JSON Schema, generated contracts,
   or both, and the compatibility policy for `domainlens.evidence.v1`.
2. **Identity evolution:** versioning and migration rules when an analyzer fixes
   or changes a canonical qualified-name recipe.
3. **Analyzer kind governance:** namespace, registration, collision prevention,
   and validation of node, edge, property, and rule identifiers.
4. **Snapshot atomicity:** acquisition mechanism that makes a Product V1 Git
   snapshot repeatable while remaining safe for untrusted repositories.
5. **Source retention:** whether original source blobs are retained, encrypted,
   deduplicated, or discarded after extracting spans and hashes.
6. **Artifact authenticity:** whether persisted graphs need signing or an
   authenticated envelope in addition to the current content hash.
7. **Diagnostic provenance:** when diagnostics must cite evidence IDs rather
   than only manifest paths and coordinates.
8. **Cross-analyzer reconciliation:** how equivalent concepts and conflicting
   observations from different analyzer families are represented without
   destructive merging.
9. **Finding Graph schema:** classification, confidence scale, contradiction
   model, revision identity, and acceptance workflow.
10. **Evidence correction:** how an analyzer defect supersedes an earlier graph
    while preserving audit history and links from existing findings.
11. **Large-graph partitioning:** storage and retrieval boundaries that retain
    canonical provenance without forcing whole-document loading.
12. **Sensitive evidence:** redaction and authorization rules for secrets,
    personal data, and source excerpts before persistence or model egress.
