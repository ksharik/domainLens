# Milestone 1 scanner fixtures

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
| `MaliciousBuild` | One SDK-style project containing a repository-controlled `Target` and `Exec`. The target would create `SHOULD_NOT_EXIST.marker` if executed. A safe scanner reads the XML only; the marker must not exist before or after analysis. |
| `MsBuildPartial` | One SDK-style project under an unevaluated `Directory.Build.props`, proving source membership remains explicit partial evidence. |

## Marker safety

Never run `dotnet build`, `msbuild`, restore, or any repository-defined target
inside `MaliciousBuild`. Automated tests should delete no marker and should
simply assert that `MaliciousBuild/SHOULD_NOT_EXIST.marker` remains absent after
inventory and source analysis. Its absence demonstrates that repository build
logic stayed inert.
