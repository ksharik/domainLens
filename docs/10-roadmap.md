# Roadmap

## V1 — End-to-End Reverse DDD for legacy .NET/WCF

Azure deployment; public Git intake; native UI/progress; C#/.NET Framework structural analysis; WCF analysis; evidence/relationship graphs; AI-assisted domain discovery and DDD modeling; validation; human clarification; persistent Domain Knowledge Model; evidence-backed explorer.

V1's target knowledge model includes business capabilities, actors/use cases, strategic and tactical DDD, business rules/invariants/workflows, application/integration relationships, security policies, data ownership/consistency and decomposition-relevant coupling evidence.

V1 must work when the analyzed source uses no DDD vocabulary or structure. The canonical Domain Knowledge Model exposes Recovered Domain Knowledge separately from Proposed DDD Design; neither view is a substitute for the later Decomposition Analysis stage.

## Engineering milestones inside V1

0. Deployment/security feasibility spike (prerequisite).
1. Repository Structure Scanner 0.1.
2. WCF discovery.
3. Relationship / persistence / behavioral evidence.
4. Evidence persistence/explorer.
5. Context builder and structured reasoning runtime.
6. Source-model-neutral recovery of domain knowledge.
7. Recovered-DDD interpretation, Proposed DDD Design and validation.
8. Human review/challenge workflow.
9. Azure end-to-end deployment/hardening.

Milestone 3 clarifies the already approved V1 evidence requirement; it is not a new milestone or an
expansion to generalized analyzer discovery. Its deterministic scope needs to address supported
method/call relationships, mutations, conditions and comparisons, validation and exceptions,
state transitions, transaction constructs, security checks, messages, side effects, scheduled
operations, persistence/data ownership and coupling. Artifact-specific depth remains governed by
the [V1 Analysis Coverage](design/01-v1-analysis-coverage.md) specification and explicit open
decisions.

Before the domain-discovery and DDD-modeling milestones, evidence extraction and reasoning contracts must be checked against the expanded Domain Knowledge Model so Domain Vocabulary, business behavior, security, ownership/consistency and coupling concepts are not lost merely because the initial scanner focused on structural evidence. The [Knowledge-to-Evidence Traceability](design/02-knowledge-evidence-traceability.md) matrix records current gaps; a V1 semantic output cannot be considered adequately supported merely because a later reasoning milestone exists.

Quality gates and benchmark fixtures should mature alongside these milestones according to the
[Quality and Evaluation Strategy](quality/01-evaluation-strategy.md). Numeric release thresholds
remain open decisions and must not be inferred from milestone numbering.

Milestones 6 and 7 also have a semantic-quality readiness gate. Their outputs cannot be accepted
as product-ready until the applicable scope has an expert-reviewed corpus of fixtures or
version-pinned representative repositories, structured expected results, and a versioned evaluation rubric. If
Confidence is used, an applicable versioned calibration method, an applicable expert-reviewed
corpus, evaluated calibration results, and explicit product approval to expose Confidence are also
required. Approved semantic acceptance/regression thresholds are required wherever the product
decision calls for them. This is an acceptance gate for the existing milestones, not a new
milestone, and it defines no numeric threshold.

## Future analyzers

Pluggable analyzer families should add: ASP.NET MVC / MVC.NET; ASP.NET Web API; modern .NET APIs/apps; Java; Java EE/Jakarta enterprise applications; Spring Framework; Spring Boot; REST/OpenAPI; standalone relational database/schema and broader persistence-framework analysis beyond the configured V1 code/config evidence; messaging/event-driven systems such as Kafka/RabbitMQ patterns; and additional stacks based on demand.

Repository discovery should eventually detect technologies and build an Analysis Plan automatically.

## Infrastructure evolution

Add Linux workers; containerize where useful; introduce queues/worker pools for concurrency; consider AKS only when scale and scheduling/isolation justify it; move to managed database infrastructure as durability/concurrency requires.

## Beyond Reverse DDD

Decomposition analysis operates over the persisted Domain Knowledge Model rather than being mixed into source evidence or DDD discovery.

`Source Code → Evidence Graph → Semantic Findings → Domain Knowledge Model { Recovered Domain Knowledge + Proposed DDD Design } → Decomposition Analysis → Modernization Model → Target Architecture → Migration Planning`
