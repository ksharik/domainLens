# DomainLens Quality and Evaluation Strategy

## Status and purpose

This document defines the **PLANNED — Product V1** quality model for the
configured legacy C#/.NET Framework/WCF analysis path. It is a design and
evaluation contract, not a claim that Scanner 0.1 already implements semantic
or hosted operational measurement.

The strategy evaluates three related but separate systems:

1. deterministic analyzers that establish Evidence Graph observations;
2. semantic reasoning that creates `Inferred` or `Proposed` findings from a
   sealed ContextPack; and
3. the pipeline that executes, validates, persists, and presents those results.

It preserves the approved knowledge flow:

`Source Code → Evidence Graph → Semantic Findings → Domain Knowledge Model { Recovered Domain Knowledge + Proposed DDD Design } → Decomposition Analysis → Modernization Model → Target Architecture`

Quality evaluation does not collapse these stages. Decomposition and
modernization quality are outside Product V1's Reverse DDD evaluation scope.

## Quality principles

- Repository content is untrusted evaluation input, not an instruction source.
- Deterministic application code alone creates `Observed` evidence.
- Models may create only `Inferred` or `Proposed` findings and may reference only typed inputs
  supplied in their sealed ContextPack. Source-backed assertions cite deterministic Evidence IDs;
  human-supplied statements cite Human Context revisions; prior findings remain finding references.
- Generated prose, quality dashboards, telemetry, and benchmark scores are
  views over canonical records; none is the canonical result.
- Metrics report quality characteristics. They do not establish facts about
  the analyzed business or implementation.
- Evaluation must reward correct uncertainty, abstention, alternatives, and
  limitations rather than reward confident overstatement.
- The absence of a finding means **not established within the declared analyzed
  scope**, never **does not exist**.
- Source naming is neither necessary nor sufficient for a DDD conclusion.
- Human acceptance changes review state, not epistemic classification or
  semantic view.
- Human Context is a versioned provenance record, not Evidence Graph evidence, model
  interpretation, review state, or another epistemic classification. Its contribution to Support
  must be explicit under a versioned rubric rather than silently combined with source evidence.

## Five distinct analysis concepts

Each concept has its own subject and provenance. A product view must not roll
them into an unexplained single score.

| Concept | Formal subject | Meaning | Must not be interpreted as |
|---|---|---|---|
| **Coverage** | A declared analysis scope and a versioned analyzer/retrieval recipe | How much of the relevant, enumerable source, artifact, construct, or evidence-candidate space DomainLens actually examined successfully. A quantitative value is valid only when its numerator, denominator, exclusions, unknowns, and scope are stated. | Strength of a semantic claim, completeness of the repository, or proof that an omitted concept does not exist. |
| **Support** | One atomic semantic finding revision | How strongly the evidence supplied for that claim favors it after relevant counterevidence, resolution quality, evidence diversity, and directness are considered. | A repository-wide coverage statement or model self-confidence. |
| **Confidence** | One finding revision, only after the Confidence availability gate is satisfied | A calibrated assessment combining support, contradictions, evidence quality, applicable coverage, known uncertainty, and evaluation history under an applicable versioned method. Until the gate is satisfied, Confidence is unavailable—not calibrated—and is omitted or shown explicitly as unavailable according to the eventual schema. | Proof, Observed evidence, human acceptance, raw model self-report, renamed Support, or a substitute for showing evidence and limitations. |
| **Completeness** | One requested analysis dimension within declared scope | How fully DomainLens believes it answered the requested question, considering expected subdimensions, analyzed scope, known unsupported areas, unresolved items, and missing inputs. | Global completeness of the system or a claim that undiscovered behavior is absent. |
| **Resolution Quality** | One deterministic source observation or relationship | How certainly an analyzer resolved that source relationship using the established vocabulary `Exact`, `Partial`, `Ambiguous`, or `Unresolved`. | Semantic support, business meaning, or whole-repository coverage. |

The scales and scoring formulas for support, Confidence, coverage, and
completeness are **OPEN DECISIONS**. Confidence is not currently an available
field or product signal. Any chosen rubric must be versioned and must retain
the underlying facts from which its presentation value is derived. Resolution
Quality already has an approved four-value vocabulary in the
[Evidence Architecture](../architecture/05-evidence-architecture.md).

### Coverage is scoped and multidimensional

At minimum, V1 should be able to report separate coverage statements for:

- repository artifacts inventoried, excluded, unreadable, oversized, or
  unsupported;
- applicable artifact kinds reached by an analyzer;
- supported constructs successfully parsed or analyzed;
- deterministic relationships resolved, partially resolved, ambiguous, or
  unresolved;
- analysis dimensions for which evidence was sought, such as behavior,
  persistence, security, workflow, and integration; and
- evidence selected, summarized, or pruned when constructing a ContextPack.

A percentage is appropriate only for an enumerable denominator. Where the
relevant universe cannot be known, DomainLens must report counts, categories,
limitations, and `Unknown` rather than manufacture a percentage. No unweighted
repository-wide “coverage score” is approved.

### Support is claim-local

Support is assessed against an atomic claim, not an entire generated report.
Its explanation should identify direct and indirect supporting evidence,
counterevidence, evidence independence, Resolution Quality, assumptions, and
coverage relevant to the claim. Repetition of the same fact across generated
files or call paths must not be treated automatically as independent support.

Illustrative labels such as `Strong`, `Moderate`, `Weak`, or `Unsupported` may
be useful in product research, but the final vocabulary and mapping rules are
not yet approved.

### Confidence requires calibration

Confidence is a product interpretation of support and uncertainty. It must be
treated as unavailable—not calibrated—and omitted or displayed explicitly as
unavailable until all of these conditions are satisfied:

1. an applicable versioned calibration method exists;
2. an applicable expert-reviewed corpus exists;
3. calibration has been evaluated on that corpus; and
4. a product decision explicitly approves exposing Confidence.

After that gate is satisfied, Confidence must be calibrated by finding type
and, where necessary, by analyzer or fixture category. A model's stated
confidence is not proof and must not override contradictory evidence, poor
Resolution Quality, or low coverage. DomainLens must not manufacture
pseudo-confidence, relabel Support as Confidence, or use an uncalibrated label
as a substitute.

### Completeness is question-relative

Completeness applies to a requested analysis dimension. For example, workflow
analysis could be incomplete because only WCF entry operations were examined,
scheduled jobs were not supported, and a referenced assembly was unavailable,
even if every supported WCF contract was found. The exact completeness status
vocabulary remains an open decision.

### Resolution Quality is deterministic and local

- `Exact` means the supported deterministic rule resolved the observation
  within its stated scope.
- `Partial` means the observation is useful but known selection or coverage is
  incomplete.
- `Ambiguous` means more than one supported target remains possible.
- `Unresolved` means the declaration or relationship was observed but no
  supported target could be established.

`Exact` does not mean behaviorally complete or semantically correct. An exact
method declaration does not prove a business rule, aggregate, or bounded
context.

### Worked example

The following is illustrative, not an approved score threshold:

| Field | Example value |
|---|---|
| Finding | `Customer` appears to act as an Aggregate Root. |
| Semantic view / classification | Recovered Domain Knowledge / `Inferred` |
| Support | Strong under the named draft rubric because mutation, repository, lifecycle, and transaction observations converge. |
| Coverage | 72% of the explicitly declared aggregate-evidence search units were analyzed; the numerator and denominator travel with the value. |
| Confidence | Unavailable—not calibrated (or omitted under the eventual schema) until the four-part Confidence availability gate is satisfied. |
| Counterevidence | None found **within the analyzed scope**. |
| Known limitations | Referenced assembly unavailable; stored procedures absent from the repository; dynamic dispatch unresolved. |
| Alternative interpretation | `CustomerService` may own a transaction-script boundary rather than the implementation enforcing a domain aggregate. |

High support plus low coverage means the inspected evidence strongly favors a
claim, but important unseen evidence could change it. Medium support plus high
coverage means DomainLens inspected most of the declared scope but the evidence
remains mixed or indirect. These states require different explanations and
review behavior even if a future composite display happened to render them
similarly.

## Deterministic analysis quality

Deterministic evaluation asks whether the configured analyzer path observed
supported repository content accurately, reproducibly, and with complete
provenance.

| Quality area | What to measure or inspect | Required interpretation |
|---|---|---|
| Artifact coverage | Included, excluded, ignored, unreadable, oversized, unsupported, and failed artifacts by declared type and reason. | Reports what was processed; it does not prove the repository contains all production artifacts. |
| Parser/analyzer coverage | Applicable constructs encountered, fully handled, degraded, unsupported, and failed by analyzer/rule/version. | Denominators must be tied to a declared supported grammar or fixture oracle. |
| Relationship resolution | Counts and distributions of `Exact`, `Partial`, `Ambiguous`, and `Unresolved`, sliced by edge kind and analyzer. | Resolution quality describes deterministic linking, not business meaning. |
| Provenance completeness | Presence and validity of snapshot, relative path, content hash, source span where applicable, extractor/rule/version, and resolution. | Synthetic records must be explicitly identifiable; they need not invent a source span. |
| Graph integrity | Identity derivation, uniqueness, valid references, endpoint rules, schema compatibility, and canonical-hash validation. | An internally valid graph can still contain an analyzer defect; corpus oracles test correctness. |
| Determinism/reproducibility | Canonical output equality for the same captured inputs, configuration, analyzer/rule versions, and supported environment. | Environment and version lineage must be held constant before equivalence is claimed. |
| Unsupported-artifact reporting | Stable diagnostics for unsupported or degraded artifacts and constructs. | Silent omission is a quality failure when the artifact was within inventory scope. |
| Source locator correctness | Hashes match captured bytes and spans identify the asserted construct under the declared offset/line convention. | Internal graph validation and fixture-based source-locator verification are distinct checks. |

### Deterministic correctness gates

The following are non-negotiable invariants rather than statistical release
thresholds:

- only deterministic analyzer application code may add or alter Observed
  Evidence Graph records;
- every source-backed observation must reference the identified snapshot and
  captured artifact hash and carry valid analyzer/rule provenance and
  Resolution Quality;
- all IDs and graph references must satisfy the versioned schema and graph
  invariants before the artifact is accepted;
- source spans, when asserted, must use the declared coordinate convention and
  fixture evaluation must verify that they locate the claimed source bytes;
- unsupported, excluded, degraded, ambiguous, and unresolved inputs must be
  surfaced through structured status or diagnostics rather than silently
  upgraded or omitted;
- equivalent runs may be called deterministic only when snapshot, input bytes,
  configuration, analyzer/rule versions, and relevant environment are the same;
  and
- repository content must remain inert throughout Product V1: analysis must not
  restore, build, load, execute, or obey code or instructions from the analyzed
  repository. Any future exception requires a separately approved security and
  isolation design outside the V1 analyzer path.

A gate failure rejects the affected artifact or stage, or produces an explicit
partial/failure outcome according to policy. It must not be hidden by an
aggregate score.

## Semantic analysis quality

Semantic evaluation asks whether each finding is supported, correctly scoped,
honest about uncertainty, and useful to a domain or architecture reviewer.

### Core measures

- **Evidence support:** expert assessment of whether cited evidence directly or
  indirectly supports the exact atomic claim and whether the support label is
  appropriate under the versioned rubric.
- **Unsupported-claim rate:** proportion of evaluated claims that lack adequate
  support for their wording or scope. Report by finding type and semantic view.
- **Fabricated-evidence-reference rate:** proportion of evidence citations that
  do not resolve to an Evidence ID supplied in the sealed ContextPack. Accepted
  findings must contain no such references.
- **Invalid-Human-Context-reference rate:** proportion of Human Context citations that do not
  resolve to the exact typed revision supplied in the sealed ContextPack, or that are presented as
  source evidence. Accepted findings must contain no such references.
- **Counterevidence handling:** whether expected contradictory evidence was
  retrieved, preserved, weighed, and shown rather than omitted or rationalized
  away.
- **Alternative-interpretation handling:** whether materially plausible
  interpretations in the expert expectation were retained or a justified
  reason for excluding them was supplied.
- **Domain-concept accuracy:** agreement with expert-reviewed expected business
  concepts and relationships, allowing explicitly documented alternatives.
- **Aggregate-boundary accuracy:** agreement on candidate root, membership,
  lifecycle, mutation, transaction, invariant, and consistency evidence.
- **Bounded-context accuracy:** agreement on candidate semantic boundaries and
  relationships based on capabilities, vocabulary, ownership, transactions,
  integrations, and coupling rather than code layout alone.
- **Business-rule recovery accuracy:** precision and recall of expected rules,
  conditions, decisions, validations, and invariants only where the corpus
  supplies an enumerable expert oracle.
- **Workflow/state-model accuracy:** agreement on actors, entry points, ordered
  steps, transitions, guards, side effects, persistence, and messages.
- **Security-policy recovery accuracy:** agreement on observable or reasonably
  inferable authentication, authorization, roles/permissions, constraints, and
  sensitive-data rules, including limitations for out-of-repository policy.
- **Data-ownership interpretation accuracy:** agreement on ownership versus
  consumption, write authority, shared data, transaction behavior, and
  alternative ownership interpretations.
- **Human-review agreement:** agreement with independent expert review and the
  nature of disagreements. Human agreement is an evaluation signal, not a
  mechanism for changing classification.

Accuracy measures must distinguish false positive claims, missed expected
claims, acceptable alternatives, and deliberately unresolved cases. Exact
string matching of generated prose is not a valid semantic-quality method.

### Semantic correctness gates

- A model-created finding must never be classified as `Observed` or mutate the
  Evidence Graph.
- Every cited Evidence ID must exist in the finding's sealed ContextPack and
  resolve to the identified snapshot.
- Every cited Human Context ID/revision must exist as that record type in the sealed ContextPack;
  it must remain distinct from Evidence Graph records, model interpretation, and review state.
- A semantic finding must not cite source material, relationships, or evidence
  outside that ContextPack as though the model inspected it.
- `RecoveredDomainKnowledge` model claims must be `Inferred`;
  `ProposedDddDesign` claims must be `Proposed`.
- Proposed DDD Design must never be displayed or persisted as recovered/as-is
  knowledge, including after human acceptance.
- Findings must preserve material counterevidence, assumptions, limitations,
  unresolved questions, and materially plausible alternatives required by the
  reasoning contract.
- A missing or unsupported finding must not be restated as proof that the
  business concept, rule, policy, relationship, or behavior does not exist.
- Structured output and provenance validation must succeed before a candidate
  becomes an accepted Finding Graph revision or is eligible for deterministic
  DKM projection.
- Human review changes review state or creates a revision; it must not relabel
  `Inferred` as `Observed`, relabel `Proposed`, or move a proposal into the
  recovered view.
- Creating, correcting, or superseding Human Context must preserve prior revisions and the exact
  ContextPack/finding/DKM lineage that used them; it is not itself a review-state transition.

These are acceptance invariants. Measures such as concept accuracy or human
agreement require empirically selected release thresholds, which remain open.

## Operational quality

Operational evaluation asks whether DomainLens completes the declared analysis
reliably, transparently, safely, and at an understandable resource cost.

| Measure | Required breakdown or context |
|---|---|
| Analysis completion rate | Terminal success by repository/fixture category, configured analyzer profile, application version, and run period. |
| Partial-analysis rate | Explicit partial outcomes by limitation and affected analysis dimension; partial must not be counted as full completion. |
| Failure categories | Typed intake, policy, analyzer, resource, persistence, orchestration, model-provider, schema/validation, cancellation, and security-significant outcomes. |
| Stage duration | Queue and execution time separately for each durable stage; human wait time separately from compute time. |
| Model invocation count | By reasoning task, skill/prompt/model version, initial call, repair, and retry. |
| Token and cost visibility | Input/output tokens and estimated cost with provider/version, run, stage, and policy context where available. |
| Retry/repair frequency | By initiating typed failure, attempt, outcome, and whether the repair used the same sealed evidence envelope. |
| Human-escalation frequency | By finding type, reason, consequence, ambiguity, missing coverage, and resolution outcome. |

Operational correctness requires durable pipeline state rather than inference
from logs, preserved attempt and version lineage, idempotency boundaries for
equivalent retries, and explicit partial/failure outcomes. Telemetry must obey
redaction, access, retention, and untrusted-input controls. An operational
metric or successful run is not evidence about the analyzed application and
cannot repair or strengthen a semantic finding.

No completion, latency, cost, retry, or escalation target has been approved.
Targets must be selected only after representative V1 measurements and product
expectations are available.

## Metrics are not evidence

| Record | Describes | May support a semantic finding about the analyzed system? |
|---|---|---|
| Evidence Graph record | A deterministic observation from an identified snapshot and analyzer rule. | Yes, when included in the sealed ContextPack and relevant to the claim. |
| Human Context record | An attributable, versioned human-supplied domain statement, distinct from source evidence and review state. | It may inform a finding when the exact revision is included in the sealed ContextPack and cited separately. Whether and how it affects Support remains a versioned-rubric decision; it never becomes Observed evidence. |
| Finding / DKM record | An evidence-linked semantic interpretation or proposal. | It may be prior reviewed context, but it does not become Observed evidence. |
| Coverage/completeness measure | What DomainLens examined or answered under a declared recipe and scope. | No. It qualifies uncertainty and limitations. |
| Evaluation score | How output compared with a versioned benchmark expectation. | No. It evaluates DomainLens; it is not a fact about the subject repository. |
| Operational metric, log, or trace | How DomainLens executed. | No, unless a separate deterministic analyzer explicitly captures an appropriate subject-system artifact as evidence. |

Consequently, “all supported files analyzed” cannot justify “no authorization
policy exists,” and a high benchmark score cannot justify an uncited finding.

## Evaluation corpus and benchmark strategy

The V1 corpus should combine purpose-built fixtures with legally usable,
version-pinned representative repositories. It must be safe to analyze and
must not require executing repository-controlled build logic. The corpus is a
planned evaluation asset; this documentation task does not create it.

### Required repository categories

| Category | Fixture/repository shape | Primary evaluation pressure |
|---|---|---|
| **A** | Clean DDD-style WCF application | Recover genuinely enforced patterns without treating DDD names as proof; distinguish recovered patterns from new proposals. |
| **B** | Traditional layered/N-tier WCF application | Reconstruct domain knowledge across service, business, and data layers without equating layers, projects, or namespaces with bounded contexts. |
| **C** | Anemic domain model | Find behavior outside entities and avoid proposing entity methods as recovered behavior. |
| **D** | Transaction-script application | Recover rules, workflows, transactions, and data ownership without requiring domain objects. |
| **E** | Highly coupled legacy monolith | Represent cross-cutting calls, shared data, contradictory boundaries, and weakly supported alternatives rather than forcing clean contexts. |
| **F** | Misleading DDD vocabulary, including types named `AggregateRoot`, `Entity`, or similar without matching behavior | Detect declarations as Observed while rejecting name-only semantic conclusions. |
| **G** | No DDD terminology | Demonstrate source-model-neutral recovery from behavior, rules, data, operations, security, and coupling. |
| **H** | Partial or incomplete repository | Report missing inputs and reduced coverage/completeness; do not convert “not found” into absence. |
| **I** | Business rules concentrated in the service layer | Recover conditions, validation, exceptions, state changes, and orchestration from their actual location. |
| **J** | Business rules or persistence logic in repository SQL/stored-procedure artifacts | If the approved V1 artifact matrix supports the relevant artifacts, evaluate their evidence. If support is partial, detection-only, deferred, or unresolved, expect explicit limitations and prohibit invented rule recovery. |

Each category should contain small diagnostic fixtures for rule-level tests and
larger integration fixtures for cross-artifact behavior. Benchmarks must record
snapshot identity, fixture version, supported analyzer profile, analyzer/rule
versions, ContextPack recipe, skill/prompt/model versions, and evaluation-rubric
version so results remain comparable.

### Expert-reviewed expectations

For each fixture, reviewers should maintain structured expectations for:

- deterministic observations and provenance;
- WCF services, contracts, operations, endpoints, bindings, behaviors, and
  hosting declarations applicable to the fixture;
- source relationships and expected Resolution Quality;
- business rules and invariants;
- workflows, state transitions, and side effects;
- security constraints;
- data ownership, consumption, persistence, transaction, and consistency;
- business-capability candidates;
- aggregate and Aggregate Root candidates;
- bounded-context candidates and context relationships;
- known ambiguity, counterevidence, limitations, and acceptable alternative
  interpretations; and
- supplied Human Context cases, including exact provenance/revisions, expected separate citation,
  conflicts or supersession where applicable, and prohibited treatment as Evidence Graph facts.

Expectations should be atomic and machine-addressable where practical. They
should declare required observations/claims, prohibited overclaims, optional or
equivalent interpretations, and deliberately unresolved cases. Source experts
and domain/architecture reviewers should review the relevant layers. Reviewer
disagreement must be retained and reconciled explicitly rather than hidden in
one prose “gold answer.”

Semantic results are compared with these expert-reviewed expectations, not
with model self-confidence. Evaluation should inspect claim meaning,
classification, semantic view, cited support, counterevidence, assumptions,
alternatives, and limitations. Generated wording may vary without becoming a
failure when the structured meaning is equivalent.

### Evaluation runs and reporting

At minimum, evaluation reporting should provide:

1. deterministic rule-level conformance against enumerated fixture oracles;
2. end-to-end Evidence Graph regression for pinned snapshots and versions;
3. semantic results sliced by knowledge type and corpus category;
4. gate failures separately from statistical quality measures;
5. coverage, support, completeness, and Resolution Quality as separate fields,
   with Confidence present only when its availability gate is satisfied and
   otherwise omitted or explicitly marked unavailable according to the
   versioned schema;
6. correct abstentions and limitations alongside false positives and misses;
7. repeated-run variation for model-backed stages under the recorded provider
   and configuration; and
8. operational reliability, duration, retries, escalations, tokens, and cost.

A single blended benchmark score is not approved. Aggregation weights could
hide a severe correctness failure or poor performance on non-DDD systems.
Release decisions should retain per-category and per-knowledge-dimension views.

### Semantic-quality readiness gate for Milestones 6 and 7

Milestone 6 domain recovery and Milestone 7 recovered-DDD interpretation,
Proposed DDD Design, and validation are not ready for product acceptance merely
because the reasoning pipeline runs. Before their semantic outputs can be
accepted as product-ready, the applicable analysis scope must have:

- an applicable expert-reviewed corpus of fixtures or version-pinned representative repositories;
- structured expected results that preserve required findings, prohibited
  overclaims, acceptable alternatives, known ambiguity, and limitations;
- a versioned evaluation rubric appropriate to the finding types under review;
- an applicable versioned calibration method, an applicable expert-reviewed corpus, evaluated
  calibration results, and explicit product approval to expose Confidence if Confidence will be
  used; and
- approved semantic acceptance/regression thresholds wherever the product decision
  requires thresholds.

This is an acceptance gate for the existing milestones, not a new milestone.
It sets no numeric threshold. Until the necessary corpus, rubric, results, and
product decisions exist, results may support development and evaluation but
must not be represented as having passed semantic product readiness.

## Release and regression policy

Non-negotiable gate failures always remain visible and cannot be averaged away.
Statistical acceptance thresholds for precision, recall, agreement,
calibration, completion, performance, or cost are not defined here because no
representative baseline has been measured and no product risk tolerance has
been approved.

Once thresholds are approved, they should be versioned, justified by corpus
results and user risk, sliced for important fixture categories, and reviewed
when the corpus, analyzer scope, or reasoning contract changes. Tightening or
relaxing a threshold is a product decision, not an undocumented model prompt
change.

## Open decisions

- **OPEN DECISION — measurement schemas:** durable representation and
  versioning for coverage, support, confidence, completeness, evaluation
  outcomes, and their underlying inputs.
- **OPEN DECISION — coverage denominators:** which artifact, construct,
  evidence-candidate, retrieval, and knowledge-dimension denominators are both
  meaningful and deterministically enumerable.
- **OPEN DECISION — support and completeness rubrics:** allowed labels or
  scales, evidence weighting, independence rules, and treatment of partial,
  ambiguous, and unresolved evidence.
- **OPEN DECISION — confidence calibration:** method, calibration datasets,
  per-finding-type treatment, update cadence, and whether a user-facing
  confidence value is useful beyond support and limitations.
- **OPEN DECISION — acceptance thresholds:** semantic quality, human
  escalation, regression tolerance, operational objectives, performance, and
  cost limits.
- **OPEN DECISION — expert review protocol:** reviewer roles, minimum review,
  adjudication, inter-reviewer agreement, uncertainty labels, and versioning of
  expected results.
- **OPEN DECISION — corpus governance:** fixture ownership, licensing,
  sensitive-data policy, security review, repository pinning, maintenance, and
  whether benchmark materials may be public.
- **OPEN DECISION — artifact-dependent corpus:** inclusion and expected output
  for SQL, stored procedures, generated clients, third-party assemblies,
  dynamic dispatch, and other artifacts according to the approved V1 coverage
  matrix.
- **OPEN DECISION — model-run protocol:** provider/version pinning,
  nondeterminism, number of repeated runs, sampling parameters, outage
  handling, and cost control.
- **OPEN DECISION — regression policy:** which gates and metric changes block a
  release, require human review, or permit an explicitly documented exception.

## Related architecture

- [Analysis and Evidence Model](../05-analysis-model.md)
- [Evidence Architecture](../architecture/05-evidence-architecture.md)
- [Domain Knowledge Architecture](../architecture/06-domain-knowledge-architecture.md)
- [Agent and Reasoning Architecture](../architecture/07-agent-reasoning-architecture.md)
- [Observability and Operations](../architecture/12-observability-and-operations.md)
- [Security Architecture](../architecture/09-security-architecture.md)
- [Roadmap](../10-roadmap.md)
