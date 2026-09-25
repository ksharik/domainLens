# Architecture and Design Baseline

This section describes how DomainLens is structured to satisfy the approved product requirements. The requirements under [`docs/`](../01-product-overview.md) remain authoritative for what the product must do; these documents define architectural boundaries, responsibilities, flows, and constraints. Accepted choices and their rationale are recorded separately in the [ADR index](../adr/README.md).

> **Code establishes evidence. AI interprets evidence. The agent orchestrates the process.**

> **Source-model neutrality:** DomainLens shall not require or assume that an analyzed application was originally designed using Domain-Driven Design. DDD concepts may be recovered, inferred, or proposed from implementation evidence even when corresponding DDD constructs do not explicitly exist in the source system.

## Status language

Every architecture document uses the following labels deliberately:

- **CURRENT — Milestone 0 feasibility:** executable spike evidence for a local Windows-oriented
  child worker and narrow net472 semantic enrichment. It is not production containment or an
  Azure deployment.
- **CURRENT — Scanner 0.1:** behavior implemented on `development` today. It is a local .NET 10 CLI/library vertical slice, not the complete V1 product.
- **PLANNED V1:** approved target architecture or behavior required for the end-to-end Azure product, but not necessarily implemented.
- **FUTURE:** an evolution explicitly outside the V1 commitment.
- **OPEN DECISION:** a design choice that has not been approved. The documentation states the required boundary without selecting an implementation.

The architectural layer invariant is:

```text
Deterministic Evidence
        ↓
Semantic Findings
        ↓
Persistent Domain Knowledge Model
        ├── Recovered Domain Knowledge
        └── Proposed DDD Design
        ↓
Decomposition Analysis
        ↓
Future Modernization Model
```

The Domain Knowledge Model is one canonical asset, not two databases. Its semantic view, epistemic classification, and review state are separate axes. Information may be traced forward and backward across these layers, but it does not silently change any of them. Model output cannot create or modify Observed evidence; recovered knowledge remains `Inferred`; Proposed DDD Design remains `Proposed` even when a human accepts it. Acceptance changes review state only.

## Document map

| Document | Primary concern |
|---|---|
| [01 — System Context](01-system-context.md) | Users, DomainLens, and external systems |
| [02 — Logical Architecture](02-logical-architecture.md) | Logical components and responsibility boundaries |
| [03 — Runtime Architecture](03-runtime-architecture.md) | Processes, trust zones, and analyzer-worker lifecycle |
| [04 — Analysis Pipeline](04-analysis-pipeline.md) | End-to-end control/data flow and pipeline states |
| [05 — Evidence Architecture](05-evidence-architecture.md) | Implemented evidence schema, identity, provenance, and validation |
| [06 — Domain Knowledge Architecture](06-domain-knowledge-architecture.md) | Persistent knowledge categories, relationships, and decomposition boundary |
| [07 — Agent and Reasoning Architecture](07-agent-reasoning-architecture.md) | Coordinator, Context Packs, skills, model boundary, and validation |
| [08 — Persistence Architecture](08-persistence-architecture.md) | Durable concepts, immutability, versioning, and operational state |
| [09 — Security Architecture](09-security-architecture.md) | Threat boundaries and current/planned controls |
| [10 — Deployment Architecture](10-deployment-architecture.md) | Conceptual Azure roles without premature service selection |
| [11 — Extensibility Architecture](11-extensibility-architecture.md) | Configured V1 analyzers, future technology discovery/analysis planning, and analyzer contracts |
| [12 — Observability and Operations](12-observability-and-operations.md) | Telemetry, diagnostics, audit, reliability, and cost visibility |

Supporting product and design specifications refine this baseline without replacing its authority:

| Specification | Primary concern |
|---|---|
| [Results Explorer](../product/01-results-explorer.md) | User-visible navigation, explanation, evidence, uncertainty, and review outcomes |
| [Quality and Evaluation Strategy](../quality/01-evaluation-strategy.md) | Deterministic, semantic and operational quality plus benchmark strategy |
| [V1 Analysis Coverage](../design/01-v1-analysis-coverage.md) | Intended treatment, ownership and limitations of legacy .NET/WCF artifacts |
| [Knowledge-to-Evidence Traceability](../design/02-knowledge-evidence-traceability.md) | Evidence and analyzer prerequisites for Domain Knowledge outputs |
| [Milestone 0 Feasibility Report](../12-milestone-0-deployment-security-feasibility.md) | Tested child-process, security and legacy semantic-analysis mechanisms, constraints and production gaps |

## Current implementation and target product

| Capability | CURRENT — M0/M1 evidence slice | PLANNED V1 |
|---|---|---|
| Input | Local repository path and optional solution selection | Public Git URL, selected immutable revision, and validated snapshot |
| Analysis | Bounded inventory, declarative solution/project reading, syntax-first C# extraction, plus an M0 net472 `SemanticModel` feasibility result over one flattened manifest-source compilation, the exact tool-owned reference catalog, and declared `Partial` resolution | Configured, approved legacy C#/.NET Framework/WCF structural and WCF analyzers plus one Relationship / persistence / behavioral evidence capability that includes supported security evidence; generalized automatic technology discovery and generated Analysis Plans are FUTURE |
| Output | Validated `domainlens.evidence.v1` Evidence Graph in canonical JSON | Evidence Graph, validated Finding Graph, one versioned Domain Knowledge Model with recovered and proposed-DDD views, and evidence-backed presentations |
| Runtime | Local CLI plus an M0 separate-child-process feasibility host with bounded staging/results, worker-lifetime/result-acceptance deadline and cancellation, result gates and cleanup; no hard deadline for synchronous trusted postprocessing, production OS containment, or Azure deployment | Azure-hosted UI/API/Core plus an isolated Windows-capable analyzer worker |
| Reasoning | None | Fixed coordinator workflow, sealed Context Packs, allowlisted skills, and structured model output |
| Persistence | Output JSON chosen by the CLI caller | Durable repository/run state, evidence, Human Context revisions, finding revisions, human review decisions, and knowledge-model versions |
| Human interaction | CLI scan and symbol inspection | Progress, clarification, review, challenge, explanation, and result exploration |

The detailed implementation authorities for the current slice are [Repository Structure Scanner 0.1](../11-milestone-1-repository-scanner.md) and the [Milestone 0 Feasibility Report](../12-milestone-0-deployment-security-feasibility.md). Product scope is defined in [V1 Scope](../02-v1-scope.md) and [Functional Requirements](../03-functional-requirements.md).

## Authority and dependency rules

When documents appear to overlap, apply these rules:

1. Product and functional requirement documents define required outcomes.
2. This architecture baseline defines structural and runtime design.
3. Accepted ADRs explain why stable choices were made.
4. The milestone document and source code define what Scanner 0.1 actually implements.
5. Roadmap text defines sequence and direction, not present-tense capability.

Logical component names do not imply independently deployed services. The planned analyzer worker is a required isolation boundary; other V1 components may remain modules in a modular application unless an approved decision establishes another deployment boundary.

Product V1 has a known analyzer family and does not require generalized automatic technology
discovery or generated Analysis Plans. The V1 coordinator dispatches the configured, approved
legacy C#/.NET Framework/WCF analyzer capabilities and may reject an unsupported repository through
a bounded deterministic qualification check. Automatic selection across analyzer families remains
FUTURE.

Likewise, references to a DomainLens user, client, or access boundary do not select an identity or
tenancy model. Persistent data and operations must be access-controlled and isolated under the
eventually approved identity, ownership, and authorization model, while authentication, anonymous
access, roles, sharing, and tenancy remain open.

## Open-decision register

The following choices are intentionally unresolved. Each is owned by the document that supplies its constraints and should be closed by a future ADR or design milestone, not by implication:

| Area | Open decisions | Owner |
|---|---|---|
| Intake and identity | Supported Git hosts/protocols/ref forms, redirects, submodules/LFS, authentication requirement/provider, user identity and ownership model, tenancy, anonymous access, roles/permissions, sharing, and client API versioning | [System Context](01-system-context.md), [Security](09-security-architecture.md) |
| Runtime isolation | Durable job transport, Windows OS-containment/hosting mechanism, production limits, identity, network policy, cancellation guarantees, cleanup, and Windows/Linux routing | [Runtime](03-runtime-architecture.md) |
| Pipeline | State/checkpoint schema, retry categories and budgets, configured V1 analyzer-job contract, human-pause expiry, and partial-run completion policy | [Analysis Pipeline](04-analysis-pipeline.md) |
| Evidence | Schema evolution, multi-analyzer merge rules, Git revision capture, and cross-snapshot logical identity/rename handling | [Evidence](05-evidence-architecture.md) |
| Domain knowledge | Concept cardinalities, semantic-view encoding/cross-view relationships, quality-dimension representation/rubrics/aggregation/calibration, conditional Confidence availability, finding projection eligibility, version lineage, and decomposition-result schema | [Domain Knowledge](06-domain-knowledge-architecture.md) |
| Reasoning and context | Model provider/deployment/retention, structured finding schema, support thresholds, repair budget, ContextPack budget/version, Human Context inclusion/validation, and evaluation gate for semantic retrieval | [Agent and Reasoning](07-agent-reasoning-architecture.md) |
| Persistence | Storage products, artifact/relational split, Human Context naming/schema/revision/supersession, transactions, migrations, encryption, retention/deletion, and concurrent updates | [Persistence](08-persistence-architecture.md) |
| Security | URL/DNS/redirect policy, egress/redaction/consent, secret handling, OS-containment technology, workspace sanitization, identity/ownership authorization and isolation, and audit access | [Security](09-security-architecture.md) |
| Deployment | Azure services, region/DR/network topology, scale model, queues, containers, and any future AKS threshold | [Deployment](10-deployment-architecture.md) |
| Extensibility | Analyzer registration/loading, contract negotiation, future discovery heuristics/generated Analysis Plans, capability scheduling, and conformance suite | [Extensibility](11-extensibility-architecture.md) |
| Operations | Telemetry backend, SLOs, alerts, sampling, retention, redaction, cost budgets, and incident policy | [Observability](12-observability-and-operations.md) |
| Quality and evaluation | Coverage denominators, support/completeness rubrics, Confidence calibration/exposure readiness, benchmark annotations, release thresholds, and human-agreement policy | [Evaluation Strategy](../quality/01-evaluation-strategy.md) |
| V1 artifact support | Partial/deferred artifact choices, safe semantic enrichment, generated/third-party inputs, SQL/stored-procedure depth, and conformance criteria | [V1 Analysis Coverage](../design/01-v1-analysis-coverage.md) |
| Results experience | Query/API contract, visualizations, source excerpt policy, review gates, exports, and cross-run comparison | [Results Explorer](../product/01-results-explorer.md) |

## Terminology reconciliations

This baseline normalizes two older ambiguities:

- **Domain Knowledge Model** is the canonical term; older references to a “Persistent DDD Model” mean this model.
- **Recovered Domain Knowledge** and **Proposed DDD Design** are explicitly tagged semantic views of that one model. They are not separate stores and must not be silently blended in queries or presentation.
- **Repository Structure Scanner 0.1 is Milestone 1.** The deployment/security feasibility spike is a prerequisite numbered Milestone 0 in the updated roadmap.

Pipeline lifecycle state and a scanner stage's result quality are separate dimensions. The product pipeline can be running, paused, completed, failed, or cancelled, while a deterministic analysis result can independently be `Success`, `PartialSuccess`, or `Failure`.
