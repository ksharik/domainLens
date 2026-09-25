# Product Overview

## Vision

DomainLens is an AI-assisted software architecture discovery and Reverse DDD platform that reconstructs domain knowledge from existing systems while preserving traceability to implementation evidence.

It is language/framework extensible. .NET Framework/WCF is the first supported stack.

## V1 experience

The user opens the DomainLens web application, supplies a public Git repository URL and starts analysis. DomainLens validates and snapshots the repository, inventories technologies, performs deterministic and WCF-specific analysis, builds an Evidence Graph, prepares bounded context packs, performs AI-assisted domain discovery and DDD modeling, validates findings, requests human clarification when needed, persists the analysis/DDD model, and presents an interactive results workspace.

## Durable product asset

The DDD model is not a disposable report. It is a canonical intermediate asset intended to feed future modernization analysis, target architecture and migration planning.

## Clients

The native web application is the primary V1 client. DomainLens exposes APIs so future ChatGPT Sites/Apps and other clients can use the platform without constraining core capabilities.
