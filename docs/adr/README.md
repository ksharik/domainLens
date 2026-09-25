# Architecture Decision Records

This directory records architectural decisions that are already accepted for
DomainLens. Requirements documents describe what the product must do;
architecture documents describe how the system is organized; and these ADRs
capture durable choices and their consequences.

An ADR fixes only the decision stated in its **Decision** section. Choices
called out as deferred remain open and must not be inferred from diagrams,
examples, or candidate technology names.

## Accepted decisions

1. [ADR-001: Azure as the primary deployment platform](001-azure-primary-deployment-platform.md)
2. [ADR-002: Modular architecture with an isolated analyzer worker](002-modular-architecture-with-isolated-analyzer-worker.md)
3. [ADR-003: Separate the Evidence Graph from the Finding Graph](003-separate-evidence-graph-from-finding-graph.md)
4. [ADR-004: Separate deterministic evidence from AI interpretation](004-separate-deterministic-evidence-from-ai-interpretation.md)
5. [ADR-005: Persist the Domain Knowledge Model as the canonical intermediate asset](005-persist-domain-knowledge-model-as-canonical-intermediate-asset.md)
6. [ADR-006: Use a language- and framework-neutral analyzer architecture](006-language-and-framework-neutral-analyzer-architecture.md)
7. [ADR-007: Use .NET 10 LTS for the DomainLens host](007-dotnet-10-lts-for-domainlens-host.md)
8. [ADR-008: Do not require a vector database for V1](008-no-vector-database-required-for-v1.md)
9. [ADR-009: Do not require dynamic agents, MCP, or A2A for V1](009-no-dynamic-agents-mcp-or-a2a-required-for-v1.md)
10. [ADR-010: Separate decomposition analysis from domain-knowledge reconstruction](010-separate-decomposition-from-domain-knowledge-reconstruction.md)
