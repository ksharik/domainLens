# Product Overview

## Vision

DomainLens is an AI-assisted software architecture discovery and Reverse DDD platform that reconstructs domain knowledge from existing systems while preserving traceability to implementation evidence.

It is language/framework extensible. .NET Framework/WCF is the first supported stack.

> **Source-model neutrality:** DomainLens shall not require or assume that an analyzed application was originally designed using Domain-Driven Design. DDD concepts may be recovered, inferred, or proposed from implementation evidence even when corresponding DDD constructs do not explicitly exist in the source system.

Reverse DDD therefore answers two related but distinct questions: **what domain knowledge can be reconstructed from the existing system and business behavior, and how can that domain be represented using DDD?** The answer must not present a recommended DDD representation as though it existed in the source application.

## V1 experience

The user opens the DomainLens web application, supplies a public Git repository URL and starts analysis. DomainLens validates and snapshots the repository, inventories technologies, performs deterministic and WCF-specific analysis, builds an Evidence Graph, prepares bounded context packs, performs AI-assisted domain discovery and DDD modeling, validates findings, requests human clarification when needed, persists the analysis and Domain Knowledge Model, and presents an interactive results workspace.

## Domain Knowledge Model

Reverse DDD is not limited to identifying tactical DDD objects. DomainLens shall reconstruct a broader, evidence-backed Domain Knowledge Model covering business architecture, strategic and tactical DDD, business behavior, application/integration, security, data/consistency, and architectural evidence.

The model is intended to explain both **what the business concepts are** and **how the existing implementation realizes, constrains and couples them**. This richer representation is the foundation for later decomposition and modernization analysis.

The Domain Knowledge Model remains one canonical asset with two explicit semantic views:

- **Recovered Domain Knowledge** — evidence-backed reconstruction of the existing system and business.
- **Proposed DDD Design** — recommended DDD representations that may not exist in the source system.

Both views preserve source findings, supporting and contradictory evidence, epistemic classification, assumptions, confidence/support, alternatives, review state, and revision history. Accepting a proposal changes its review state; it remains `Proposed`.

## Durable product asset

The Domain Knowledge Model is not a disposable report. It is a canonical intermediate asset intended to feed decomposition analysis, modernization analysis, target architecture and migration planning.

The intended progression is:

`Source Code → Evidence Graph → Semantic Findings → Domain Knowledge Model { Recovered Domain Knowledge + Proposed DDD Design } → Decomposition Analysis → Modernization Model → Target Architecture`

## Clients

The native web application is the primary V1 client. DomainLens exposes APIs so future ChatGPT Sites/Apps and other clients can use the platform without constraining core capabilities.
