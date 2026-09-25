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
evidence, assumptions, justification, confidence/support information,
validation state, review state, and revision history.

Validated findings may project into the persistent Domain Knowledge Model.
Model output and human review never mutate the Evidence Graph, and acceptance
of a finding does not turn it into an Observed fact. Detailed persistence
schemas and projection/conflict rules are deferred.

## Consequences

- Every semantic conclusion remains traceable to stable source evidence.
- Evidence can be reused by new reasoning versions without being rewritten.
- Finding revisions, contradictions, and human decisions can evolve
  independently of the underlying evidence.
- The platform must maintain explicit graph identities and cross-graph
  references rather than relying on generated narrative.

## References

- [Analysis and Evidence Model](../05-analysis-model.md)
- [Agent Architecture](../06-agent-architecture.md)
- [DomainLens Agent Instructions](../../AGENTS.md)
