# Quality and Evaluation

This section defines how DomainLens quality is measured without confusing
measurements with source evidence or model confidence with proof.

> **Code establishes evidence. AI interprets evidence. The agent orchestrates
> the process.**

## Status

- **CURRENT — Milestone 1:** deterministic Repository Structure Scanner tests
  cover selected Evidence Graph behavior, canonical output, diagnostics, and
  security boundaries. There is no semantic reasoning or hosted operational
  evaluation runtime.
- **PLANNED — Product V1:** a versioned quality model spanning deterministic
  analysis, semantic analysis, and end-to-end operations for the configured
  legacy C#/.NET Framework/WCF analyzer path.
- **FUTURE:** the same evaluation contracts may be extended to additional
  analyzer families. This does not move generalized automatic technology
  discovery or generated Analysis Plans into V1.

## Documents

- [Evaluation Strategy](01-evaluation-strategy.md) — quality dimensions,
  correctness gates, coverage and uncertainty semantics, evaluation corpus,
  and unresolved scoring decisions.

Related approved architecture:

- [Analysis and Evidence Model](../05-analysis-model.md)
- [Evidence Architecture](../architecture/05-evidence-architecture.md)
- [Domain Knowledge Architecture](../architecture/06-domain-knowledge-architecture.md)
- [Agent and Reasoning Architecture](../architecture/07-agent-reasoning-architecture.md)
- [Observability and Operations](../architecture/12-observability-and-operations.md)

## Governing distinctions

- Quality metrics describe analyzer, reasoning, or operational performance;
  they are not Evidence Graph observations about the analyzed system.
- Coverage, support, confidence, completeness, and resolution quality answer
  different questions and must be stored and presented separately.
- A missing finding means only that DomainLens did not establish it within the
  analyzed scope. It does not establish absence from the business or system.
- Semantic evaluation compares findings with expert-reviewed expectations,
  including accepted ambiguity and alternatives, rather than trusting model
  self-confidence.
- Recovered Domain Knowledge remains distinct from Proposed DDD Design.
  Epistemic classification remains independent of human review state.

The exact scoring rubrics, release thresholds, service objectives, and human
review thresholds remain open until representative evaluation data supports
their selection.
