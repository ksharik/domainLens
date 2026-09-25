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
what the child-process, security, and legacy semantic-analysis spikes do—and do not—prove.

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
