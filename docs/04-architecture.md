# Architecture

> This is the concise approved direction. The detailed, status-labelled design
> is in the [Architecture and Design Baseline](architecture/README.md).

## Style

DomainLens V1 plans a modular application architecture with an isolated analyzer-worker execution boundary. Scanner 0.1 currently runs as a local CLI/library; the hosted worker boundary is not implemented yet.

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
Evidence Graph -> Context Packs -> LLM Reasoning -> Finding Validation -> Finding Graph -> Human Review -> Persistent Domain Knowledge Model
```

## Azure

Azure is the target cloud. The core should use adapter boundaries rather than depend directly on Azure-specific APIs.

Initial direction: Azure-hosted web UI and API/Core; isolated Windows-capable analyzer; Git/Roslyn/MSBuild/reference assemblies as safely required; persistence behind an application abstraction; model access behind a provider adapter.

## Extensibility

DomainLens core is language/framework neutral. Technology analyzers emit a normalized Evidence Model. Future worker families may include Windows and Linux workers. Container pools and AKS are scaling options, not V1 prerequisites.

The architecture shall support future automatic technology discovery and generation of an Analysis Plan selecting applicable analyzers.
