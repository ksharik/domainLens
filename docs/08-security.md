# Security

> Scanner 0.1 controls and planned V1 controls are separated in the detailed
> [Security Architecture](architecture/09-security-architecture.md).

All analyzed repository content is untrusted.

## Analyzer isolation

The planned analyzer worker receives only required source/snapshot data, has no repository credentials or unnecessary network access, uses a controlled temporary workspace and resource/time limits, and must not execute repository binaries, scripts, or build targets. Controlled semantic enrichment must not weaken this invariant.

## Intake

Public repository retrieval shall use allowlisted protocol/host policy plus protections against SSRF, unsafe redirects, oversized repositories and resource abuse.

## Model boundary

Trusted code constructs context. Model output remains untrusted until schema/provenance/policy validation succeeds. Models cannot elevate permissions or create Observed evidence. External actions require deterministic authorization/validation.

## Human input

Human-supplied domain context and human review decisions are separate untrusted inputs. A domain
statement used in reasoning is stored as a versioned Human Context record with provenance and may
be cited only as Human Context; it never becomes deterministic source evidence or an `Observed`
fact. Acceptance, rejection and challenge affect review state or create new revisions but do not
rewrite Evidence Graph records, prior Human Context revisions, or epistemic classification. Any
actor/session/principal attribution is conditional on the eventually approved identity model.

Before private repositories are supported, define source-code egress, model retention, secret-redaction and credential policies.
