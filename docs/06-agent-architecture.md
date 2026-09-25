# Agent Architecture

## Coordinator

V1 uses a fixed allowlisted workflow rather than arbitrary dynamic sub-agents. It manages analysis state, invokes deterministic capabilities, builds context packs, invokes approved reasoning skills, validates outputs and coordinates human review.

## Deterministic responsibilities

Git/snapshotting, path/network security, solution/project parsing, Roslyn/MSBuild integration, WCF/config analysis, relationship extraction, evidence storage, retrieval/token budgeting, pipeline state, schema/provenance validation and permission/resource policy.

## AI reasoning responsibilities

Business-language clustering; candidate domains/subdomains; bounded contexts; aggregate/root, entity/value-object and domain-service interpretations; commands/events; context maps; alternatives; unresolved business questions; and proposals.

## Initial skills

- `domain-discovery`
- `ddd-modeling`
- `explain-finding`
- `semantic-evidence-review`

A skill is a trusted/versioned reasoning contract defining purpose, evidence requirements, context recipe, instructions, structured output, support rubric and validation/retry policy.

MCP, A2A and richer multi-agent coordination are roadmap capabilities, not V1 prerequisites.
