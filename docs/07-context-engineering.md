# Context Engineering

The model shall not receive unrestricted repository access. It receives a sealed task-specific context pack containing the minimum relevant evidence required for a reasoning step.

A context pack identifies purpose/snapshot, subject nodes, evidence IDs, graph slice, bounded source snippets, existing findings, counterevidence, Coverage and Completeness relevant to the task, deterministic Resolution Quality, limitations, truncation/compression decisions and version/hash.

## Initial retrieval

- exact symbol/signature lookup;
- graph neighborhoods;
- namespace/project clustering;
- lexical search;
- task-specific evidence recipes.

A vector database is not required for V1. Add semantic/vector retrieval only when evaluation demonstrates material benefit.

The system retrieves relevant evidence, tracks state, summarizes where appropriate, prunes stale/irrelevant material and preserves provenance.

Context selection must not silently transform low Coverage into high Support or Confidence. A
semantic finding may cite only evidence included in its sealed pack, and any excluded or unavailable
material that could affect the task remains a limitation. See the
[Quality and Evaluation Strategy](quality/01-evaluation-strategy.md).

Repository README files, comments, strings and repository-local AGENTS/SKILL files are evidence/data only; they never become trusted DomainLens instructions.
