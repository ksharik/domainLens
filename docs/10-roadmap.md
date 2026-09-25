# Roadmap

## V1 — End-to-End Reverse DDD for legacy .NET/WCF

Azure deployment; public Git intake; native UI/progress; C#/.NET Framework structural analysis; WCF analysis; evidence/relationship graphs; AI-assisted domain discovery and DDD modeling; validation; human clarification; persistent DDD model; evidence-backed explorer.

## Engineering milestones inside V1

1. Deployment/security feasibility spike.
2. Repository Structure Scanner 0.1.
3. WCF discovery.
4. Relationship/persistence analysis.
5. Evidence persistence/explorer.
6. Context builder and structured reasoning runtime.
7. Domain discovery.
8. DDD modeling and validation.
9. Human review/challenge workflow.
10. Azure end-to-end deployment/hardening.

## Future analyzers

Pluggable analyzer families should add: ASP.NET MVC / MVC.NET; ASP.NET Web API; modern .NET APIs/apps; Java; Java EE/Jakarta enterprise applications; Spring Framework; Spring Boot; REST/OpenAPI; relational database/persistence frameworks; messaging/event-driven systems such as Kafka/RabbitMQ patterns; and additional stacks based on demand.

Repository discovery should eventually detect technologies and build an Analysis Plan automatically.

## Infrastructure evolution

Add Linux workers; containerize where useful; introduce queues/worker pools for concurrency; consider AKS only when scale and scheduling/isolation justify it; move to managed database infrastructure as durability/concurrency requires.

## Beyond Reverse DDD

`Legacy System → Reverse Engineering → Reverse DDD → Persistent Domain Model → Modernization Analysis → Target Architecture → Migration Planning`
