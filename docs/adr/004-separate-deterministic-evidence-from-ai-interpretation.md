# ADR-004: Separate Deterministic Evidence from AI Interpretation

## Status

Accepted

## Context

Source structure and relationships can often be established mechanically,
while business meaning and DDD boundaries require interpretation. Using a
model as a source-code parser would weaken repeatability, provenance, and the
ability to distinguish facts from hypotheses.

## Decision

DomainLens follows the governing rule:

> Code establishes evidence. AI interprets evidence. The agent orchestrates
> the process.

Deterministic application code owns repository intake, snapshotting, parsing,
static analysis, security enforcement, Evidence Graph construction, retrieval,
pipeline state, and schema/provenance validation. AI reasoning receives sealed,
task-specific evidence and may produce only **Inferred** or **Proposed**
findings. Its structured output remains untrusted until deterministic
validation succeeds. A model cannot create **Observed** evidence or authorize
tools and external actions.

Specific model providers, output schemas, support thresholds, and repair/retry
limits are deferred.

## Consequences

- Evidence extraction is reproducible and testable without an LLM.
- Semantic reasoning can change or be rerun without changing source facts.
- Reasoning requests need bounded context, evidence references, constraints,
  and validated structured responses.
- Some facts require richer deterministic analyzers before AI can reason about
  them reliably; missing evidence must be represented as a limitation rather
  than filled with model guesses.

## References

- [DomainLens README](../../README.md)
- [DomainLens Agent Instructions](../../AGENTS.md)
- [Analysis and Evidence Model](../05-analysis-model.md)
- [Agent Architecture](../06-agent-architecture.md)
- [Context Engineering](../07-context-engineering.md)
