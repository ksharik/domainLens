# Roadmap

## V1 — End-to-End Reverse DDD for legacy .NET/WCF

Azure deployment; public Git intake; native UI/progress; C#/.NET Framework structural analysis; WCF analysis; evidence/relationship graphs; AI-assisted domain discovery and DDD modeling; validation; human clarification; persistent Domain Knowledge Model; evidence-backed explorer.

V1's target knowledge model includes business capabilities, actors/use cases, strategic and tactical DDD, business rules/invariants/workflows, application/integration relationships, security policies, data ownership/consistency and decomposition-relevant coupling evidence.

## Engineering milestones inside V1

0. Deployment/security feasibility spike (prerequisite).
1. Repository Structure Scanner 0.1.
2. WCF discovery.
3. Relationship/persistence analysis.
4. Evidence persistence/explorer.
5. Context builder and structured reasoning runtime.
6. Domain discovery.
7. DDD modeling and validation.
8. Human review/challenge workflow.
9. Azure end-to-end deployment/hardening.

Before the domain-discovery and DDD-modeling milestones, evidence extraction and reasoning contracts must be checked against the expanded Domain Knowledge Model so business behavior, security, ownership/consistency and coupling concepts are not lost merely because the initial scanner focused on structural evidence.

## Future analyzers

Pluggable analyzer families should add: ASP.NET MVC / MVC.NET; ASP.NET Web API; modern .NET APIs/apps; Java; Java EE/Jakarta enterprise applications; Spring Framework; Spring Boot; REST/OpenAPI; relational database/persistence frameworks; messaging/event-driven systems such as Kafka/RabbitMQ patterns; and additional stacks based on demand.

Repository discovery should eventually detect technologies and build an Analysis Plan automatically.

## Infrastructure evolution

Add Linux workers; containerize where useful; introduce queues/worker pools for concurrency; consider AKS only when scale and scheduling/isolation justify it; move to managed database infrastructure as durability/concurrency requires.

## Beyond Reverse DDD

Decomposition analysis operates over the persisted Domain Knowledge Model rather than being mixed into source evidence or DDD discovery.

`Source Code → Evidence Graph → Semantic Findings → Domain Knowledge Model → Decomposition Analysis → Modernization Model → Target Architecture → Migration Planning`
