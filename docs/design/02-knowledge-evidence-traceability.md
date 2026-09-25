# Knowledge Requirement to Evidence Traceability

## Purpose and status

This document traces each major Product V1 Domain Knowledge Model (DKM) output to the minimum
implementation evidence needed to support it, the deterministic capability expected to establish
that evidence, and the semantic reasoning stage that may interpret it. It is a requirements and gap
analysis, not a physical analyzer design or a commitment to an unapproved schema.

The governing separation remains:

> **Code establishes evidence. AI interprets evidence. The agent orchestrates the process.**

Scanner 0.1 currently establishes repository, solution, project, declaration, signature-dependency,
source-span, and provenance facts. It does not inspect method bodies, construct a call graph, analyze
WCF semantics, establish persistence behavior, or produce semantic findings. The additional
deterministic capabilities named below are **PLANNED V1 requirements or identified gaps**, not
current behavior.

This traceability follows the canonical information flow:

`Source Code → Evidence Graph → Semantic Findings → Domain Knowledge Model { Recovered Domain Knowledge + Proposed DDD Design } → Decomposition Analysis → Modernization Model → Target Architecture`

It also preserves these invariants:

- deterministic analyzers alone create `Observed` Evidence Graph records;
- recovered semantic knowledge is `Inferred` and belongs to **Recovered Domain Knowledge**;
- a recommended DDD representation is `Proposed` and belongs to **Proposed DDD Design**;
- human acceptance changes review state, not classification or semantic view;
- a DDD-named type is declaration evidence, not proof that the corresponding pattern exists;
- source systems need not use DDD vocabulary or structure;
- coverage measurements and model confidence are not evidence; and
- failure to find a concept within analyzed scope does not prove that it does not exist.

See [Analysis and Evidence Model](../05-analysis-model.md),
[V1 Analysis Coverage](01-v1-analysis-coverage.md),
[Evidence Architecture](../architecture/05-evidence-architecture.md),
[Domain Knowledge Architecture](../architecture/06-domain-knowledge-architecture.md), and
[Agent and Reasoning Architecture](../architecture/07-agent-reasoning-architecture.md) for the
layer contracts used here.

## How to read the matrices

The analyzer names below describe deterministic **capabilities**. They do not require separate
processes or select an implementation technology.

| Horizon/status | Meaning |
|---|---|
| **Current — M1 slice** | Scanner 0.1 already supplies only the stated structural evidence. It does not produce the DKM output. |
| **V1 — path identified** | The approved roadmap names a capability that can supply a substantial part of the required evidence, subject to the artifact-coverage contract and implementation validation. |
| **V1 — evidence gap** | The DKM output is an approved V1 requirement, but the roadmap still lacks explicit deterministic extraction acceptance criteria sufficient to support it. This is not a deferral to Future. |
| **V1 — human context likely** | Repository evidence can support the output, but source alone commonly cannot establish the business meaning; limitations, alternatives, and clarification must remain visible. |
| **Open artifact dependency** | Sufficiency depends on an unresolved support decision for artifacts such as SQL, mappings, generated clients, third-party assemblies, or scheduling configuration. |

All requested outputs below are Product V1 knowledge goals. **FUTURE generalized automatic
Technology Discovery and generated Analysis Plans are not prerequisites for them.** V1 uses the
configured, approved legacy C#/.NET Framework/WCF analyzer path, optionally preceded by bounded
support qualification. Future technology discovery may eventually select other analyzer families;
it must not be used to disguise a missing V1 evidence capability.

Resolution quality (`Exact`, `Partial`, `Ambiguous`, or `Unresolved`) qualifies deterministic
relationships. It is independent of semantic support, confidence, coverage, and completeness.
References below to “M3 clarification needed” mean the new broader Milestone 3 title needs detailed
artifact, resolution, coverage and acceptance criteria; they do not request another milestone.

## Business architecture and strategic DDD

| Knowledge output | Minimum evidence needed | Deterministic source/analyzer or stage | Semantic reasoning stage | Horizon and sufficiency |
|---|---|---|---|---|
| Business Capability | Operations grouped by behavior and outcomes; participating rules, workflows, data, messages, external dependencies, and vocabulary; entry-point-to-effect traces | WCF operation evidence (M2); call, behavior, persistence, message, and side-effect extraction (M3 clarification needed); lexical/context retrieval | Domain discovery → Recovered (`Inferred`); alternatives may be reviewed by a human | **V1 — evidence gap.** Structural and WCF signatures alone cannot establish business capability. |
| Actor | Entry points; authentication/authorization context; callers and generated clients where available; operation usage; scheduled or system triggers; repository documentation only as untrusted evidence | WCF/service/config analyzer (M2); caller, security, and scheduled-trigger extraction (M3 clarification needed) | Domain discovery → Recovered (`Inferred`), often with human clarification | **V1 — human context likely; evidence gap.** External human roles are frequently absent from server source. |
| Use Case | Actor or trigger; initiating operation/message; ordered calls; rules; data reads/writes; state changes; result/fault; external effects | WCF operation analyzer (M2); call/behavior/data-flow/message/state/exception extraction (M3 clarification needed) | Domain discovery → Recovered (`Inferred`) | **V1 — evidence gap.** An operation name is not a complete use case. |
| Domain Vocabulary | Identifiers, contract names, parameters, enum values, validation/error terms, message fields, configuration labels, comments/docs/strings as untrusted text; usage location and co-occurrence by capability | Structural and WCF evidence (M1/M2); lexical-term and usage extraction plus provenance; context retrieval | Domain discovery → candidate terms, meanings, synonyms, aliases, acronyms, conflicts, and context-specific meanings (`Inferred`) | **V1 — evidence gap; human context likely.** Text can be observed, but business meaning cannot be derived deterministically from names alone. |
| Domain / Subdomain | Capability clusters; vocabulary; workflows; rules; owned data; service boundaries; transaction patterns; integrations and coupling | Composite graph over WCF, behavior, persistence, security, and integration evidence | Domain discovery → Recovered (`Inferred`); proposed restructuring, if any, must remain separate | **V1 — evidence gap; human context likely.** Projects and namespaces are not domain boundaries. |
| Core / Supporting / Generic classification | Domain/subdomain evidence plus differentiation, strategic importance, replaceability/commodity signals, and organizational/business context | Repository evidence can establish implementation and dependency signals; human clarification supplies business strategy where absent | Domain discovery → Recovered (`Inferred`) with explicit assumptions and alternatives | **V1 — human context likely.** Source code usually cannot establish strategic importance by itself. |
| Bounded Context | Capability and vocabulary cohesion/conflicts; model meanings; rules; ownership; transactions; API/message contracts; security policy; dependencies; integration patterns; shared data | Composite graph over structural/WCF (M1/M2) and behavior, persistence, message, security, and coupling evidence (M3 clarification needed) | DDD interpretation may recover an enforced boundary (`Inferred`); DDD modeling may recommend one (`Proposed`) | **V1 — evidence gap.** Recovery and proposal must be separate findings; package boundaries are weak evidence only. |
| Context relationships / Context Map | Direction of calls/messages; contract ownership; producer/consumer behavior; dependency direction; translation layers; shared code/data; release/version evidence where available | Call/integration/message/persistence/dependency extraction and composite graph; WCF client/server/config evidence | DDD interpretation → recovered relationship (`Inferred`); DDD modeling → proposed relationship/pattern (`Proposed`) | **V1 — evidence gap.** Shared Kernel, Customer/Supplier, Conformist, Published Language, and Anti-Corruption Layer labels require behavior and ownership evidence, not names alone. |

Vocabulary conflicts are especially important boundary evidence. For example, observed uses of
`Account` may support different inferred meanings in identity, billing, and banking capabilities.
The identifiers remain Observed; the meanings and possible bounded-context boundary remain
Inferred. A model must retain competing meanings instead of forcing a single glossary entry.

## Tactical DDD

| Knowledge output | Minimum evidence needed | Deterministic source/analyzer or stage | Semantic reasoning stage | Horizon and sufficiency |
|---|---|---|---|---|
| Aggregate | Identity and object graph; mutation entry points; invariant enforcement; persistence and lifecycle; transaction/consistency behavior; external references | Structural evidence (M1); method-body mutation/call analysis and persistence/transaction extraction (M3 clarification needed) | DDD interpretation → recovered aggregate behavior (`Inferred`); DDD modeling → candidate design (`Proposed`) | **V1 — evidence gap.** Type containment or an `AggregateRoot` name is insufficient. |
| Aggregate Root | Stable identity; externally reachable mutation boundary; creation/loading/saving paths; invariant coordination; references from outside the candidate; transaction scope | Structural plus caller/callee, mutation, persistence, and transaction evidence | DDD interpretation or DDD modeling, preserving Recovered versus Proposed | **V1 — evidence gap.** A repository type parameter or naming convention is supporting evidence only. |
| Entity | Identity fields and equality; lifecycle/state mutation; persistence mapping; references over time; creation and deletion behavior | Structural evidence; equality/identity, mutation, and persistence extraction | DDD interpretation → Recovered (`Inferred`) or DDD modeling → Proposed (`Proposed`) | **V1 — evidence gap; open artifact dependency.** Mapping sources may be outside C# or absent. |
| Value Object | Lack of independent identity; immutable or replacement-style use; construction validation; structural equality; serialization and persistence shape; usage within other concepts | Structural evidence; constructor/equality/mutation/data-flow/persistence analysis | DDD interpretation → Recovered (`Inferred`) or DDD modeling → Proposed (`Proposed`) | **V1 — evidence gap.** `record`, `struct`, or `ValueObject` naming alone does not prove semantics. |
| Domain Service | Domain operation spanning concepts; domain rules; state dependencies; absence of entity-like lifecycle; callers and side effects | Call/behavior/mutation/side-effect and persistence evidence | DDD interpretation → Recovered (`Inferred`) or DDD modeling → Proposed (`Proposed`) | **V1 — evidence gap.** A class ending in `Service` may instead be an application or infrastructure service. |
| Repository | Query/write operations; aggregate/entity type usage; mapping/store access; unit-of-work or transaction participation; callers | Structural interfaces (M1); body, persistence, and transaction extraction (M3 clarification needed) | DDD interpretation → Recovered (`Inferred`) or DDD modeling → Proposed (`Proposed`) | **V1 — partial path plus evidence gap.** A `Repository` suffix is not sufficient; support depends on persistence artifacts. |
| Factory | Object creation paths; construction complexity; validation/invariants; dependency resolution; callers; whether creation is domain-significant | Constructor/object-creation, call, behavior, and data-flow extraction | DDD interpretation → Recovered (`Inferred`) or DDD modeling → Proposed (`Proposed`) | **V1 — evidence gap.** A method named `Create` is not proof of a DDD Factory. |
| Command | Imperative request shape; initiating operation/message; validation; target behavior; mutation; handler/dispatcher relationship; outcome | WCF contract/message evidence (M2); dispatch, call, validation, and mutation extraction (M3 clarification needed) | DDD interpretation → recovered command semantics (`Inferred`) or DDD modeling → Proposed (`Proposed`) | **V1 — evidence gap.** DTO or method naming alone does not establish a Command. |
| Domain Event | Past-tense domain occurrence; publication; subscribers; payload; causal mutation; transaction timing; retry/delivery behavior | Message/publication/subscription, call, mutation, and transaction extraction | DDD interpretation → recovered event (`Inferred`) or DDD modeling → Proposed (`Proposed`) | **V1 — evidence gap; open artifact dependency.** A .NET event or `*Event` class is not automatically a Domain Event. |
| Handler | Registration/dispatch; consumed command/event/message; invoked behavior; side effects; retry/error semantics; transaction scope | Framework registration/config plus dispatch/call/message/body analysis | DDD interpretation → Recovered (`Inferred`) or DDD modeling → Proposed (`Proposed`) | **V1 — evidence gap.** Handler registration and behavioral extraction are not named in the current milestones. |

## Business behavior

| Knowledge output | Minimum evidence needed | Deterministic source/analyzer or stage | Semantic reasoning stage | Horizon and sufficiency |
|---|---|---|---|---|
| Business Rule | Conditions, comparisons, constants, calculations, branches, decisions, exceptions/faults, affected state/data, and observable outcomes | Method-body control/data-flow, constant/configuration, exception, and call extraction (M3 clarification needed) | Domain discovery → Recovered (`Inferred`) | **V1 — evidence gap.** The roadmap does not explicitly schedule rule-condition extraction. |
| Invariant | Rule enforced across mutation paths; guarded state; invalid-state handling; creation/update/delete coverage; transaction/consistency scope; counterexamples | Mutation/control-flow, validation/exception, persistence, and transaction evidence | Domain discovery → Recovered (`Inferred`); DDD modeling may relate it to a proposed aggregate (`Proposed`) | **V1 — evidence gap.** One guard on one path cannot prove universal enforcement. |
| Policy | Choice among rules/strategies; inputs; decision tables or configuration; authorization or domain constraints; call sites and outcomes | Control/data-flow, configuration, dependency, security, and behavior extraction | Domain discovery → Recovered (`Inferred`) | **V1 — evidence gap.** Policy meaning must not be inferred from a `Policy` type name alone. |
| Workflow / Business Process | Trigger; ordered and branching call chains; participants; state transitions; reads/writes; messages; external calls; compensations and failure paths | WCF entry points (M2); interprocedural call/control-flow, state, message, side-effect, and exception extraction (M3 clarification needed) | Domain discovery → Recovered (`Inferred`) | **V1 — evidence gap.** A static dependency graph alone does not establish ordering or business process. |
| State Transition / Lifecycle | State representation; before/after assignments; guards; triggering operation/message; invalid transitions; persistence and transaction context | State-write/data-flow/control-flow plus persistence and call extraction | Domain discovery → Recovered (`Inferred`) | **V1 — evidence gap.** Enum declarations do not establish a state machine. |
| Decision | Branch inputs, condition, alternatives, selected outcomes, rule source, and affected behavior/data | Control/data-flow and constant/configuration extraction | Domain discovery → Recovered (`Inferred`) | **V1 — evidence gap.** Required by the DKM but not explicit in the current milestone evidence list. |
| Preconditions / Postconditions | Entry guards; input and prior-state checks; success/failure exits; resulting state, outputs, messages, and faults | Control/data-flow, validation, state, side-effect, and exception extraction | Domain discovery → Recovered (`Inferred`) | **V1 — evidence gap.** Signature types alone do not establish behavioral contracts. |
| Validation Rule | Input/state checks; validation APIs/attributes; branches; error codes/messages/faults; affected operation and bypass paths | Attribute/config plus method-body control/data-flow and exception extraction | Domain discovery → Recovered (`Inferred`) | **V1 — evidence gap.** Declarative attributes provide only partial coverage without imperative validation paths. |

## Application and integration

| Knowledge output | Minimum evidence needed | Deterministic source/analyzer or stage | Semantic reasoning stage | Horizon and sufficiency |
|---|---|---|---|---|
| Application Service | Entry-point orchestration; calls to domain/persistence/integration services; transaction demarcation; mapping; lack or presence of domain rules | WCF analyzer (M2); call, behavior, persistence, transaction, and side-effect extraction | Domain discovery / DDD interpretation → Recovered (`Inferred`) | **V1 — evidence gap.** `*Service` and WCF implementation names do not distinguish application from domain services. |
| APIs / Operations | Service/interface declaration; `ServiceContract`, `OperationContract`, `FaultContract`; implementation link; signature; endpoint/binding/hosting configuration; accessibility | WCF source/config/hosting analyzer (M2), with structural provenance from M1 | Exact declarations remain Observed in the Evidence Graph; domain purpose and use-case relation are Recovered (`Inferred`) | **V1 — path identified.** M2 must still cover implementation/config correlation and explicit partial/unresolved cases. |
| DTOs / Contracts | `DataContract`, `DataMember`, `MessageContract`, message headers/body, serialization attributes, types, versions, operation usage | WCF contract and serialization analyzer (M2); structural/type evidence (M1) | Contract role and domain meaning → Recovered (`Inferred`) | **V1 — path identified.** Generated and external schema sources may remain partial/open. |
| Messages / Integration Events | Message shape; producer/consumer; transport/configuration; send/receive site; correlation; delivery, ordering, retry and transaction behavior where visible | WCF message evidence (M2); messaging/call/config/transaction extraction (M3 clarification needed) | Domain discovery → Recovered (`Inferred`); classification as a Domain Event requires separate support | **V1 — evidence gap; open artifact dependency.** Message contracts alone do not establish business event semantics. |
| External Systems | Endpoint addresses/types without exposing secrets; client/proxy/channel creation; outbound calls; assembly/service dependencies; request/response or message relationships | WCF clients, `ChannelFactory<T>`, `ClientBase<T>`, endpoint/config and call extraction; dependency analyzer | Domain discovery → Recovered (`Inferred`) | **V1 — partial path plus evidence gap.** Dynamic endpoints, generated proxies, transforms, and third-party assemblies may limit coverage. |
| Synchronous / Asynchronous Dependencies | Invocation or send relationship; waiting/return behavior; callback/task/one-way/message pattern; endpoint/binding; failure/retry behavior | Call and message analyzer; WCF operation/config evidence; control-flow and exception extraction | Domain discovery → Recovered (`Inferred`) | **V1 — evidence gap.** Static references do not establish interaction mode. |

## Security of the analyzed system

These outputs describe the analyzed repository. They are not the access controls that protect the
DomainLens product itself. Repository content remains untrusted, and secret values must not be
copied into evidence, model context, logs, or results when a safe redacted observation is enough.

| Knowledge output | Minimum evidence needed | Deterministic source/analyzer or stage | Semantic reasoning stage | Horizon and sufficiency |
|---|---|---|---|---|
| Authentication | WCF binding/transport/message security settings; service credentials; certificate/Windows/custom authentication configuration; authentication middleware/attributes and principal establishment visible in source | WCF configuration/security analyzer (M2 where framework-specific); security-sensitive code/config extraction (M3 clarification needed) | Domain discovery → Recovered (`Inferred`) | **V1 — evidence gap.** WCF discovery does not currently promise security-semantics extraction. Absence in repository is not proof of no authentication. |
| Authorization | Role/claim/principal checks; declarative attributes; operation/service authorization configuration; custom authorization managers; denial/fault paths | Security control-flow and framework/config analyzer (M3 clarification needed) | Domain discovery → Recovered (`Inferred`) | **V1 — evidence gap.** Security checks must be tied to protected operations and bypass paths. |
| Roles / Permissions | Role/permission declarations and constants; membership checks; operation/resource association; configuration and custom policy usage | Security symbol/config/control-flow extraction | Domain discovery → Recovered (`Inferred`) | **V1 — evidence gap.** A role-name string does not establish an effective permission. |
| Security Policy | Correlated authentication, authorization, transport protection, sensitive-data handling, enforcement paths, exceptions and configuration | Composite security evidence over WCF/config and method bodies | Domain discovery → Recovered (`Inferred`) with contradictions and coverage limitations | **V1 — evidence gap.** The current Milestone 3 label omits security evidence entirely. |
| Sensitive-data handling rule | Data classifications suggested by types/attributes/names; serialization; persistence; logging; masking/encryption calls; access checks; transport configuration; flows to external systems | Taint/data-flow, serialization, persistence, logging, security, and config extraction with redaction | Domain discovery → Recovered (`Inferred`) | **V1 — evidence gap; human context likely.** Names are weak signals and secret values must not be retained. |

## Data, consistency, and cross-cutting architecture knowledge

| Knowledge output | Minimum evidence needed | Deterministic source/analyzer or stage | Semantic reasoning stage | Horizon and sufficiency |
|---|---|---|---|---|
| Data Ownership | Authoritative writes; read-only consumers; creation/deletion; repository/mapping/table access; lifecycle control; transaction boundaries; shared writers | Persistence/data-access, mutation, call, and transaction extraction (M3 clarification needed) | Domain discovery → Recovered (`Inferred`) | **V1 — evidence gap; open artifact dependency.** Access is not ownership, and missing SQL/mappings can prevent a conclusion. |
| Persistence | Data-access calls; repositories; ORM/mapping metadata; SQL/stored-procedure invocation; connection/store identity safely normalized; read/write kind; procedure-body facts only if a supporting analyzer exists | Relationship / persistence / behavioral evidence (M3); artifact-specific analyzers subject to the V1 coverage contract; stored-procedure body analysis is Deferred in the current matrix | Exact access facts → Observed; architectural meaning → Recovered (`Inferred`) | **V1 — path identified but underspecified; open artifact dependency.** Persistence extraction needs an explicit artifact and resolution contract. |
| Transaction Boundary | `TransactionScope`, connection/ORM transactions, unit-of-work patterns, ambient transactions, commit/rollback, operation attributes/config, calls and messages inside scope | Transaction, call/control-flow, WCF, and persistence extraction (M3 clarification needed) | Domain discovery → Recovered (`Inferred`) | **V1 — evidence gap.** Persistence access alone does not establish a transaction boundary. |
| Consistency Boundary | Invariants and related mutations; transaction scope; aggregate write set; synchronous versus message-mediated updates; rollback/compensation and concurrency behavior | Composite behavior, mutation, transaction, persistence, and message evidence | Domain discovery → Recovered (`Inferred`); DDD modeling may propose an aggregate/context boundary (`Proposed`) | **V1 — evidence gap.** Consistency boundaries cannot be read from class containment. |
| Shared Data | Multiple components/services reading or writing the same table/store/file/schema; mappings and connection identities; shared write ownership | Persistence/mapping/config/call extraction and cross-component reconciliation | Domain discovery → Recovered (`Inferred`) and later decomposition input | **V1 — path identified but underspecified; open artifact dependency.** Store-name equality and aliases require conservative resolution. |
| Dependencies and Coupling | Resolved/unresolved type, project, call, data, message, transaction, configuration and external-service dependencies; fan-in/fan-out as derived measurements | Structural dependencies (M1); WCF (M2); call/persistence/message/security/transaction extraction (M3 clarification needed) | Domain discovery → Recovered (`Inferred`); decomposition remains a later stage | **V1 — partial current path plus evidence gap.** Structural coupling alone misses behavioral and runtime coupling. |
| Calls and Side Effects | Caller/callee resolution; reads/writes; external I/O; sends/publishes; logging/audit; mutation; exceptions and order/condition where supported | Interprocedural call, control/data-flow, mutation, persistence, messaging, and external-call extraction | Exact supported relationships → Observed; business meaning → Recovered (`Inferred`) | **V1 — evidence gap.** Scanner 0.1 explicitly has no method-body or call-graph analysis. |
| Configuration-driven behavior | Configuration keys/sections; safe values or redacted presence; transforms; consumers; branch or framework behavior affected; environment-specific ambiguity | XML/config/framework analyzer plus configuration-to-code usage analysis; transforms subject to coverage decision | Domain discovery → Recovered (`Inferred`) | **V1 — evidence gap; open artifact dependency.** Reading `.config` as XML is not enough to establish its behavioral effect. |
| Scheduled Processes | Timer/scheduler/service/job declarations; triggers and cadence without leaking secrets; entry methods; calls, data changes, messages, retries and overlap behavior | Framework/config trigger detection plus call/behavior/persistence/message extraction | Domain discovery → Recovered (`Inferred`) | **V1 Partial artifact path plus evidence gap.** The coverage matrix assigns supported patterns to the Relationship / persistence / behavioral evidence capability, but the roadmap does not state its trigger/behavior acceptance criteria. |
| Error / Exception Behavior | Throws/catches; WCF faults; error codes/results; retries; compensation; logging; operation and state context | Fault-contract evidence (M2); exception/control-flow/call/side-effect extraction (M3 clarification needed) | Domain discovery → Recovered (`Inferred`) | **V1 — evidence gap.** Declared faults are only part of actual failure behavior. |
| Audit Behavior / Requirements | Audit-write calls/interceptors; recorded actor/action/object/outcome fields; persistence/message targets; operation coverage; security/config conditions | Call, logging/audit framework, persistence, security, and configuration extraction | Domain discovery → Recovered (`Inferred`); a normative requirement needs human or repository-policy evidence | **V1 — evidence gap; human context likely.** Observed logging is not automatically a complete business audit requirement. |
| Concurrency / Locking | `lock`/monitor/synchronization primitives; optimistic concurrency/version columns; isolation levels; database locking hints when supported; retry/conflict paths; shared mutable state | C# body/control-flow, persistence/mapping/SQL, and transaction extraction | Domain discovery → Recovered (`Inferred`) | **V1 — evidence gap; open artifact dependency.** Cross-process and database behavior may be unavailable. |
| Caching | Cache API/config usage; keys; scope; TTL/eviction; read-through/write-through behavior; invalidation; affected data and consistency paths | Call/config/data-flow analysis with framework-specific cache recognition | Domain discovery → Recovered (`Inferred`) | **V1 — evidence gap.** A cache dependency does not establish effective caching or invalidation behavior. |
| Transaction-consistency characteristics | Isolation, commit/rollback, retry, idempotency, outbox/compensation, message timing, cross-store writes and failure behavior | Composite transaction, persistence, message, concurrency, exception, and control-flow evidence | Domain discovery → Recovered (`Inferred`) | **V1 — evidence gap; open artifact dependency.** This is broader than detecting transaction API calls. |

## Evidence sufficiency rules

The tables identify minimum evidence families, not mechanical proof rules. Finding validation must
apply the following cross-cutting constraints:

1. **Traceability:** every source-backed assertion cites valid Evidence IDs present in the sealed
   ContextPack, with source spans/hashes and analyzer/rule versions preserved.
2. **Support versus coverage:** a claim can have strong support from the inspected paths and still
   have low coverage because referenced assemblies, generated source, stored procedures, transforms,
   or dynamic dispatch were unavailable.
3. **Counterevidence:** alternative mutation paths, shared writers, bypassed validation, conflicting
   vocabulary, transaction gaps, and contradictory configuration are deliberately retrieved and
   retained.
4. **Resolution:** partial, ambiguous, and unresolved links remain visible and lower the usable
   evidence quality; they are never silently resolved by a model.
5. **No evidence of absence:** “not found within analyzed scope” is reported with its scope and
   limitations, never rewritten as “does not exist.”
6. **Source-model neutrality:** analyzer recipes seek behavior and relationships, not DDD marker
   names. Names can guide retrieval but cannot independently establish semantic findings.
7. **Atomic findings:** an as-is reconstruction and a design recommendation are separate findings.
   A proposed bounded context or aggregate never appears as recovered merely because it was
   accepted by a reviewer.
8. **Human input provenance:** strategic importance, external actors, organization ownership, and
   policies supplied by a reviewer remain identified as human clarification rather than source
   evidence.

## Roadmap gap analysis

Before this enhancement, the roadmap described Milestone 2 as **WCF discovery** and Milestone 3 as
**Relationship/persistence analysis**. This branch clarifies Milestone 3 as
**Relationship / persistence / behavioral evidence**, followed by Evidence
persistence/exploration, ContextPack construction, domain recovery, and DDD reasoning. The sequence
and broader title are directionally correct, but the milestone still needs explicit acceptance
criteria showing how approved V1 knowledge requirements receive their deterministic foundation.

In particular, the broadened title by itself does not yet commit acceptance criteria for:

- method bodies, interprocedural calls, control flow, data flow, and object/state mutations;
- rule conditions, calculations, constants, validation, pre/postconditions, and exceptions;
- workflow ordering, branches, state transitions, lifecycle, and compensation paths;
- transaction demarcation, consistency behavior, concurrency, and locking;
- authentication, authorization, roles/claims, WCF security configuration, and sensitive-data flows;
- message production/consumption, delivery semantics, external side effects, and integration timing;
- configuration-to-code effects, scheduled triggers, audit behavior, and cache invalidation; and
- explicit coverage/unsupported diagnostics for every participating artifact family.

Without these observations, Milestones 6 and 7 could only reason from structure, names, signatures,
and persistence references. That would be insufficient for the approved V1 outputs most sensitive
to behavior: business rules, invariants, workflows, state transitions, security policies, data
ownership, transaction/consistency boundaries, aggregate candidates, and bounded-context
candidates. It would also encourage exactly the name-based DDD inference that the architecture
prohibits.

This is an **evidence-capability and milestone-acceptance gap**, not authority to invent a new
milestone. The already approved V1 requirements justify the broader Milestone 3 wording now used by
the roadmap and require its eventual acceptance criteria to include security evidence as well. The
implementation slicing and analyzer ownership remain an open planning decision. These capabilities
must be available before semantic domain recovery consumes them.

### Recommended clarification criteria

Milestone 3 documentation should eventually identify, without freezing a physical design:

1. which V1 artifact categories supply each evidence family and which are Supported, Partial,
   Detection Only, Deferred, or Open Decision;
2. the supported resolution and coverage limits for calls, mutations, data access, transactions,
   security checks, messages, state changes, and side effects;
3. how source spans/hashes and analyzer/rule versions are retained for body-level and composite
   observations;
4. how counterevidence, unresolved dynamic behavior, generated/external code, and unavailable data
   artifacts are reported;
5. how WCF evidence from Milestone 2 is correlated with implementation behavior and configuration;
6. which evidence-gate or evaluation result must pass before Milestones 6 and 7 may claim support
   for each DKM output; and
7. that coverage and quality diagnostics flow into ContextPacks and findings but do not become
   evidence or model proof.

No arbitrary numeric threshold is established here. Per-output sufficiency gates, scoring rubrics,
and human-escalation thresholds remain open decisions for the quality and reasoning designs.

## Knowledge outputs with insufficient explicitly planned evidence

The following V1 output groups currently lack a sufficiently explicit deterministic plan:

| Gap group | Affected knowledge outputs | Missing or underspecified evidence capability |
|---|---|---|
| Behavioral semantics | Use Case, Business Rule, Invariant, Policy, Workflow, Decision, Preconditions/Postconditions, Validation Rule | Method-body control/data flow, mutation, call ordering, outcomes, and exception paths |
| Lifecycle and DDD boundaries | Aggregate, Aggregate Root, Entity, Value Object, Factory, State Transition, Consistency Boundary | Identity/lifecycle, mutation entry points, invariant enforcement, construction/equality, state writes, and transaction correlation |
| Service and message semantics | Domain Service, Application Service, Command, Domain Event, Handler, Messages, synchronous/asynchronous dependencies | Dispatch/call roles, publication/consumption, orchestration versus domain behavior, delivery and transaction timing |
| Security | Authentication, Authorization, Roles/Permissions, Security Policy, Sensitive-data handling | Framework/config security interpretation, principal/role/claim checks, protected-operation mapping, and security-sensitive data flow |
| Ownership and consistency | Data Ownership, Transaction Boundary, Shared Data, transaction-consistency characteristics | Read/write classification, authoritative-writer analysis, transaction scope, shared-store reconciliation, concurrency, and failure behavior |
| Strategic structure | Business Capability, Domain/Subdomain, Bounded Context, Context relationships | Composite behavior, vocabulary, ownership, transaction, security, integration, and coupling evidence |
| Vocabulary | Domain Vocabulary and context-specific meanings | Provenance-preserving term/usage extraction plus conflict and ambiguity retrieval; semantic definitions still require inference/review |
| Cross-cutting behavior | Calls/Side Effects, Configuration-driven behavior, Scheduled Processes, Error/Exception Behavior, Audit Behavior, Concurrency/Locking, Caching | Method-body/framework/config correlation and artifact-specific acceptance criteria not yet defined by the roadmap |

The strongest currently identifiable V1 path is for WCF APIs/operations and contracts through
Milestone 2, plus structural declarations and dependencies already supplied by Milestone 1. The
[V1 Analysis Coverage](01-v1-analysis-coverage.md) matrix now records intended treatments for
persistence, configuration, generated, scheduled, and external artifacts. Persistence and
shared-data goals also have a named Milestone 3 home, but read/write, transaction, behavioral, and
resolution acceptance criteria remain underspecified. All other groups above need roadmap
acceptance criteria or another approved mapping before their evidence supply can be considered
implementation ready.

## Open decisions

- Internal module and work-slicing boundaries within the canonical Relationship / persistence /
  behavioral evidence capability, including how behavior, security, messaging, transaction, and
  persistence extraction are composed within the Milestone 3 workstream.
- Detailed implementation and conformance rules for the artifact treatments established in
  [V1 Analysis Coverage](01-v1-analysis-coverage.md), including the still-open WSDL/XSD,
  framework/version, symbol-enrichment, and supported-pattern decisions.
- Supported levels of interprocedural control/data flow and the conservative resolution policy for
  virtual dispatch, dependency injection, reflection, configuration aliases, and generated code.
- The vocabulary term/meaning/conflict representation and the role of human clarification in
  definitions and strategic subdomain classification.
- Per-output minimum evidence recipes, completeness rules, support/confidence calibration,
  validation gates, and escalation criteria.
- Cross-analyzer reconciliation of equivalent, overlapping, and contradictory observations.
- Handling of runtime-only behavior that static analysis cannot establish and whether any future
  runtime evidence source is warranted.

These choices do not change the V1/Future boundary. Generalized technology discovery and generated
Analysis Plans remain FUTURE; the open work here is making the configured V1 .NET/WCF path capable
of supplying the evidence already required by Product V1.
