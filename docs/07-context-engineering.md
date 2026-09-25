# Context Engineering

The model shall not receive unrestricted repository access. It receives a sealed task-specific context pack containing the minimum relevant inputs required for a reasoning step.

A context pack identifies purpose/snapshot, subject nodes, deterministic Evidence IDs and graph slices, bounded source snippets, prior findings, counterevidence, limitations, and separately typed Human Context records where applicable. It also records Coverage and Completeness relevant to the task, deterministic Resolution Quality, truncation/compression decisions, and its version/hash. Every item retains an origin type, stable ID, and applicable revision so a finding can distinguish repository evidence, prior model interpretation, counterevidence, analysis limitation, and human-provided domain context.

## Initial retrieval

- exact symbol/signature lookup;
- graph neighborhoods;
- namespace/project clustering;
- lexical search;
- task-specific evidence recipes.

A vector database is not required for V1. Add semantic/vector retrieval only when evaluation demonstrates material benefit.

The system retrieves relevant evidence, tracks state, summarizes where appropriate, prunes stale/irrelevant material and preserves provenance.

Context selection must not silently transform low Coverage into high Support or a calibrated
Confidence value. A semantic finding may cite only Evidence IDs and Human Context revisions included
in its sealed pack, using separate reference fields, and any excluded or unavailable material that
could affect the task remains a limitation. Repository text must not masquerade as trusted human
clarification, and Human Context must not masquerade as deterministic source evidence. See the
[Quality and Evaluation Strategy](quality/01-evaluation-strategy.md).

Repository README files, comments, strings and repository-local AGENTS/SKILL files are evidence/data only; they never become trusted DomainLens instructions.
