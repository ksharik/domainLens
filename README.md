# DomainLens

**DomainLens** is a language- and framework-extensible software architecture discovery and Reverse Domain-Driven Design (DDD) platform.

> **Code establishes evidence. AI interprets evidence. The agent orchestrates the process.**

## Version 1

V1 is an end-to-end Azure-hosted product. A user can submit a public Git repository URL, monitor the Reverse DDD pipeline, answer clarification questions, explore recovered domain knowledge and explicitly proposed DDD designs, and persist the resulting domain model for future modernization.

The first supported stack is **legacy C#/.NET Framework with WCF**. This is the first analyzer family, not DomainLens's architectural boundary.

The analyzed application does not need to use DDD. DomainLens reconstructs existing domain knowledge from implementation evidence, then keeps any recommended DDD representation explicitly separate from what it recovered about the source system.

See [Product Overview](docs/01-product-overview.md), the detailed
[Architecture and Design Baseline](docs/architecture/README.md), the
[Architecture Decision Records](docs/adr/README.md), and the
[Roadmap](docs/10-roadmap.md). Product/design detail is captured in the
[Results Explorer specification](docs/product/01-results-explorer.md),
[Quality and Evaluation Strategy](docs/quality/01-evaluation-strategy.md),
[V1 Analysis Coverage](docs/design/01-v1-analysis-coverage.md), and
[Knowledge-to-Evidence Traceability](docs/design/02-knowledge-evidence-traceability.md). The
[Milestone 0 Feasibility Report](docs/12-milestone-0-deployment-security-feasibility.md) records
what the child-process, security, and legacy semantic-analysis spikes do—and do not—prove. The
[Milestone 2 Classic WCF Discovery contract](docs/13-milestone-2-wcf-discovery.md) defines the
implemented bounded WCF rule set and its limitations.

## Milestone 0 feasibility boundary

Milestone 0 demonstrates a local Windows-oriented child-worker protocol, deadline/cancellation
result rejection, bounded disposable workspaces and results, and compiler-backed .NET Framework
4.7.2 symbol enrichment over one flattened manifest-source compilation with `Partial` resolution
and an exact tool-owned reference catalog. It does **not** establish a production security
boundary: the worker still uses the host account's OS identity, network denial is not proven, no
Azure service is selected, and CPU/memory/process/disk containment remains open. Building or
restoring an analyzed repository is prohibited; repository-controlled MSBuild evaluation is not
the V1 default analysis mechanism.

## Repository Structure Scanner 0.1

Milestone 1 provides a deterministic, syntax-first scanner for local C#/.NET
repositories. It reads solution, project, and source files as untrusted data and
emits a versioned Evidence Graph as canonical JSON. It does not evaluate
MSBuild, restore or build the analyzed repository, execute repository content,
or invoke an LLM.

The implementation targets .NET 10 LTS. `global.json` pins the minimum .NET 10
SDK feature band and permits roll-forward to the latest installed feature band.

```powershell
dotnet run --project src/DomainLens.Cli -- scan `
  --repository C:\path\to\repository `
  --solution MySolution.sln `
  --output analysis.json

dotnet run --project src/DomainLens.Cli -- inspect `
  --analysis analysis.json `
  --symbol My.Namespace.Customer
```

Build and run the acceptance suite with:

```powershell
dotnet restore DomainLens.sln
dotnet format DomainLens.sln --verify-no-changes --no-restore
dotnet build DomainLens.sln --configuration Release --no-restore
dotnet test DomainLens.sln --configuration Release --no-build --no-restore
```

See [Repository Structure Scanner 0.1](docs/11-milestone-1-repository-scanner.md)
for its evidence model, security controls, status semantics, and known
limitations.

## Classic WCF Discovery 0.1

Milestone 2 adds a concrete `DomainLens.Analyzer.Wcf` module to the isolated Worker path. The
Worker creates one manifest-verified C# 7.3 compilation against the pinned net472 catalog, shares
that in-process context with legacy semantic enrichment and WCF discovery, composes WCF
observations into the existing `domainlens.evidence.v1` graph, and sends the final graph through
the existing Host validation boundary. Roslyn objects do not cross the process protocol.

The bounded analyzer recognizes the approved classic WCF service, operation, fault, data, and
message attributes by trusted semantic identity; source implementation relationships; inert
`.svc` `ServiceHost` directives; a hardened, allowlisted `system.serviceModel` XML subset; and
direct `ServiceHost`, `ChannelFactory<T>`, and `ClientBase<T>` source patterns. Within the WCF
section, bounded namespace/XDT inspection retains transform controls only as diagnostics, never
applies them, and suppresses declaration promotion when an XDT control is present. The analyzer
preserves `Exact`, `Partial`, `Ambiguous`, and `Unresolved` outcomes instead of guessing.
Repository builds, MSBuild evaluation, extension activation, endpoint contact, business
interpretation, and DDD classification remain outside this milestone.

The `domainlens.classic-wcf@0.1.1` remediation makes three boundaries explicit. Source-backed WCF
semantic observations may be `Exact` only when the source belongs to exactly one deterministically
selected `net472`/`v4.7.2` project; unsupported, unknown, multiple, or conditional profiles retain
useful evidence as `Partial` with `DL4001`. This profile rule does not downgrade independent
declarative configuration evidence. Repository-controlled WCF text is persisted through a
collision-resistant 1,024-UTF-16-code-unit representation with an explicit truncation marker,
original length, and SHA-256 digest over the exact big-endian UTF-16 code-unit sequence, while
resolution compares full manifest-bounded values before persistence. The same explicit form keeps
unpaired UTF-16 source constants JSON-safe without replacement fallback. Finally, `.svc` and `.config`
decoding is strict: UTF-8 (with or without BOM) and
BOM-marked UTF-16 LE/BE are supported; invalid, unsupported, or declaration-incompatible input
produces typed diagnostics and no evidence from that artifact. No fallback decoding or code-page
guessing occurs.

The standalone `DomainLens.Cli scan` command remains the Milestone 1 structural path. Milestone 2
is exercised through the isolated Worker/Host analysis path and its acceptance suites. See
[Milestone 2 Classic WCF Discovery](docs/13-milestone-2-wcf-discovery.md) for the exact supported
forms, evidence vocabulary, diagnostics, security boundary, and remaining limitations.
