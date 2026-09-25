# Functional Requirements

## Repository intake

The UI shall accept a public Git repository URL, validate supported hosts/URL forms, safely select a branch/tag/commit where appropriate, and analyze an identifiable repository snapshot.

## Pipeline

The coordinator shall expose durable stages conceptually similar to:

`Created → Snapshotted → Inventoried → DeterministicAnalysis → WcfAnalysis → EvidenceReady → ContextPrepared → SemanticReasoning → Validating → ReviewReady → Completed`

`Failed` and `Cancelled` are explicit outcomes. Stages should be idempotent/resumable where practical.

## Progress and human interaction

The UI shall display stage/progress/diagnostics. The pipeline may pause when user clarification materially affects DDD interpretation. Responses are recorded and incorporated into subsequent reasoning. Findings may be challenged and re-analyzed without destroying revision history.

## Results and persistence

Users shall explore As-Is architecture/evidence and Proposed DDD, navigating findings to supporting source locations.

Persist repository/snapshot metadata, runs, evidence, findings, DDD concepts/relationships, user decisions, revisions and diagnostics. Persistence shall be abstracted from the database engine.

Persisted DDD models must be reusable by future modernization workflows without reparsing generated prose.
