# Architecture

## Style

DomainLens V1 uses a modular application architecture with an isolated analyzer-worker execution boundary.

```text
DomainLens Web UI
       |
DomainLens API / Core
       |
       +-- Pipeline Coordinator
       +-- Persistence
       +-- Retrieval / Context Builder
       +-- Reasoning Runtime
       +-- Finding Validator
       |
Isolated Analyzer Worker
       |
       +-- Repository Inventory
       +-- Roslyn / C# Analysis
       +-- WCF Analysis
       +-- Relationship Extraction
       |
Evidence Graph -> Context Packs -> LLM Reasoning -> Finding Graph -> Persistent DDD Model
```

## Azure

Azure is the target cloud. The core should use adapter boundaries rather than depend directly on Azure-specific APIs.

Initial direction: Azure-hosted web UI and API/Core; isolated Windows-capable analyzer; Git/Roslyn/MSBuild/reference assemblies as safely required; persistence behind an application abstraction; model access behind a provider adapter.

## Extensibility

DomainLens core is language/framework neutral. Technology analyzers emit a normalized Evidence Model. Future worker families may include Windows and Linux workers. Container pools and AKS are scaling options, not V1 prerequisites.

The architecture shall support future automatic technology discovery and generation of an Analysis Plan selecting applicable analyzers.
