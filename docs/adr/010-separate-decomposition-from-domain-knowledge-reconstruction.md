# ADR-010: Separate Decomposition Analysis from Domain-Knowledge Reconstruction

## Status

Accepted

## Context

Reverse DDD reconstructs what the existing system and its evidence say about
the business and implementation. Decomposition asks a different question: how
that reconstructed system might be separated or modernized. Mixing the two
would make recommendations look like source facts and weaken the reusable
Domain Knowledge Model.

## Decision

DomainLens preserves this ordered boundary:

`Source Code → Evidence Graph → Semantic Findings → Domain Knowledge Model → Decomposition Analysis → Modernization Model → Target Architecture`

The Domain Knowledge Model describes evidence-backed reconstructed knowledge.
Decomposition Analysis consumes that model and produces separate analyses and
recommendations. Decomposition recommendations are never deterministic
Evidence Graph observations and do not rewrite the reconstructed model as if
they were facts.

The decomposition algorithm, recommendation model, scoring, modernization
model, and target-architecture workflow are deferred beyond the Reverse DDD V1
baseline.

## Consequences

- Reconstructed knowledge remains useful even when modernization strategies
  change.
- Decomposition recommendations retain traceability through the Domain
  Knowledge Model to findings and evidence.
- Users can distinguish an as-is understanding from a possible future design.
- Later decomposition work requires its own models, validation, revision, and
  human-review policies.

## References

- [Product Overview](../01-product-overview.md)
- [Functional Requirements](../03-functional-requirements.md)
- [Analysis and Evidence Model](../05-analysis-model.md)
- [Roadmap](../10-roadmap.md)
