# Product Overview

## Vision

DomainLens is an AI-assisted software architecture discovery and Reverse DDD platform that reconstructs domain knowledge from existing systems while preserving traceability to implementation evidence.

It is language/framework extensible. .NET Framework/WCF is the first supported stack.

## V1 experience

The user opens the DomainLens web application, supplies a public Git repository URL and starts analysis. DomainLens validates and snapshots the repository, inventories technologies, performs deterministic and WCF-specific analysis, builds an Evidence Graph, prepares bounded context packs, performs AI-assisted domain discovery and DDD modeling, validates findings, requests human clarification when needed, persists the analysis and Domain Knowledge Model, and presents an interactive results workspace.

## Domain Knowledge Model

Reverse DDD is not limited to identifying tactical DDD objects. DomainLens shall reconstruct a broader, evidence-backed Domain Knowledge Model covering business architecture, strategic and tactical DDD, business behavior, application/integration, security, data/consistency, and architectural evidence.

The model is intended to explain both **what the business concepts are** and **how the existing implementation realizes, constrains and couples them**. This richer representation is the foundation for later decomposition and modernization analysis.

## Durable product asset

The Domain Knowledge Model is not a disposable report. It is a canonical intermediate asset intended to feed decomposition analysis, modernization analysis, target architecture and migration planning.

The intended progression is:

`Source Code → Evidence Graph → Domain Knowledge Model → Decomposition Analysis → Modernization Model → Target Architecture`

## Clients

The native web application is the primary V1 client. DomainLens exposes APIs so future ChatGPT Sites/Apps and other clients can use the platform without constraining core capabilities.
