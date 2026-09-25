# ADR-009: Do Not Require Dynamic Agents, MCP, or A2A for V1

## Status

Accepted

## Context

V1 needs predictable execution, bounded permissions, reproducible analysis,
and clear validation of model output. Runtime creation of arbitrary agents or
open-ended tool protocols would expand the security and operational surface
before the core evidence-to-findings workflow is proven.

## Decision

V1 uses a fixed, allowlisted coordinator workflow. Deterministic capabilities
remain application code, while trusted and versioned reasoning skills define
bounded semantic tasks such as `domain-discovery`, `ddd-modeling`,
`explain-finding`, and `semantic-evidence-review`.

Dynamic agent creation, MCP, A2A, and richer multi-agent coordination are not
required for V1. They remain possible future evolutions subject to explicit
security, authorization, provenance, evaluation, and operational decisions.
This ADR does not select an orchestration framework or define final skill wire
schemas and retry policies.

## Consequences

- The coordinator can enforce a known workflow and least-privilege capability
  set.
- Skills can evolve as versioned reasoning contracts without becoming arbitrary
  executable plug-ins.
- V1 may use predefined internal stages or specialists, but cannot create
  unreviewed agents or grant them unbounded tools at runtime.
- Later MCP, A2A, or multi-agent adoption requires a new architectural decision.

## References

- [V1 Scope](../02-v1-scope.md)
- [Agent Architecture](../06-agent-architecture.md)
- [DomainLens Agent Instructions](../../AGENTS.md)
