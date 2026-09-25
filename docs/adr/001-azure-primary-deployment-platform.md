# ADR-001: Azure as the Primary Deployment Platform

## Status

Accepted

## Context

DomainLens V1 is an end-to-end hosted product whose first workload requires a
web experience, durable application state, model connectivity, and an isolated
Windows-capable analyzer environment. A target cloud is needed to guide
deployment architecture without coupling the application core to one vendor's
SDKs.

## Decision

Microsoft Azure is the primary deployment platform for DomainLens. Deployment
architecture will describe roles such as Web/UI hosting, API/Core hosting,
analyzer workers, persistence, temporary workspaces, model connectivity,
observability, and secrets/configuration in Azure.

DomainLens core remains behind application-owned ports and adapters rather than
depending directly on Azure-specific APIs. This decision does not select
specific Azure services, database products, hosting plans, or orchestration
technology; those choices are deferred until their requirements are known.

## Consequences

- V1 deployment and operational guidance can assume Azure capabilities.
- Cloud-specific integrations belong in adapters and deployment code, keeping
  core analysis and domain logic independently testable and portable.
- Future clients, including ChatGPT integrations, consume DomainLens APIs and
  do not become its runtime boundary.
- Concrete Azure service selection requires later decisions and may evolve
  without changing this ADR.

## References

- [V1 Scope](../02-v1-scope.md)
- [Architecture](../04-architecture.md)
- [Deployment](../09-deployment.md)
