# DomainLens

**DomainLens** is a language- and framework-extensible software architecture discovery and Reverse Domain-Driven Design (DDD) platform.

> **Code establishes evidence. AI interprets evidence. The agent orchestrates the process.**

## Version 1

V1 is an end-to-end Azure-hosted product. A user can submit a public Git repository URL, monitor the Reverse DDD pipeline, answer clarification questions, explore evidence-backed DDD results, and persist the resulting domain model for future modernization.

The first supported stack is **legacy C#/.NET Framework with WCF**. This is the first analyzer family, not DomainLens's architectural boundary.

See [Product Overview](docs/01-product-overview.md), [Architecture](docs/04-architecture.md), and [Roadmap](docs/10-roadmap.md).

## Repository Structure Scanner 0.1

Milestone 1 provides a deterministic, syntax-first scanner for local C#/.NET
repositories. It reads solution, project, and source files as untrusted data and
emits a versioned Evidence Graph as canonical JSON. It does not evaluate
MSBuild, restore or build the analyzed repository, execute repository content,
or invoke an LLM.

The implementation targets the .NET 9 SDK pinned by `global.json`.

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
dotnet build DomainLens.sln --configuration Release --no-restore
dotnet test DomainLens.sln --configuration Release --no-build --no-restore
```

See [Repository Structure Scanner 0.1](docs/11-milestone-1-repository-scanner.md)
for its evidence model, security controls, status semantics, and known
limitations.
