# Deterministic analyzer fixtures

These directories are repository-shaped **test data** for the deterministic
Repository Structure Scanner. They are not part of the DomainLens build and
must never be restored, built, imported as trusted MSBuild projects, or used to
load analyzers or source generators.

All paths and file contents are intentionally stable so repeated scans can be
compared for equivalent canonical output.

| Fixture | Expected observations |
| --- | --- |
| `MultiProjectSdk` | One SDK-style solution with two C# projects, two target frameworks on the domain project, a project reference, a package reference, an assembly reference, and source declarations covering attributes, classes, interfaces, structs, enums, records, constructors, properties, methods, inheritance, implementation, and declared type dependencies. |
| `LegacyFramework` | One traditional non-SDK .NET Framework 4.7.2 project and solution, explicit `Compile` items, framework assembly references, and classic C# declarations. The `Microsoft.CSharp.targets` import is data only. |
| `ProjectOnly` | One SDK-style C# project with no solution, proving repository inventory can fall back to project discovery. |
| `PartialAnalysis` | One discoverable SDK-style project with a missing project reference, an unresolved assembly hint path, an unresolved source type, and a recoverable C# syntax error. Analysis should retain useful declarations while reporting explicit partial-analysis diagnostics. |
| `MaliciousBuild` | One SDK-style project containing repository-controlled `Exec` and custom targets, an inline `UsingTask`, pre/post-build hooks, a package reference, an analyzer/source-generator declaration, repository-local build configuration and imports, a package source, and a script. These remain inert data during safe analysis. |
| `MsBuildPartial` | One SDK-style project under an unevaluated `Directory.Build.props`, proving source membership remains explicit partial evidence. |
| `WcfBasic` | A net472 classic WCF contract with an operation, declared fault, data contracts, data members, and a source implementation. |
| `WcfRich` | Callback and fully qualified service contracts, aliases, attribute suffix variants, overloads, and wrapped/unwrapped message contracts with header/body members. |
| `WcfInheritance` | Base and derived service contracts plus inherited, explicit, and alternative source implementations. |
| `WcfHostingConfig` | Resolved and unresolved `.svc` declarations and an allowlisted `system.serviceModel` subset covering services, clients, endpoints, bindings, behaviors, identity, and service activation. |
| `WcfProgrammatic` | Direct `ServiceHost` and `ChannelFactory<T>` construction plus compiler-generated and hand-written `ClientBase<T>` clients. |
| `WcfAmbiguousHostile` | A non-net472 profile, duplicate configuration identities, fake and unresolved attributes, missing types, malformed and DTD-bearing XML, an external config reference, remote WSDL/schema imports, a custom extension, and inert marker-writing code. |
| `WcfSharedSource` | One physical service-contract source file selected by two net472 projects, proving project-specific structural candidates remain explicit and configuration correlation degrades to `Ambiguous` instead of choosing a project. |

## Marker safety

Never run `dotnet build`, `msbuild`, restore, or any repository-defined target
inside `MaliciousBuild`. The original target still writes
`MaliciousBuild/SHOULD_NOT_EXIST.marker` if it executes. Additional hostile
hooks and the script use the literal `__DOMAINLENS_EXTERNAL_MARKER__`
placeholder. Tests should copy the fixture, replace that placeholder with a
test-owned path outside the copied repository, and assert that neither marker
exists after analysis. Their absence demonstrates that repository build logic
and scripts stayed inert.

`tests/DomainLens.RepositoryPayload` is trusted DomainLens test code, not an
analyzed-repository project. It builds a controlled assembly containing both a
Roslyn analyzer and source generator. Their constructors read
`DomainLens.RepositoryPayload.marker-path.txt` next to the assembly and append
to the configured marker. Static initialization remains side-effect free;
building the payload assembly does not instantiate either type and therefore
must not create a marker. A safety test may copy the
assembly and sidecar into the copied fixture's `RepositoryPayload` directory,
replace the sidecar placeholder with its external marker path, analyze the
fixture, and assert that the marker remains absent. This proves the analyzed
repository's declared analyzers and generators were neither instantiated nor run.

The `WcfAmbiguousHostile` fixture applies the same rule to classic WCF inputs.
Neither its repository project target nor its custom behavior-extension
constructor may execute. The `.config`, `.wsdl`, type strings, and remote URLs
are inert repository data: tests must never fetch them, load the fixture
assembly, or invoke WCF configuration APIs that instantiate extensions.
