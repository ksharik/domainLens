# ADR-007: Use .NET 10 LTS for the DomainLens Host

## Status

Accepted

## Context

DomainLens is a new platform that needs a supported, stable host runtime while
it analyzes applications built on many older and newer target frameworks.
The host runtime and an analyzed repository's runtime are independent concerns.

## Decision

DomainLens production and test projects target .NET 10 LTS where appropriate.
The repository SDK policy establishes a .NET 10 minimum feature band and
permits roll-forward to a compatible later installed feature band. CI installs
and validates with .NET 10.

This decision applies to the DomainLens host, not to analyzed repositories.
Fixtures and source systems intentionally representing .NET Framework or other
target frameworks retain those targets. Later host-runtime upgrades require a
separate reviewed change.

## Consequences

- The platform receives the support lifetime and runtime/tooling capabilities
  of the active LTS release.
- Developer and CI environments need a compatible .NET 10 SDK.
- Analyzer contracts must not assume that analyzed code targets .NET 10.
- Multi-target and legacy-framework fixtures remain valid compatibility data.

## References

- [DomainLens README](../../README.md)
- [V1 Scope](../02-v1-scope.md)
- [Repository Structure Scanner 0.1](../11-milestone-1-repository-scanner.md)
