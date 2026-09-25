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
- Business capability, actor, use-case, business-rule, invariant, workflow/state-transition, security-policy, data-ownership and coupling discovery where supported by analyzed evidence.
- Finding validation and Finding Graph.
- Human-in-the-loop clarification, challenge and review.
- Visible pipeline status/diagnostics.
- Persistent analysis runs and Domain Knowledge Models.
- Evidence-backed result exploration.

## Domain Knowledge Model outputs

Where evidence supports them, V1 shall identify or propose:

### Business architecture
- Business capabilities.
- Actors.
- Use cases.
- Domains and subdomains.
- Core / Supporting / Generic subdomain classification.

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

DomainLens shall preserve the distinction between observed implementation evidence and inferred/proposed domain meaning. Absence of evidence in analyzed artifacts must not be represented as proof that a business rule, security policy or other concept does not exist.

## Excluded from V1

Private/authenticated repos; arbitrary repository code execution; automated refactoring; arbitrary model tool access; dynamic agent creation; MCP/A2A orchestration; vector databases; AKS as a default requirement; and Java/Spring analysis.

Repository Structure Scanner 0.1 is the first engineering milestone, **not** Product V1. Product V1 is the complete workflow above.
