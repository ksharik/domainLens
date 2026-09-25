# Deployment

> This document is a planned V1 direction, not a description of deployed
> infrastructure. See [Deployment Architecture](architecture/10-deployment-architecture.md).

## Target

Microsoft Azure is the target platform. DomainLens core is independently deployable; ChatGPT Sites/Apps are future clients rather than the runtime boundary.

## V1 topology

```text
Internet
  |
DomainLens Web UI
  |
DomainLens API/Core ---- Persistence
  |
Pipeline Coordinator
  |
Isolated Windows Analyzer Worker
  |
Evidence + Diagnostics
  |
Context Builder / Reasoning
  |
Finding Validation / Finding Graph / Human Review
  |
Persistent Domain Knowledge Model
```

The first workload is legacy .NET Framework/WCF, so accurate analysis may benefit from Windows reference assemblies, Roslyn/MSBuild semantics and framework-specific tooling. The worker boundary keeps these needs from constraining the web/API tier.

A lightweight database may be used initially behind persistence abstractions. Temporary repository workspaces are disposable; evidence, findings, analysis state and DDD models are durable.

Future expansion may add Linux/Java workers, containerized worker pools and eventually AKS when concurrency/isolation/scheduling scale justifies it.
