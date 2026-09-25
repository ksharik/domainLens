# Repository Structure Scanner 0.1

## Scope

Milestone 1 is the first deterministic DomainLens vertical slice. It accepts a
local repository path and optional repository-relative solution selection,
captures an immutable content manifest, discovers .NET solution/project
structure, parses C# syntax, validates the resulting Evidence Graph, and writes
canonical JSON.

It creates deterministic evidence only. It contains no Reverse DDD reasoning,
LLM integration, Finding Graph, remote repository intake, WCF-specific logic,
database, web UI, or Azure infrastructure.

## Projects

- `DomainLens.Core` owns the language-neutral evidence records, canonical
  identities, canonical JSON, and graph/provenance validation.
- `DomainLens.Scanner` owns safe filesystem inventory, solution/project parsing,
  C# syntax extraction, declared relationships, and analysis status.
- `DomainLens.Cli` exposes `scan` and `inspect` commands.
- `DomainLens.Scanner.Tests` contains fixture-backed acceptance, determinism,
  provenance, graph-integrity, and security tests.

This is intentionally smaller than the eventual Product V1 module layout. The
scanner library can later be hosted by the isolated analyzer worker described
in the approved architecture.

## Deterministic evidence model

The authoritative JSON document contains:

- a snapshot ID derived from a sorted repository-relative manifest;
- manifest entries with byte length and SHA-256 content hash;
- language-neutral nodes and relationships;
- source evidence with snapshot, repository-relative path, content hash,
  source span, extractor/rule/version, and resolution;
- typed diagnostics and `Success`, `PartialSuccess`, or `Failure` status; and
- a canonical hash that excludes itself.

Canonical IDs are application-owned, type-prefixed SHA-256 identities
(`snapshot:`, `evidence:`, `logical-node:`, `node:`, and `edge:`). The JSON wire
form is a string, while the containing schema field and mandatory prefix retain
the identity type. IDs are recomputed and type-checked by graph validation.
They do not persist Roslyn object identities, timestamps, absolute repository
paths, temporary paths, or randomly generated values. Dedicated CLR value-type
wrappers are intentionally deferred because they would not change this
canonical wire contract.

Source offsets and lengths are zero-based UTF-16 code-unit positions. Human
line and column values are one-based, with exclusive end coordinates. File
content hashes are calculated from the original bytes captured for the
snapshot.

## Security controls

The scanner structurally cannot execute analyzed repositories:

- it has no `Microsoft.Build`, `MSBuildWorkspace`, process-launch, restore, or
  build integration;
- C# is parsed with Roslyn syntax APIs only;
- project files are parsed through an XML reader with DTD processing prohibited
  and external resolution disabled;
- project imports, targets, tasks, `Exec`, analyzers, generators, and build
  events are data only;
- `.git`, build-output/package directories, and reparse points are not followed;
- literal project/source paths are resolved only when they remain inside the
  selected repository root; and
- traversal, file-count, individual-file-size, and aggregate-byte limits bound
  snapshot work.

The CLI `inspect` command displays evidence already present in an analysis
artifact. It never follows artifact-provided paths to read source files.

## Analysis status

- `Success`: deterministic analysis completed without a known coverage loss.
- `PartialSuccess`: trustworthy evidence was produced, but a missing file,
  unresolved project reference, unsafe/dynamic project construct, resource
  exclusion, conditional compilation, or syntax error reduced coverage.
- `Failure`: the repository/solution selection was invalid, no analyzable C#
  project was in scope, or an internal Evidence Graph invariant failed.

Informational security diagnostics, such as detecting an inert `Exec` element,
do not by themselves change a successful analysis to partial success.

CLI exit codes are `0` for success, `2` for partial success, `1` for fatal
failure or invalid artifact, `3` for an unmatched/ambiguous inspect query, and
`64` for invalid command usage.

## Safely discovered structure

Project discovery reads literal, statically declared values for target
frameworks, assembly/root namespace, project references, assembly references,
package references, and compile items. SDK default C# items and literal
in-repository linked files are supported without invoking MSBuild. Conditional
items and source membership affected by unevaluated repository-wide build files
are retained only as partial evidence and produce coverage diagnostics.

C# extraction covers namespaces; classes; interfaces; structs; enums; records;
delegates; constructors; methods; properties; indexers; fields; events;
operators; enum members; attributes; inheritance; interface implementation;
and declared type dependencies. Inheritance and implementation are classified
only when the referenced source type is uniquely resolvable through lexical
namespace/import evidence inside the current project or an eligible direct,
literal, unconditional project reference. `ReferenceOutputAssembly` and
non-global aliases are honored conservatively. Other type references retain
explicit unresolved or ambiguous resolution instead of being guessed.

## Known limitations

- MSBuild properties, conditions, imports, item transforms, and globs are not
  evaluated. Affected coverage is diagnosed.
- Analysis is syntax-only. It cannot fully bind external types, aliases,
  reflection, dynamic dispatch, generated code, or compiler-synthesized
  members.
- Preprocessor symbols are not inferred from project evaluation.
- Source generators and repository analyzers are intentionally not executed.
- Only C# project/source analysis is implemented. WCF and other framework
  analyzers remain later milestones.
- Milestone 1 does not resolve Git revision metadata. The captured content
  manifest is the authoritative snapshot identity.
- .NET does not expose a portable managed API that proves a Unix directory
  entry is a regular file before opening it. Reparse points and entries reported
  as devices are excluded and reads are size-bounded, but a hostile Unix FIFO
  or socket may still block a standalone CLI scan. Production analysis must run
  in the isolated worker with an OS-level deadline; portable special-file
  rejection is deferred with that worker hardening.
- Remote Git input, persistence services, analyzer-worker hosting, UI, and cloud
  deployment remain later Product V1 work.

These limitations preserve the approved boundary: code establishes evidence;
AI interpretation comes later.
