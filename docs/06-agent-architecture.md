# Agent Architecture

> This document describes planned V1 behavior. Scanner 0.1 contains no agent or
> model invocation. See [Agent and Reasoning Architecture](architecture/07-agent-reasoning-architecture.md).

## Coordinator

V1 uses a fixed allowlisted workflow rather than arbitrary dynamic sub-agents. It manages analysis state, invokes deterministic capabilities, builds context packs, invokes approved reasoning skills, validates outputs and coordinates human review.

## Deterministic responsibilities

Git/snapshotting, path/network security, solution/project parsing, Roslyn analysis, controlled semantic enrichment, WCF/config analysis, relationship extraction, evidence storage, retrieval/token budgeting, pipeline state, schema/provenance validation and permission/resource policy. Scanner 0.1 is syntax-only and does not load or evaluate projects with MSBuild. Any future use of MSBuild-derived semantics must preserve the no-execution rule and run inside the isolated analyzer boundary.

As analyzer capabilities mature, deterministic evidence may include calls, mutations, persistence access, transaction constructs, authorization checks, state changes, messages, external dependencies and other implementation facts needed by the Domain Knowledge Model.

Analyzers do not search for DDD-named types as a prerequisite. They establish implementation facts from applications that may be procedural, transaction-script based, anemic, layered, service-oriented, monolithic, partially domain-oriented, or explicitly DDD.

## AI reasoning responsibilities

AI reasoning may recover evidence-backed knowledge about business capabilities; actors/use cases;
Domain Vocabulary and context-specific meanings; domains/subdomains; behavior; business
rules/invariants/policies; workflows/state transitions; application/integration relationships;
security-policy interpretations; data ownership/consistency boundaries; and coupling. Separately,
it may infer DDD structures that appear to exist or propose useful DDD representations such as
contexts, aggregates, roots, entities, value objects, domain services, repositories, factories,
commands, events, and handlers.

AI reasoning must not convert inferred business meaning into deterministic evidence or present a Proposed DDD Design as a recovered feature of the source system.

## Initial skills

- `domain-discovery`
- `ddd-modeling`
- `explain-finding`
- `semantic-evidence-review`

Skill contracts should evolve to support the expanded Domain Knowledge Model while remaining evidence-bounded.

A skill is a trusted/versioned reasoning contract defining purpose, evidence requirements, context recipe, instructions, structured output, support rubric and validation/retry policy.

Each skill's evidence requirements must align with the
[knowledge-to-evidence traceability](design/02-knowledge-evidence-traceability.md) contract. Its
output must keep Coverage, Support, Confidence, Completeness and deterministic Resolution Quality
distinct according to the [evaluation strategy](quality/01-evaluation-strategy.md); model
self-confidence cannot compensate for missing analyzer evidence.

MCP, A2A and richer multi-agent coordination are roadmap capabilities, not V1 prerequisites.
