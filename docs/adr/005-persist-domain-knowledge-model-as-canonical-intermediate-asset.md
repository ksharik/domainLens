# ADR-005: Persist the Domain Knowledge Model as the Canonical Intermediate Asset

## Status

Accepted

## Context

DomainLens is intended to produce reusable architecture knowledge, not a
disposable report. Later decomposition, modernization, target-architecture,
and migration work must be able to query the reconstructed domain without
reparsing narrative output or rerunning every earlier analysis stage.

## Decision

The persistent, structured, and queryable **Domain Knowledge Model** is the
canonical intermediate asset between evidence-backed Reverse DDD analysis and
later decomposition or modernization work. It is populated from validated
findings and preserves links to evidence, separately versioned Human Context where used,
classification, review state, and revision history. Human Context remains distinct from source
evidence, semantic findings, and human review decisions. Generated prose and diagrams are views of
the model, not the canonical persisted representation.

The storage engine, physical schema, transaction model, and exact rules for
projecting, superseding, or rejecting findings are deferred behind persistence
abstractions.

## Consequences

- Domain knowledge can be explored, revised, and consumed by later workflows
  without treating generated Markdown as a database.
- Persistence must support stable identities, provenance, relationships, versions, Human Context
  revision/supersession, and separate human review decisions.
- Decomposition and modernization models can evolve separately while retaining
  a traceable input model.
- A lightweight V1 store may be replaced by Azure-managed persistence without
  changing the conceptual model.

## References

- [Product Overview](../01-product-overview.md)
- [Functional Requirements](../03-functional-requirements.md)
- [Analysis and Evidence Model](../05-analysis-model.md)
- [Deployment](../09-deployment.md)
