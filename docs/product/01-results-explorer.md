# Results Explorer Product Specification

## Status and purpose

**PLANNED — Product V1.** This document defines the information and navigation outcomes required
from the DomainLens results experience. It does not select a UI framework, visual design system,
page layout, chart library, or physical query schema.

The Results Explorer helps a user understand what DomainLens analyzed, what it could not analyze,
what the implementation establishes, what domain meaning was inferred, and what DDD design was
proposed. It is a projection over versioned repository, Evidence Graph, Human Context, Finding
Graph, review, and Domain Knowledge Model records. Generated prose, diagrams, and summaries are
replaceable views; they are not the canonical source of results.

The governing progression remains:

`Source Code -> Evidence Graph -> Semantic Findings -> Domain Knowledge Model { Recovered Domain Knowledge + Proposed DDD Design } -> Decomposition Analysis -> Modernization Model -> Target Architecture`

## Product principles

1. **Traceability before persuasion.** Every material semantic conclusion must answer, “Why does
   DomainLens think this?” with inspectable Evidence, separately attributable Human Context where
   used, and limitations.
2. **Views must not collapse authority.** Observed implementation evidence, versioned Human
   Context, model-generated Inferred recovered knowledge, Proposed DDD Design, and human review
   state remain independently attributable and visible.
3. **Coverage and uncertainty are first-class.** Users must see exclusions, partial analysis,
   ambiguous or unresolved relationships, and missing artifact categories alongside conclusions.
4. **Absence is bounded.** “Not found within analyzed scope” must never be presented as “does not
   exist.”
5. **Source-model neutrality.** The experience must remain useful for layered, transaction-script,
   anemic, procedural, service-oriented, monolithic, partially domain-oriented, and explicit-DDD
   systems. DDD terminology in source is neither required nor conclusive.
6. **Human activity is attributable and revisioned.** Human Context creation/correction and human
   review actions retain separate histories. Neither rewrites evidence or epistemic classification.

## Results workspace

The following areas are conceptual information spaces, not prescribed screens or navigation
controls.

### Overview

The overview must orient the user before presenting semantic conclusions:

- repository identity, selected ref/revision, snapshot identity, and analysis-run identity;
- current or terminal pipeline state and deterministic analyzer outcomes;
- configured V1 analyzer profile and producer versions;
- artifact and analysis-dimension coverage summaries;
- unsupported, excluded, partial, ambiguous, and unresolved areas;
- major limitations, contradictions, unresolved questions, and required clarification;
- concise architecture and domain summaries with links to their underlying records; and
- freshness/version information for Evidence Graph, applicable Human Context, Finding Graph, and
  Domain Knowledge Model.

A completion state must not imply complete analysis. A completed run may contain partial coverage
and unresolved questions, and the experience must show those dimensions separately.

### As-Is implementation

The implementation view presents deterministic structure and relationships without silently
assigning business or DDD meaning. Where the configured analyzers support them, it exposes:

- solutions, projects, assemblies, namespaces, types, members, and dependencies;
- WCF services, contracts, operations, faults, data/message contracts, endpoints, bindings,
  behaviors, clients, and hosting configuration;
- service implementations, APIs/operations, DTOs/contracts, messages, and integrations;
- call, mutation, validation, exception, state-transition, transaction, persistence, external-call,
  security-check, scheduled-operation, and side-effect evidence;
- data-access relationships, shared data, ownership/consumption indicators, and coupling; and
- analyzer diagnostics, resolution quality, and artifact-specific coverage.

Observed declarations and relationships may be correlated with findings, but the implementation
view must not label a class as an aggregate root or a namespace as a bounded context merely because
of its name or location.

### Recovered Domain Knowledge

This view contains `Inferred` reconstruction of the existing system and business, including where
supported:

- business capabilities, actors, and use cases;
- Domain Vocabulary: business terms, candidate definitions, aliases, abbreviations, acronyms,
  context-specific meanings, conflicts, ambiguous usages, and source evidence;
- domains, subdomains, and Core/Supporting/Generic interpretations;
- business rules, invariants, policies, decisions, validations, workflows, state transitions, and
  lifecycles;
- data ownership, transaction/consistency boundaries, security policies, external systems, and
  coupling; and
- recovered DDD patterns only where behavior and relationships support that interpretation.

Each item must retain its recovered semantic view and `Inferred` classification, including after
human acceptance.

### Proposed DDD Design

This separately labeled view contains recommendations rather than claims about what exists:

- proposed bounded contexts, context relationships, and context map;
- proposed aggregates, aggregate roots, entities, and value objects;
- proposed domain/application services, repositories, and factories;
- proposed commands, events, messages, and handlers; and
- rationale, assumptions, trade-offs, alternatives, consequences, and unresolved design questions.

Proposed DDD Design must be visually and semantically distinguishable from Recovered Domain
Knowledge in navigation, labels, exports, explanations, and combined diagrams. Human acceptance
changes review state only; an accepted proposal remains `Proposed` and does not become as-is
knowledge.

### Evidence

The Evidence workspace must permit navigation through:

- repository-relative files and immutable content hashes;
- source spans and bounded source excerpts when retention and access policy permit them;
- Evidence IDs, node and relationship identities, and analyzer/rule versions;
- graph relationships and resolution basis/quality (`Exact`, `Partial`, `Ambiguous`, or
  `Unresolved`);
- artifact, parser/analyzer, and analysis-dimension coverage plus known exclusions; and
- diagnostics for unsupported, malformed, unavailable, unsafe, or resource-excluded material.

Coverage summaries are measurements about the analysis; they are not source evidence and must not
be inserted into the Evidence Graph as facts about the analyzed business.

### Human Context

Human Context (or Domain Assertion; exact name and schema are an **OPEN DECISION**) records
human-supplied business or organizational knowledge that may not be available in source. The
explorer must label this material separately from Evidence Graph records, model interpretation, and
review state. It should expose the context/assertion ID, applicable analysis or repository scope,
eliciting question, supplied statement, timestamp, revision or supersession lineage, applicable
concepts/findings, and status or validation where appropriate. An actor, session, or principal
reference is displayed only according to the identity model eventually approved; this does not
introduce an authentication or multi-tenancy requirement.

Human Context is durable and versioned when it materially affects a finding or DKM projection. A
correction or supersession adds a revision without rewriting history. It is not `Observed`
deterministic evidence and does not introduce a new epistemic classification. A resulting Recovered
Domain Knowledge finding may remain `Inferred` while citing both Evidence IDs and Human Context
revision IDs.

### Review

The review workspace exposes:

- clarification questions and their linked, versioned Human Context records;
- ambiguous, contradictory, weakly supported, and consequential findings;
- Proposed findings requiring explicit review;
- accepted, rejected, challenged, superseded, and pending review states;
- alternative interpretations and counterevidence;
- requests for re-analysis and the resulting finding revisions; and
- a chronological, auditable revision and decision history.

Filtering by review state must not relabel classification. For example, “accepted proposals” are
still `Proposed`, and “accepted recovered findings” are still `Inferred`.
Supplying or revising Human Context and accepting, rejecting, or challenging a finding are distinct
actions with separate histories.

## “Why does DomainLens think this?” contract

Every semantic finding and DKM projection must provide an explanation path containing:

| Information | Required user-visible meaning |
|---|---|
| Atomic claim | The specific conclusion being evaluated, without combining as-is and recommendation intent. |
| Semantic view | `RecoveredDomainKnowledge` or `ProposedDddDesign`. |
| Classification | `Inferred` or `Proposed`; Observed facts remain linked Evidence Graph records. |
| Supporting evidence | Evidence IDs with source locations, relationships, provenance, and resolution quality. |
| Counterevidence | Evidence that weakens or conflicts with the claim, or an explicit statement that none was found within the analyzed scope. |
| Human Context | Material Human Context revision IDs, supplied statements, provenance, scope, and supersession/status information, explicitly distinguished from repository evidence. |
| Assumptions | Conditions used by the interpretation but not established as source facts. |
| Support | How strongly the available deterministic evidence and explicitly attributed Human Context support this particular claim without conflating their provenance. |
| Confidence | A calibrated interpretation distinct from Support, shown only after the calibration and product-exposure gate is satisfied; otherwise unavailable, not calibrated, or omitted—not raw model self-confidence. |
| Coverage and completeness | How much relevant material was analyzed and how complete this requested dimension is believed to be, with scope and limitations. |
| Known limitations | Missing artifacts, unavailable references, unsupported constructs, pruning, ambiguous resolution, and other constraints. |
| Alternatives | Plausible competing interpretations and why they were not selected or remain unresolved. |
| Review status | Pending, accepted, rejected, challenged, or superseded without changing classification. |
| Lineage | Snapshot, ContextPack, Human Context revisions when used, finding revision, producer versions, and DKM version. |

An explanation may summarize these records, but the underlying structured references remain the
authority. Unknown or unavailable fields must be shown as unknown, not replaced with confident
narrative.

For example, an `Inferred` finding that Payments is a Core Subdomain might cite payment flows,
dependencies, and transaction ownership as repository evidence while separately citing the Human
Context statement, “Payments is a primary differentiating capability for this organization.” The
explorer must preserve both provenance paths rather than presenting the human statement as source
evidence or presenting the entire conclusion as unaided model inference.

## Coverage and comparison behavior

The explorer must preserve the conceptual distinction among Coverage, Support, Confidence,
Completeness, and deterministic Resolution Quality according to the
[evaluation strategy](../quality/01-evaluation-strategy.md). Confidence is populated or exposed
only after a versioned calibration method, an applicable expert-reviewed evaluation corpus,
evaluated calibration results, and an approved product decision to expose it all exist. Until then,
the eventual schema may present it as unavailable or not calibrated, or omit it from user-facing
views. The explorer must never generate pseudo-confidence from raw model output or silently relabel
Support as Confidence.

High Support with low Coverage is not equivalent to medium Support with high Coverage. Comparisons
across runs are meaningful only when snapshot scope, analyzer versions, configured capability
profile, and measurement definitions are compatible and visible.

No arbitrary product threshold is selected here. Policies for suppressing, warning, requiring
review, or blocking projection based on these dimensions are **OPEN DECISIONS**.

## Queries, projections, and exports

The product should support navigation in both directions:

- source/evidence -> findings -> DKM concepts and relationships; and
- DKM conclusion -> finding revision -> ContextPack -> separately attributed Human Context and
  evidence -> source location where applicable.

Cross-cutting queries should correlate capabilities, vocabulary, rules, workflows, security,
ownership, APIs/messages, dependencies, and DDD interpretations without erasing their source layer
or semantic view. Exports and diagrams must carry the same view, classification, Human Context
provenance where material, coverage, limitation, review-state, and version labels as the interactive
experience.

## Open decisions

- Information architecture, UI framework, visual language, and accessibility targets.
- Query/API contracts and pagination for large graphs and revision histories.
- Source-retention and excerpt-display policy, including redaction and access controls.
- Visualization choices for context maps, workflows, call graphs, state models, and evidence paths.
- Coverage aggregation and comparison rules; suppression, warning, and review thresholds.
- Confidence calibration method, eligible finding types, evaluation corpus and cadence, and the
  product decision about whether and how calibrated Confidence should be exposed.
- Default review gates by finding type, consequence, uncertainty, and semantic view.
- Human Context versus Domain Assertion naming, schema, status/validation semantics, provenance
  presentation, revision/supersession experience, and identity references under an approved
  identity model.
- Export formats and how interactive relationships are preserved in static outputs.
- Cross-run comparison, stale-result signaling, and re-analysis triggers.

## Related documentation

- [V1 Scope](../02-v1-scope.md)
- [Functional Requirements](../03-functional-requirements.md)
- [Analysis and Evidence Model](../05-analysis-model.md)
- [Evaluation Strategy](../quality/01-evaluation-strategy.md)
- [V1 Analysis Coverage](../design/01-v1-analysis-coverage.md)
- [Knowledge-to-Evidence Traceability](../design/02-knowledge-evidence-traceability.md)
- [Evidence Architecture](../architecture/05-evidence-architecture.md)
- [Domain Knowledge Architecture](../architecture/06-domain-knowledge-architecture.md)
