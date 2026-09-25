# Extensibility Architecture

## Status and scope

DomainLens is language- and framework-extensible. Legacy C#/.NET Framework/WCF is the first
supported workload, not the architectural boundary of the platform.

- **CURRENT — Milestone 1:** one built-in C# Repository Structure Scanner creates language-neutral Evidence Graph records for repository, solution, project, dependency, namespace, type, and member structure. There is no analyzer plugin system, technology-discovery service, or generated Analysis Plan.
- **PLANNED — Product V1:** deterministic technology discovery, a fixed Analysis Plan, allowlisted .NET/WCF and relationship/persistence analyzers, and normalized evidence contribution/validation contracts.
- **FUTURE:** ASP.NET, modern .NET, Java/Jakarta, Spring, database, OpenAPI, messaging, and other analyzer families, including Linux-capable workers where appropriate.

This document defines the concerns an analyzer boundary must satisfy. It deliberately does not
invent a C# interface, package-loading mechanism, plugin manifest schema, or analyzer-specific data
model before those components are designed.

## Extensibility flow

```mermaid
flowchart LR
    snapshot["Immutable Repository Snapshot"]
    discovery["Deterministic Technology Discovery"]
    plan["Fixed Analysis Plan"]
    catalog["Trusted allowlisted Analyzer Catalog"]

    subgraph worker["Compatible isolated worker profile"]
        foundation["Repository / language structural analyzer"]
        framework["Framework analyzer"]
        persistence["Persistence / data analyzer"]
        integration["API / messaging / integration analyzer"]
        relationships["Relationship enrichment"]
    end

    normalize["Normalized Evidence contribution boundary"]
    validate["Evidence Kernel validation and merge"]
    graph["Versioned Evidence Graph"]
    diagnostics["Coverage and analyzer diagnostics"]

    snapshot --> discovery
    discovery --> plan
    catalog --> plan
    plan --> foundation
    plan --> framework
    plan --> persistence
    plan --> integration
    plan --> relationships
    foundation --> normalize
    framework --> normalize
    persistence --> normalize
    integration --> normalize
    relationships --> normalize
    normalize --> validate
    validate --> graph
    validate --> diagnostics
```

Technology discovery and plan selection are deterministic application behavior. A repository may
contain configuration suggesting an analyzer, but repository content cannot supply executable
analyzer code, alter the trusted catalog, or turn its own instructions into a plan.

## Core neutrality

The Evidence Kernel owns common concepts needed by all analyzer families:

- an identifiable repository snapshot and normalized manifest;
- language-neutral nodes and directed relationships;
- stable logical and snapshot-scoped identities;
- source evidence with repository-relative path, content hash, source span, extractor/rule/version, and resolution;
- diagnostics and explicit coverage loss;
- deterministic canonical serialization and graph integrity validation.

The core may allow analyzer-owned stable `Kind`, relationship kind, attribute, and property values,
but it must not depend on Roslyn symbols, WCF attributes, Java AST objects, Entity Framework
metadata objects, or any other analyzer-runtime identity. Technology-specific detail is normalized
at the analyzer boundary and retains provenance sufficient to explain how it was established.

The Domain Knowledge Model is also technology-independent. WCF operations, Spring controllers,
OpenAPI operations, or messaging consumers may provide different evidence for concepts such as an
application service or command, but those business interpretations remain semantic findings rather
than technology-specific facts smuggled into the core.

## Source-architecture neutrality

Analyzer families must support applications that were never designed using DDD: traditional
N-tier, transaction-script, anemic-domain-model, service-oriented, procedural, tightly coupled
monolithic, partially domain-oriented, and explicitly DDD systems are all valid inputs. Analyzer
selection and evidence extraction must not require classes, interfaces, base types, attributes, or
folders named `AggregateRoot`, `Entity`, `ValueObject`, `DomainEvent`, or `BoundedContext`.

When those names occur, an analyzer may record the declaration, inheritance, attribute, or
relationship as Observed evidence. The name is neither necessary nor sufficient to establish the
DDD role. Analyzer capabilities should instead establish supported implementation facts about
behavior, rules, invariants, data relationships, mutations, transactions, workflows, state,
operations, persistence, messages, security, dependencies, and coupling. Semantic reasoning then
decides whether those facts support Recovered Domain Knowledge or a Proposed DDD Design.

## Technology discovery and the Analysis Plan

Technology Discovery examines the immutable snapshot using safe, deterministic rules. Its output
describes observed indicators and limitations, such as project formats, languages, configuration
types, and framework markers. It does not decide domain meaning.

The Analysis Planner combines those observations with the trusted analyzer catalog and policy to
produce a fixed, versioned plan. A plan should eventually make the following concerns explicit:

- snapshot and requested scope;
- selected analyzer capability IDs and versions;
- ordering or prerequisite constraints;
- compatible worker capabilities and operating-system profile;
- bounded analyzer configuration and resource class;
- expected evidence schema compatibility;
- reasons an analyzer was selected, skipped, or could not run; and
- cancellation/deadline context.

The exact plan schema and selection rules are open decisions. In V1, plans are created from a
predefined allowlist; dynamic model-generated analyzers or arbitrary agent composition are out of
scope.

## Analyzer contract concerns

Any future analyzer contract must address the following without assuming a particular programming
language or loading mechanism:

| Concern | Required architectural behavior |
|---|---|
| Identity and version | A stable analyzer/extractor identity and version accompany its evidence so results can be reproduced, compared, invalidated, or migrated. |
| Capabilities | The analyzer declares the technologies/artifact types it understands, prerequisites, and compatible worker profiles. |
| Input boundary | The analyzer receives only the immutable snapshot view and bounded plan/configuration required for its task. It cannot expand its own permissions. |
| Determinism | Given the same captured bytes, analyzer/rule version, and declared configuration, deterministic analysis produces the same normalized contribution or explicit diagnostics. |
| Evidence output | Observations use the normalized Evidence Model, carry source or metadata provenance, resolution basis/quality, and stable identities, and never contain live parser/runtime objects. DDD-like names remain declarations or relationships, not preclassified domain roles. |
| Coverage | Unsupported, ambiguous, malformed, excluded, conditional, or inaccessible input yields typed diagnostics and appropriate partial/unresolved resolution rather than guessed facts. |
| Composition | Analyzer output can reference valid existing nodes or contribute new normalized nodes/edges according to merge and identity rules. Conflicts are surfaced, not resolved by ordering accident. |
| Validation | Contributions pass schema, identity, reference, provenance, path, size, and policy validation before entering the Evidence Graph. Invalid output cannot be repaired into Observed evidence by an LLM. |
| Security | The analyzer is trusted, deployed DomainLens code. Repository-supplied tasks, plugins, scripts, generators, and build analyzers remain data. Network, filesystem, credentials, process, time, and resources are constrained by its worker profile. |
| Cancellation and failure | Cooperative cancellation is supported, while the worker host independently enforces deadlines and termination. Failure and coverage loss are explicit. |
| Observability | The analyzer emits redacted structured diagnostics and timing/counter data correlated to the Analysis Run and records its version in durable results. |

## Composition and evidence graph growth

Analyzers extend the Evidence Graph; they do not create parallel, incompatible source-of-truth
graphs per technology. Contributions are accepted only through the Evidence Kernel so canonical
identity and provenance invariants are consistent across analyzer families.

Composition may include:

- foundational repository/project nodes used by multiple analyzers;
- technology-specific observed nodes, such as a WCF contract or database mapping;
- resolved relationships to existing nodes when the target is deterministically established;
- unresolved or ambiguous textual targets when exact resolution is unavailable;
- multiple evidence records supporting one logical node or relationship; and
- diagnostics identifying skipped analyzers or incomplete coverage.

The precise collision, precedence, and merge algorithm for multiple analyzers contributing to the
same logical observation is not yet approved. Until it is, documentation must not claim that
last-writer-wins, priority ordering, or model arbitration is the architecture.

## Analyzer families

| Analyzer family | Product horizon | Intended deterministic contribution | Likely worker profile |
|---|---|---|---|
| Repository / C# structure | CURRENT foundation | Manifest, solutions, projects, dependencies, declarations, and syntax-resolvable relationships with provenance. | Current local process; planned Windows worker. |
| Classic WCF | PLANNED V1 | Service/data/message contracts, operations, implementations, endpoints, bindings, hosting configuration, and supported relationships. | Windows-oriented legacy .NET worker. |
| Relationship / persistence | PLANNED V1 | Calls, mutations, repository/data access, transaction constructs, configuration, external calls, and other supported implementation facts. | Windows worker initially. |
| ASP.NET MVC / MVC.NET | FUTURE | Controllers, actions, routing, filters, models, and supported application relationships. | Windows or .NET-capable profile, to be established. |
| ASP.NET Web API / modern .NET | FUTURE | HTTP endpoints, middleware, dependency relationships, contracts, and configuration. | Cross-platform or Windows depending on artifacts. |
| Java EE / Jakarta | FUTURE | Enterprise components, APIs, configuration, persistence, and supported relationships. | Linux-capable Java worker. |
| Spring / Spring Boot | FUTURE | Controllers, services, configuration, dependency injection, persistence, messaging, and supported relationships. | Linux-capable Java worker. |
| Database / persistence technology | FUTURE | Schemas/mappings, stores, tables, ownership/access evidence, transaction and consistency constructs where statically established. | Selected by tooling and artifact needs. |
| REST / OpenAPI | FUTURE | API operations, schemas, contracts, and integration dependencies. | Cross-platform. |
| Messaging / event-driven | FUTURE | Producers, consumers, messages, topics/queues, handlers, and supported delivery/configuration facts. | Selected by implementation stack. |

This table is a roadmap boundary, not a commitment to design every analyzer now. Each family needs
its own evidence rules, threat review, fixtures, determinism tests, and coverage semantics before it
is enabled.

## Framework-specific evidence versus semantic reasoning

An analyzer may establish that a method is decorated with a WCF `OperationContract` attribute or
that code writes to a mapped table. It may not conclude solely from that fact that the method is a
business command, the table belongs to a bounded context, or a class is an aggregate root. Those
are evidence-backed semantic findings created later through the
[agent and reasoning architecture](07-agent-reasoning-architecture.md).

Likewise, the literal names `AggregateRoot`, `Entity`, `ValueObject`, `DomainEvent`, and
`BoundedContext` are not privileged shortcuts. They can guide retrieval only alongside other
evidence; they cannot satisfy a reasoning recipe by themselves. Their absence cannot reduce
analyzer coverage when the relevant behavior and relationships are otherwise available.

Likewise, an LLM is not an analyzer implementation. It cannot compensate for a missing parser by
creating Observed nodes, edges, source spans, or resolution. The separation keeps new technology
support testable and makes the platform's confidence limits visible.

## Versioning and compatibility

Analyzer version, rule ID, normalized Evidence Model schema version, and relevant configuration
must be retained with durable analysis state. A later analyzer version may create a new analysis or
evidence revision; it must not silently rewrite the output of an earlier snapshot/run.

Schema evolution must provide an explicit compatibility and migration policy before multiple
independently evolving analyzer families are introduced. The current
`domainlens.evidence.v1` document is the Milestone 1 contract, not a guarantee that all future
framework concepts can be encoded without versioning.

## Open decisions

- **OPEN DECISION — analyzer packaging:** built-in modules, signed packages, separately deployed workers, or another trusted distribution model.
- **OPEN DECISION — registration:** catalog manifest, dependency injection registration, or another allowlisted discovery mechanism. Repository-driven dynamic loading is not an option.
- **OPEN DECISION — contract shape:** the concrete analyzer, technology-observation, Analysis Plan, contribution, and diagnostic schemas/interfaces.
- **OPEN DECISION — plan selection:** deterministic rule precedence, user overrides, conflicting technology indicators, and skipped-analyzer semantics.
- **OPEN DECISION — graph composition:** namespace governance for kinds/rules, identity collisions, duplicate contributions, precedence, and cross-analyzer reference resolution.
- **OPEN DECISION — version compatibility:** Evidence Model evolution, analyzer/core compatibility ranges, result migration, and re-analysis triggers.
- **OPEN DECISION — worker scheduling:** capability/OS matching, resource classes, analyzer co-location, and isolation profiles.
- **OPEN DECISION — analyzer conformance:** the fixture, determinism, security, performance, provenance, and coverage test suite required before catalog admission, including representative non-DDD systems and misleading or absent DDD-style names.

## Related architecture

- [Logical Architecture](02-logical-architecture.md)
- [Runtime and Process Architecture](03-runtime-architecture.md)
- [Analysis Pipeline](04-analysis-pipeline.md)
- [Evidence Architecture](05-evidence-architecture.md)
- [Agent and Reasoning Architecture](07-agent-reasoning-architecture.md)
- [Security Architecture](09-security-architecture.md)
- [Roadmap](../10-roadmap.md)
- [Repository Structure Scanner 0.1](../11-milestone-1-repository-scanner.md)

Related accepted decisions: [ADR-006](../adr/006-language-and-framework-neutral-analyzer-architecture.md)
and [ADR-009](../adr/009-no-dynamic-agents-mcp-or-a2a-required-for-v1.md).
