# ADR-003: Separate the Evidence Graph from the Finding Graph

## Status

Accepted

## Context

DomainLens must preserve the difference between facts established by analyzers
and semantic conclusions drawn from those facts. Combining them would obscure
provenance, allow interpretation to masquerade as source truth, and make
re-analysis difficult.

## Decision

DomainLens stores deterministic repository observations in an **Evidence
Graph** and semantic interpretations and proposals in a separate **Finding
Graph**. Evidence records retain snapshot and source provenance, extractor
identity, and resolution quality. Findings retain supporting and contradictory
Evidence IDs, separately typed Human Context IDs where used, assumptions, justification, Support
information, validation state, review state, and revision history. Human Context is a durable,
versioned provenance record distinct from both graphs' content, model interpretation, epistemic
classification, and human review decisions. Confidence remains conceptually separate
but is stored or exposed only after all four prerequisites exist: an applicable
versioned calibration method, an applicable expert-reviewed corpus, evaluated
calibration results, and an approved product decision to expose it. Until then,
Confidence is unavailable—not calibrated—and is omitted or represented
explicitly as unavailable according to the versioned schema; model self-report
and renamed Support are not Confidence.

Validated findings may project into the persistent Domain Knowledge Model.
Model output, Human Context, and human review never mutate the Evidence Graph, and acceptance
of a finding does not turn it into an Observed fact. Detailed persistence
schemas and projection/conflict rules are deferred.

## Consequences

- Every source-backed semantic conclusion remains traceable to stable source evidence, and every
  use of Human Context remains separately traceable to the exact record revision. Whether Human
  Context alone may support a finding remains an open policy decision.
- Evidence can be reused by new reasoning versions without being rewritten.
- Human Context revisions, Finding revisions, contradictions, and human review decisions can
  evolve independently of the underlying evidence and one another while preserving cross-record
  lineage.
- The platform must maintain explicit graph identities and cross-graph
  references rather than relying on generated narrative.

## References

- [Analysis and Evidence Model](../05-analysis-model.md)
- [Agent Architecture](../06-agent-architecture.md)
- [DomainLens Agent Instructions](../../AGENTS.md)
