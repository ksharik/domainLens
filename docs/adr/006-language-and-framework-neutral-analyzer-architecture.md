# ADR-006: Use a Language- and Framework-Neutral Analyzer Architecture

## Status

Accepted

## Context

Legacy C#/.NET Framework/WCF is the first supported workload, but DomainLens is
intended to analyze additional .NET, Java, web/API, database, persistence, and
messaging technologies. Encoding WCF concepts into the platform core would
make that evolution costly and distort the shared evidence model.

## Decision

DomainLens core and its normalized Evidence Model remain language- and
framework-neutral. Product V1 dispatches a configured, approved legacy
C#/.NET Framework/WCF analyzer set; it does not require generalized automatic
technology discovery or a generated Analysis Plan. The architecture supports
future deterministic technology discovery and Analysis Plan generation to
select applicable analyzer families when that capability is separately
approved. Analyzer families contribute normalized nodes, relationships,
evidence, provenance, diagnostics, and coverage/resolution information through
application-owned contracts.

The .NET/WCF family is first, not privileged in the core model. This ADR does
not define every future analyzer, a plug-in packaging mechanism, analyzer
dependency ordering, or compatibility/version-negotiation rules; those details
are deferred until the extension contract is designed.

## Consequences

- Framework-specific knowledge stays in analyzer modules and workers.
- Cross-technology evidence can be queried through shared graph concepts and
  provenance rules.
- New analyzer families must normalize their output without discarding
  technology-specific detail needed for traceability.
- The extension contract will need explicit capability, version, validation,
  and failure-isolation semantics.

## References

- [DomainLens README](../../README.md)
- [Architecture](../04-architecture.md)
- [Roadmap](../10-roadmap.md)
- [Repository Structure Scanner 0.1](../11-milestone-1-repository-scanner.md)
