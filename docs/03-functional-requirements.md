# Functional Requirements

## Repository intake

The UI shall accept a public Git repository URL, validate supported hosts/URL forms, safely select a branch/tag/commit where appropriate, and analyze an identifiable repository snapshot.

## Pipeline

The coordinator shall expose durable stages conceptually similar to:

`Created → Snapshotted → Inventoried → DeterministicAnalysis → WcfAnalysis → EvidenceReady → ContextPrepared → SemanticReasoning → Validating → ReviewReady → Completed`

`Failed` and `Cancelled` are explicit outcomes. Stages should be idempotent/resumable where practical.

## Domain reconstruction

DomainLens shall use deterministic evidence and evidence-backed semantic reasoning to construct a Domain Knowledge Model.

The system shall support discovery of business capabilities, actors/use cases, strategic DDD boundaries/relationships, tactical DDD concepts, business behavior, application/integration relationships, security policies, data/consistency characteristics and cross-cutting architecture knowledge as defined in the V1 scope.

Business capability discovery shall provide a business-oriented organizing layer above implementation structure. Package, namespace and project boundaries must not automatically be treated as business boundaries.

## Behavioral analysis

DomainLens shall be capable of representing business rules, invariants, policies, workflows/business processes, state transitions/lifecycles, decisions, preconditions/postconditions and validation rules.

Where evidence permits, invariants shall be related to affected domain objects, mutation paths and transaction/consistency boundaries because these relationships are significant evidence for aggregate-boundary analysis.

Workflow/state analysis should relate actors, use cases, commands/operations, rules, aggregates, state transitions and resulting events when supported.

## Security analysis

DomainLens shall represent authentication, authorization, roles/permissions, security policies and sensitive-data rules that are observable or reasonably inferable from analyzed artifacts.

Security findings must retain provenance and classification. DomainLens must not claim that an undiscovered policy does not exist merely because it is absent from the repository.

## Data ownership and consistency

DomainLens shall distinguish data ownership from data consumption where evidence permits.

The system shall identify persistence access, shared tables/data stores, transaction boundaries, consistency boundaries and cross-context shared-data dependencies. Shared data shall be available as coupling evidence for later decomposition analysis.

## Dependency and coupling analysis

For candidate bounded contexts, DomainLens should make available the business capabilities, aggregates, business rules, invariants, owned/consumed data, exposed APIs, produced/consumed events, external dependencies, shared data, transaction coupling and applicable security policies.

Decomposition recommendations are a later analysis over the Domain Knowledge Model. They must not be stored as deterministic source evidence.

## Progress and human interaction

The UI shall display stage/progress/diagnostics. The pipeline may pause when user clarification materially affects DDD interpretation. Responses are recorded and incorporated into subsequent reasoning. Findings may be challenged and re-analyzed without destroying revision history.

## Results and persistence

Users shall explore As-Is architecture/evidence and the Domain Knowledge Model, navigating findings to supporting source locations.

Persist repository/snapshot metadata, runs, evidence, findings, domain-knowledge concepts/relationships, user decisions, revisions and diagnostics. Persistence shall be abstracted from the database engine.

Persisted Domain Knowledge Models must be reusable by decomposition and future modernization workflows without reparsing generated prose.
