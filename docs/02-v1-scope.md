# V1 Scope

## In scope

- Azure-hosted end-to-end product and native web UI.
- Public, unauthenticated Git repository URLs and ref selection.
- C# / legacy .NET Framework and classic WCF.
- Repository snapshots and immutable manifests.
- Safe syntax analysis plus isolated semantic enrichment.
- Project/reference/dependency analysis.
- WCF contracts, operations, implementations, data/message contracts, endpoints, bindings and hosting configuration.
- Static relationships and Evidence Graph.
- Predefined AI-assisted domain discovery and DDD modeling.
- Business capability, actor, use-case, Domain Vocabulary, business-rule, invariant, workflow/state-transition, security-policy, data-ownership and coupling discovery where supported by analyzed evidence.
- Finding validation and Finding Graph.
- Human-in-the-loop clarification, challenge and review.
- Visible pipeline status/diagnostics.
- Persistent analysis runs and Domain Knowledge Models.
- Evidence-backed result exploration.

The analyzed application may use traditional N-tier, transaction-script, anemic-domain-model, service-oriented, procedural, tightly coupled monolithic, partially domain-oriented, or explicit DDD styles. V1 shall not require DDD constructs or naming conventions in the source. In particular, names such as `AggregateRoot`, `Entity`, `ValueObject`, `DomainEvent`, or `BoundedContext` are neither required nor sufficient for a DDD conclusion.

Domain reconstruction instead uses supported evidence about behavior, business rules, invariants, data relationships, mutation paths, transaction boundaries, workflows, state transitions, service operations, persistence, messages, security policies, dependencies, and coupling.

## Domain Knowledge Model outputs

The single canonical Domain Knowledge Model shall expose **Recovered Domain Knowledge** separately from **Proposed DDD Design**. Where evidence supports them, V1 shall recover, infer, or propose the following concepts without implying that a proposal existed in the source system:

### Business architecture
- Business capabilities.
- Actors.
- Use cases.
- Domain Vocabulary / Ubiquitous Language: business terms, candidate definitions, synonyms,
  aliases, abbreviations, acronyms, context-specific meanings, conflicting meanings, ambiguous
  usages and evidence-linked source locations.
- Domains and subdomains.
- Core / Supporting / Generic subdomain classification.

Vocabulary is semantic domain knowledge, not a deterministic restatement of identifiers. Source
names, labels, comments, contracts and messages may be Observed evidence; candidate meaning is
`Inferred`, and a recommended normalized language is `Proposed`. The same term used with materially
different meanings may support a bounded-context interpretation, but vocabulary conflict alone is
not proof of a boundary.

### Strategic DDD
- Bounded contexts.
- Context maps and context relationships.
- Upstream/downstream relationships.
- Shared Kernel, Customer/Supplier, Conformist, Published Language and Anti-Corruption Layer patterns where supported.

### Tactical DDD
- Aggregates and aggregate roots.
- Entities and value objects.
- Domain services.
- Repositories.
- Factories.
- Commands, domain events and handlers.

### Business behavior
- Business rules.
- Invariants.
- Policies.
- Workflows/business processes.
- State transitions and lifecycles.
- Decisions.
- Preconditions/postconditions.
- Validation rules.

### Application and integration
- Application services.
- APIs/operations.
- DTOs/contracts.
- Messages and integration events.
- External systems.
- Synchronous/asynchronous dependencies.

### Security
- Authentication mechanisms/policies visible in analyzed artifacts.
- Authorization.
- Roles and permissions.
- Security policies.
- Sensitive-data handling rules visible in analyzed artifacts.

### Data and consistency
- Data ownership.
- Persistence relationships.
- Transaction boundaries.
- Consistency boundaries.
- Shared-data/shared-database dependencies.

### Cross-cutting architecture knowledge
- Dependencies and coupling.
- Calls and side effects.
- Configuration-driven behavior.
- Batch/scheduled processes.
- Error/exception behavior.
- Audit requirements visible in analyzed artifacts.
- Concurrency/locking.
- Caching.
- Transaction-consistency characteristics.

DomainLens shall preserve the distinction between observed implementation evidence and inferred/proposed domain meaning. Each DKM view shall retain source findings, evidence, classification, assumptions, Support, Confidence, Coverage, Completeness, linked Resolution Quality, alternatives, review state, and revision history. Human acceptance changes review state only: an accepted Proposed DDD concept remains `Proposed`. Absence of evidence in analyzed artifacts must not be represented as proof that a business rule, security policy or other concept does not exist.

Coverage, Support, Confidence, Completeness and deterministic Resolution Quality are separate
analysis dimensions. Their definitions and thresholds must remain visible rather than being
collapsed into a single score. No Product V1 threshold is approved merely by listing a concept in
scope.

## Excluded from V1

Private/authenticated repos; arbitrary repository code execution; automated refactoring; arbitrary model tool access; dynamic agent creation; MCP/A2A orchestration; vector databases; AKS as a default requirement; and Java/Spring analysis.

Repository Structure Scanner 0.1 is the first engineering milestone, **not** Product V1. Product V1 is the complete workflow above.
