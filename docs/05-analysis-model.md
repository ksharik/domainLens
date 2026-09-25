# Analysis and Evidence Model

> Scanner 0.1 currently implements only the deterministic Evidence Graph.
> Finding Graph and Domain Knowledge Model behavior below is planned V1
> architecture. See the detailed [Evidence](architecture/05-evidence-architecture.md)
> and [Domain Knowledge](architecture/06-domain-knowledge-architecture.md)
> designs.

## Evidence Graph

Contains deterministic facts established from a repository snapshot: structure, declarations, framework constructs, relationships, source spans/hashes, extractor provenance and resolution.

The Evidence Graph may include deterministic observations useful to later domain interpretation: calls, mutations, persistence access, transaction constructs, authorization checks, state assignments/transitions, messages, external calls, configuration and other analyzer-supported facts. Their business/domain meaning remains a Finding unless directly established as implementation fact.

## Finding Graph

Contains semantic interpretations and proposals derived from evidence. Examples include candidate business capabilities, bounded contexts, aggregate boundaries, business rules, invariants, workflows, data ownership and security-policy interpretations.

A model response cannot create an Observed fact.

Findings must be source-model neutral. A class or member named `AggregateRoot`, `Entity`, `ValueObject`, `DomainEvent`, or `BoundedContext` is an Observed declaration only; its name alone does not establish its domain meaning. Conversely, the absence of those names does not prevent evidence-backed reconstruction from behavior, rules, invariants, data/mutation/transaction relationships, workflows, state, operations, persistence, messages, security, dependencies, and coupling.

## Classification

- **Observed** — deterministically established.
- **Inferred** — interpretation supported by evidence.
- **Proposed** — architectural/domain recommendation.

Review status is independent of classification. Human acceptance of an inference does not convert it into an observed fact, and acceptance of a proposal does not convert it into an inference or an observed fact.

## Provenance

Source-backed evidence records snapshot, relative file, content hash, source span, extractor/rule/version and resolution (exact/partial/ambiguous/unresolved).

Findings preserve atomic claim, concept type, classification, subject nodes, supporting/counter evidence, assumptions, justification, support/coverage, alternatives, unresolved questions, producer/version, validation/review status and revision history.

## Domain Knowledge Model

In planned V1, eligible validated findings project through deterministic application logic into a queryable, persistent Domain Knowledge Model. Projection eligibility and conflict handling remain open design decisions.

The DKM is one canonical asset with two semantic views:

- **Recovered Domain Knowledge** contains `Inferred` reconstructions of the existing system and business, linked to `Observed` evidence. It may recover a DDD pattern that the implementation actually appears to enforce, but does not treat a matching type name as proof.
- **Proposed DDD Design** contains `Proposed` DDD representations that DomainLens recommends even when no corresponding construct exists in the source system.

Every projected record retains its view, source Finding IDs, classification, evidence, assumptions, support/confidence, alternatives, review state, and revision history. A claim that mixes recovered and proposed meaning must be split into atomic findings before projection.

### Business Architecture
- Business Capabilities
- Actors
- Use Cases
- Domains
- Subdomains: Core, Supporting, Generic

### Strategic DDD
- Bounded Contexts
- Context Map
- Upstream / Downstream
- Shared Kernel
- Customer / Supplier
- Conformist
- Published Language
- Anti-Corruption Layer

### Tactical DDD
- Aggregates
- Aggregate Roots
- Entities
- Value Objects
- Domain Services
- Repositories
- Factories

### Behavior
- Commands
- Domain Events
- Handlers
- Business Rules
- Invariants
- Policies
- Workflows / Business Processes
- State Transitions / Lifecycles
- Decisions
- Preconditions / Postconditions
- Validation Rules

### Application / Integration
- Application Services
- APIs / Operations
- DTOs / Contracts
- Messages
- Integration Events
- External Systems
- Synchronous / Asynchronous Dependencies

### Security
- Authentication
- Authorization
- Roles / Permissions
- Security Policies
- Sensitive Data Rules

### Data & Consistency
- Data Ownership
- Persistence
- Transaction Boundaries
- Consistency Boundaries
- Shared Data

### Architecture Evidence
- Dependencies
- Coupling
- Calls
- Side Effects
- Configuration
- Scheduled Processes
- Error / Exception Behavior
- Audit Requirements
- Concurrency / Locking
- Caching
- Transaction Consistency

## Relationships important to decomposition

DomainLens should explicitly preserve relationships such as:

`Business Capability → Domain/Subdomain → Bounded Context → Aggregate`

`Actor → Use Case → Command/Operation → Business Rule → Aggregate → Domain Event`

`Invariant → Affected Objects → Mutation Paths → Transaction/Consistency Boundary`

It should distinguish data ownership from consumption and retain shared-table/shared-store access as coupling evidence.

## Decomposition boundary

The Domain Knowledge Model describes both reconstructed domain/implementation knowledge and explicitly labeled proposed DDD representations. **Decomposition Analysis operates over this model; Proposed DDD Design is not Decomposition Analysis, and neither DDD nor decomposition recommendations are deterministic Evidence Graph observations.**

This separation supports:

`Source Code → Evidence Graph → Semantic Findings → Domain Knowledge Model { Recovered Domain Knowledge + Proposed DDD Design } → Decomposition Analysis → Modernization Model → Target Architecture`

The Domain Knowledge Model is therefore the durable platform asset for future modernization.
