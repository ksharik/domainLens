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

## Classification

- **Observed** — deterministically established.
- **Inferred** — interpretation supported by evidence.
- **Proposed** — architectural/domain recommendation.

Review status is independent of classification. Human acceptance of an inference does not convert it into an observed fact.

## Provenance

Source-backed evidence records snapshot, relative file, content hash, source span, extractor/rule/version and resolution (exact/partial/ambiguous/unresolved).

Findings preserve atomic claim, concept type, classification, subject nodes, supporting/counter evidence, assumptions, justification, support/coverage, alternatives, unresolved questions, producer/version, validation/review status and revision history.

## Domain Knowledge Model

In planned V1, eligible validated findings project through deterministic application logic into a queryable, persistent Domain Knowledge Model. Projection eligibility and conflict handling remain open design decisions.

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

The Domain Knowledge Model describes the reconstructed domain and implementation relationships. **Decomposition Analysis operates over this model; decomposition recommendations are not themselves part of deterministic Evidence Graph observations.**

This separation supports:

`Source Code → Evidence Graph → Semantic Findings → Domain Knowledge Model → Decomposition Analysis → Modernization Model → Target Architecture`

The Domain Knowledge Model is therefore the durable platform asset for future modernization.
