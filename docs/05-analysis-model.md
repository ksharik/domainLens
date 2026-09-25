# Analysis and Evidence Model

## Evidence Graph

Contains deterministic facts established from a repository snapshot: structure, declarations, WCF constructs, relationships, source spans/hashes, extractor provenance and resolution.

## Finding Graph

Contains semantic interpretations such as candidate bounded contexts, aggregates, entities, value objects, commands/events and proposals.

A model response cannot create an Observed fact.

## Classification

- **Observed** — deterministically established.
- **Inferred** — interpretation supported by evidence.
- **Proposed** — architectural/domain recommendation.

Review status is independent of classification.

## Provenance

Source-backed evidence records snapshot, relative file, content hash, source span, extractor/rule/version and resolution (exact/partial/ambiguous/unresolved).

Findings preserve atomic claim, concept type, classification, subject nodes, supporting/counter evidence, assumptions, justification, support/coverage, alternatives, unresolved questions, producer/version, validation/review status and revision history.

## Persistent DDD model

Validated findings project into a queryable model of domains, subdomains, bounded contexts, aggregates/roots, entities, value objects, services, commands, events, repositories and context relationships. This is a durable platform asset for future modernization.
