# ADR-002: Modular Architecture with an Isolated Analyzer Worker

## Status

Accepted

## Context

Most DomainLens capabilities belong to one cohesive product, while repository
analysis processes attacker-controlled files and may eventually require legacy
.NET Framework, WCF, Roslyn, MSBuild, and reference-assembly tooling. That work
needs a stronger trust boundary than ordinary in-process modularity provides.

## Decision

DomainLens V1 uses a modular application architecture for the Web/API/Core and
an isolated analyzer-worker execution boundary for untrusted repository
analysis. Logical modules do not automatically become independently deployable
services.

The worker receives only the inputs and permissions required for an analysis,
uses a disposable workspace, and is subject to filesystem, network,
credential, resource, cancellation, and time limits. The initial worker is
Windows-oriented for legacy .NET Framework/WCF analysis. Exact process,
container, VM, queue, scheduling, and worker-pool mechanisms are deferred; AKS
is not a V1 prerequisite.

## Consequences

- Application cohesion is preserved without introducing incidental
  microservices.
- Untrusted analysis failures and repository content are contained outside the
  trusted Web/API/Core process.
- Worker communication needs explicit, versioned input, evidence, diagnostic,
  cancellation, and result contracts.
- Additional worker families, including Linux workers, can be introduced when
  new analyzer ecosystems require them.

## References

- [Architecture](../04-architecture.md)
- [Security](../08-security.md)
- [Deployment](../09-deployment.md)
- [Repository Structure Scanner 0.1](../11-milestone-1-repository-scanner.md)
