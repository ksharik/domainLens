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

## Domain vocabulary

DomainLens shall represent Domain Vocabulary / Ubiquitous Language as evidence-backed domain
knowledge. It shall support business terms, candidate definitions, synonyms, aliases,
abbreviations, acronyms, ambiguous usages, conflicting meanings and context-specific meanings,
with links to observed source locations and usages.

Identifiers, comments, labels, operation names, contracts, configuration and messages may provide
Observed text or usage evidence. A candidate business definition is `Inferred`; a recommended
normalized term is `Proposed`. Neither may be created as Observed evidence by a model. Vocabulary
conflicts may contribute to a bounded-context finding when combined with behavior, ownership,
rules, workflows and integration evidence; they do not establish a context boundary alone.

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

The user shall be able to distinguish analysis completion from analysis coverage. Coverage,
Support, Confidence, Completeness and deterministic Resolution Quality shall remain separately
represented, with known limitations and counterevidence. “Not found within analyzed scope” must
not be presented as “does not exist.” Numeric quality thresholds remain open until approved
through evaluation.

## Results and persistence

Users shall explore As-Is architecture/evidence and the Domain Knowledge Model, navigating findings to supporting source locations.

Every semantic finding shall support an explanation equivalent to “Why does DomainLens think
this?” showing its semantic view, classification, supporting and counterevidence, assumptions,
Support, Confidence, Coverage, Completeness, linked Resolution Quality, limitations, alternatives,
review status and revision lineage.
Proposed DDD Design shall remain visibly and semantically distinct from Recovered Domain
Knowledge in interactive views, diagrams and exports.

Persist repository/snapshot metadata, runs, evidence, findings, domain-knowledge concepts/relationships, user decisions, revisions and diagnostics. Persistence shall be abstracted from the database engine.

The Domain Knowledge Model shall remain one canonical persistent asset with separately queryable and visibly labeled **Recovered Domain Knowledge** and **Proposed DDD Design** views. Records in both views shall retain supporting and contradictory evidence, source findings, classification, assumptions, Support, Confidence, Coverage, Completeness, linked Resolution Quality, alternatives, review state and revision history. Presentation and persistence must not imply that a Proposed DDD construct existed in the source system.

Human acceptance, rejection or challenge shall change review state or create a revision without changing epistemic classification. In particular, an accepted `Proposed` finding remains `Proposed`, and an accepted `Inferred` finding does not become `Observed`.

Persisted Domain Knowledge Models must be reusable by decomposition and future modernization workflows without reparsing generated prose.

See the [Results Explorer specification](product/01-results-explorer.md) and
[Quality and Evaluation Strategy](quality/01-evaluation-strategy.md) for the product-facing and
evaluation contracts. Those documents do not select a physical UI or persistence schema.
