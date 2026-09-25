# ADR-008: Do Not Require a Vector Database for V1

## Status

Accepted

## Context

Reasoning steps need small, relevant, traceable evidence sets rather than the
entire repository. Much of the initial retrieval problem can be addressed with
canonical identities, graph traversal, exact symbol/signature lookup,
namespace/project clustering, lexical search, and task-specific evidence
recipes. A vector database would add infrastructure and evaluation complexity
before its benefit is established.

## Decision

V1 does not require a vector database. Initial ContextPack construction uses
deterministic and inspectable retrieval methods over the Evidence Graph and
source index. Semantic/vector retrieval may be added later only when evaluation
demonstrates material benefit for defined tasks.

No vector technology, provider, embedding model, quality threshold, or
operational topology is selected by this ADR.

## Consequences

- V1 has fewer infrastructure components and a more explainable retrieval
  path.
- Retrieval quality must be evaluated using the initial graph, exact, lexical,
  and recipe-based mechanisms.
- The ContextPack contract remains compatible with a future semantic retriever.
- A later vector solution must preserve repository isolation, provenance,
  versioning, deletion, and source-code-egress policies.

## References

- [V1 Scope](../02-v1-scope.md)
- [Context Engineering](../07-context-engineering.md)
