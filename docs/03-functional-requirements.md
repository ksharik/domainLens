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

DomainLens shall not require or assume that the analyzed application already uses DDD. It shall support N-tier, transaction-script, anemic-domain-model, service-oriented, procedural, tightly coupled monolithic, partially domain-oriented, and explicitly DDD systems. DDD-named types or members are evidence about source declarations, not proof of DDD semantics, and their absence shall not prevent recovery or proposal of DDD concepts.

Domain reconstruction shall reason from supported observations of behavior, rules, invariants, data relationships, mutation paths, transaction boundaries, workflows, state transitions, operations, persistence, messages, security policies, dependencies and coupling. It must not depend on constructs named `AggregateRoot`, `Entity`, `ValueObject`, `DomainEvent`, or `BoundedContext`.

Business capability discovery shall provide a business-oriented organizing layer above implementation structure. Package, namespace and project boundaries must not automatically be treated as business boundaries.

## Behavioral analysis

DomainLens shall be capable of representing business rules, invariants, policies, workflows/business processes, state transitions/lifecycles, decisions, preconditions/postconditions and validation rules.

Where evidence permits, invariants shall be related to affected domain objects, mutation paths and transaction/consistency boundaries because these relationships are significant evidence for aggregate-boundary analysis.

Workflow/state analysis should first relate actors, use cases, service operations, rules, affected data, state transitions, persistence and messages when supported. Any mapping of those relationships to commands, aggregates or domain events must state whether it is recovered from existing behavior or proposed as DDD design.

## Security analysis

DomainLens shall represent authentication, authorization, roles/permissions, security policies and sensitive-data rules that are observable or reasonably inferable from analyzed artifacts.

Security findings must retain provenance and classification. DomainLens must not claim that an undiscovered policy does not exist merely because it is absent from the repository.

## Data ownership and consistency

DomainLens shall distinguish data ownership from data consumption where evidence permits.

The system shall identify persistence access, shared tables/data stores, transaction boundaries, consistency boundaries and cross-context shared-data dependencies. Shared data shall be available as coupling evidence for later decomposition analysis.

## Dependency and coupling analysis

For candidate bounded contexts, DomainLens should make available the business capabilities, business rules, invariants, affected concepts/data, exposed APIs, messages, external dependencies, shared data, transaction coupling and applicable security policies, plus any separately labeled recovered or proposed aggregate/event interpretations.

Decomposition recommendations are a later analysis over the Domain Knowledge Model. They must not be stored as deterministic source evidence.

Proposed DDD Design is also not Decomposition Analysis. Reverse DDD asks what domain knowledge can be reconstructed and how that domain can be represented using DDD; Decomposition Analysis asks how the existing application could be separated or reorganized; Modernization asks what the future implementation architecture should become.

## Progress and human interaction

The UI shall display stage/progress/diagnostics. The pipeline may pause when user clarification materially affects DDD interpretation. Responses are recorded and incorporated into subsequent reasoning. Findings may be challenged and re-analyzed without destroying revision history.

## Results and persistence

Users shall explore As-Is architecture/evidence and the Domain Knowledge Model, navigating findings to supporting source locations.

Persist repository/snapshot metadata, runs, evidence, findings, domain-knowledge concepts/relationships, user decisions, revisions and diagnostics. Persistence shall be abstracted from the database engine.

The Domain Knowledge Model shall remain one canonical persistent asset with separately queryable and visibly labeled **Recovered Domain Knowledge** and **Proposed DDD Design** views. Records in both views shall retain supporting and contradictory evidence, source findings, classification, assumptions, confidence/support, alternatives, review state and revision history. Presentation and persistence must not imply that a Proposed DDD construct existed in the source system.

Human acceptance, rejection or challenge shall change review state or create a revision without changing epistemic classification. In particular, an accepted `Proposed` finding remains `Proposed`, and an accepted `Inferred` finding does not become `Observed`.

Persisted Domain Knowledge Models must be reusable by decomposition and future modernization workflows without reparsing generated prose.
