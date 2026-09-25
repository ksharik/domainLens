# ADR-010: Separate Decomposition Analysis from Domain-Knowledge Reconstruction

## Status

Accepted

## Context

Reverse DDD reconstructs what the existing system and its evidence say about
the business and implementation, and may propose how that domain could be
represented using DDD. Decomposition asks a different question: how the
existing application might be separated or reorganized. Modernization asks
what the future implementation architecture should become. Mixing these
questions would make recommendations look like source facts and weaken the
reusable Domain Knowledge Model.

## Decision

DomainLens preserves this ordered boundary:

`Source Code → Evidence Graph → Semantic Findings → Domain Knowledge Model { Recovered Domain Knowledge + Proposed DDD Design } → Decomposition Analysis → Modernization Model → Target Architecture`

The Domain Knowledge Model remains one canonical asset with two explicitly
tagged semantic views: Recovered Domain Knowledge and Proposed DDD Design. A
Proposed DDD item remains `Proposed` after human acceptance. Proposed DDD
Design is part of Reverse DDD, not Decomposition Analysis. Decomposition
Analysis consumes a DKM version and produces separate separation or
reorganization analyses and recommendations. Decomposition recommendations
are never deterministic Evidence Graph observations and do not rewrite the
reconstructed model as if they were facts.

The decomposition algorithm, recommendation model, scoring, modernization
model, and target-architecture workflow are deferred beyond the Reverse DDD V1
baseline.

## Consequences

- Reconstructed knowledge remains useful even when modernization strategies
  change.
- Proposed DDD representations remain distinguishable from both recovered
  source-system knowledge and decomposition recommendations.
- Decomposition recommendations retain traceability through the Domain
  Knowledge Model to findings and evidence.
- Users can distinguish recovered/as-is knowledge, Proposed DDD Design,
  decomposition options, and future implementation architecture.
- Later decomposition work requires its own models, validation, revision, and
  human-review policies.

## References

- [Product Overview](../01-product-overview.md)
- [Functional Requirements](../03-functional-requirements.md)
- [Analysis and Evidence Model](../05-analysis-model.md)
- [Roadmap](../10-roadmap.md)
