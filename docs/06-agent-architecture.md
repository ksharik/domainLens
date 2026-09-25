# Agent Architecture

## Coordinator

V1 uses a fixed allowlisted workflow rather than arbitrary dynamic sub-agents. It manages analysis state, invokes deterministic capabilities, builds context packs, invokes approved reasoning skills, validates outputs and coordinates human review.

## Deterministic responsibilities

Git/snapshotting, path/network security, solution/project parsing, Roslyn/MSBuild integration, WCF/config analysis, relationship extraction, evidence storage, retrieval/token budgeting, pipeline state, schema/provenance validation and permission/resource policy.

As analyzer capabilities mature, deterministic evidence may include calls, mutations, persistence access, transaction constructs, authorization checks, state changes, messages, external dependencies and other implementation facts needed by the Domain Knowledge Model.

## AI reasoning responsibilities

AI reasoning may interpret evidence into business capabilities; actors/use cases; domains/subdomains and Core/Supporting/Generic classification; bounded contexts/context maps; aggregate/root, entity/value-object, domain-service, repository and factory interpretations; commands/events/handlers; business rules/invariants/policies; workflows/state transitions; application/integration relationships; security-policy interpretations; data ownership/consistency boundaries; coupling characteristics; alternatives; unresolved business questions; and proposals.

AI reasoning must not convert inferred business meaning into deterministic evidence.

## Initial skills

- `domain-discovery`
- `ddd-modeling`
- `explain-finding`
- `semantic-evidence-review`

Skill contracts should evolve to support the expanded Domain Knowledge Model while remaining evidence-bounded.

A skill is a trusted/versioned reasoning contract defining purpose, evidence requirements, context recipe, instructions, structured output, support rubric and validation/retry policy.

MCP, A2A and richer multi-agent coordination are roadmap capabilities, not V1 prerequisites.
