# Milestone 0 Deployment, Security, and Analyzer-Isolation Feasibility

## Purpose and evidence status

Milestone 0 is an executable feasibility spike for the Windows-oriented analyzer boundary needed
by DomainLens Product V1. It tests whether the existing deterministic scanner and a narrow legacy
.NET semantic-enrichment path can run in a separate child process without evaluating or building
the analyzed repository.

This report is **spike evidence, not production readiness**. It does not select an Azure service,
provide an operating-system containment boundary, prove network denial, or authorize hosted
analysis of arbitrary public repositories.

The status terms in this report are deliberately narrow:

| Status | Meaning |
|---|---|
| **PROVEN** | The checked-in implementation and a named automated test demonstrate the stated behavior in the tested Windows development environment. |
| **FEASIBLE WITH CONSTRAINTS** | The spike demonstrates a viable mechanism, but production controls, supported scope, or operational evidence are still required. |
| **NOT PROVEN** | The spike does not establish the property; absence of an observed failure is not evidence of the control. |
| **REJECTED** | The mechanism was considered incompatible with the V1 default trust boundary. |
| **PROHIBITED** | Product V1 analysis must not perform the action. |
| **OPEN** | A product, mechanism, limit, or policy still requires an explicit decision. |

## Executive outcome

| Question | Outcome | Evidence and limit |
|---|---|---|
| Can deterministic analysis run in another process? | **PROVEN** | `RealScannerRunsInDifferentProcessAndReturnsValidatedGraph` verifies a different worker PID and a validated Evidence Graph. This is process separation, not OS identity or kernel isolation. |
| Can a worker crash be contained as a typed attempt failure? | **PROVEN** | `WorkerCrashIsContainedAndWorkspaceIsCleaned` verifies that a child exit does not crash the test host and that cleanup completes. |
| Can a deadline or caller cancellation force rejection? | **PROVEN within the tested scope** | `WallClockTimeoutForcesRejectionAndCleanupAfterDelayedOutput` and `CallerCancellationKillsWorkerAndRejectsAnyOutput` verify rejection during worker lifetime and no acceptance after either control becomes terminal. Synchronous trusted validation is not independently preempted. |
| Can the tested descendant process be terminated? | **PROVEN for the fixture; FEASIBLE WITH CONSTRAINTS generally** | `TimeoutTerminatesDescendantProcessTree` verifies a child exits when the still-running worker parent is tree-killed and reaped. Detached descendants after parent exit are **NOT PROVEN**. |
| Can repository content be staged without following reparse points? | **PROVEN for implemented checks** | Staging rejects a reparse-point root or entry; bounds files, total entries, relative depth, and bytes; omits analyzer-excluded source trees without traversing or copying their contents; and uses a unique per-job workspace. Race-free filesystem confinement is **NOT PROVEN**. |
| Can caller environment secrets be withheld? | **PROVEN for environment inheritance** | `WorkerDoesNotInheritCallerSecretEnvironmentVariable` verifies the sentinel is absent, `PATH` is absent, and CWD/`TEMP`/`TMP` are job-scoped. A different OS identity is **NOT PROVEN**. |
| Can worker output be accepted through deterministic gates? | **PROVEN for the spike contracts** | The host bounds output, correlates protocol/job/PID, binds the graph snapshot ID to the staged manifest, rechecks the staged tree after worker exit, checks JSON hashes, deserializes strictly, and applies trusted result validators. This is integrity validation, not producer authentication. |
| Can .NET Framework 4.7.2 symbols be bound without repository build evaluation? | **PROVEN for the checked-in net472 slice** | The semantic analyzer uses one flattened compilation of repository-manifest C# sources plus the exact tool-owned reference catalog. The declared compilation and source-binding resolution is `Partial`; repository MSBuild is not evaluated. |
| Is outbound network access denied by the operating system? | **NOT PROVEN** | The spike performs no intentional analyzer network operation, but it installs no firewall, network namespace, or equivalent deny policy. |
| Is the worker a production containment boundary? | **NOT PROVEN** | The child currently runs under the host account's OS security context and has no CPU, memory, process-count, or general host-filesystem enforcement. |
| Which Azure compute service hosts the worker? | **OPEN** | Milestone 0 selects no Azure service, SKU, container mode, VM topology, queue, or orchestrator. |

## Executable slice

The spike introduces five runtime/test roles:

| Project | Spike responsibility |
|---|---|
| `DomainLens.Analyzer.Protocol` | Neutral, dependency-light wire contracts and strict serialization for the versioned job/result exchange. |
| `DomainLens.Analyzer.Host` | Trusted launcher, bounded staging, protocol consumption, deadline/cancellation, process termination/reaping, result gates, and typed cleanup outcome. |
| `DomainLens.Analyzer.Worker` | Child entry point that invokes the scanner and legacy semantic analyzer against the staged repository and atomically writes a result envelope. |
| `DomainLens.Semantics` | Narrow compiler-backed symbol enrichment over one flattened set of manifest-verified C# sources and the exact tool-owned .NET Framework 4.7.2 reference catalog, with declared `Partial` resolution. |
| `DomainLens.Analyzer.TestWorker` | Trusted fault-injection executable for crash, hang, delayed result, malformed/tampered result, environment, output-bound, and descendant-process tests. |

The host and worker communicate through `domainlens.analyzer-process.v1`, with both depending on
the neutral `DomainLens.Analyzer.Protocol` project: `Host -> Protocol <- Worker`. The worker has no
project reference to the host, and host-only process, staging, validation, and cleanup types remain
in `DomainLens.Analyzer.Host`. A generated job ID and the launched PID correlate a result to one
attempt. Process terminal outcome is a separate type
from the Evidence Graph's `AnalysisStatus`; `ValidFailureDocumentIsDistinctFromProcessFailure`
demonstrates that a trustworthy `AnalysisStatus.Failure` document can cross a successful process
and protocol exchange.

Absolute worker executable and managed-entry-point paths are trusted configuration and must exist
outside the per-job workspace. `ProcessStartInfo.UseShellExecute` is `false`, and arguments are
added through `ArgumentList` rather than shell composition. The result envelope is written through
a same-directory temporary file and atomic rename under the tested filesystem semantics.

## What the child-process boundary proves

### Process, deadline, and cleanup

**PROVEN:** each attempt receives a fresh `job-{id}` directory, a staged `repository` child, and a
job-scoped `temp` child. `StagingBoundsRejectInputBeforeAWorkerStarts`,
`FileCountBoundRejectsInputBeforeAWorkerStarts`,
`AggregateByteBoundRejectsInputBeforeAWorkerStarts`,
`EmptyDirectoriesCountTowardTheTotalStagingEntryBound`, and
`RelativeDirectoryDepthIsBoundedBeforeAWorkerStarts` cover the file-size, file-count, aggregate-
byte, total-filesystem-entry, and relative-depth gates before launch.
`WideDirectoryIsRejectedBeforeUnboundedMaterializationOrWorkerStart` additionally proves that the
total-entry bound is applied before a wide directory is fully materialized for sorting.
`RepositoryReparsePointRootIsRejectedBeforeWorkerStarts` and
`NestedRepositoryReparsePointIsRejectedBeforeWorkerStarts` cover root and nested-entry reparse
rejection. `EveryJobUsesADistinctWorkspace` covers per-job uniqueness. Stdout/stderr retention
and result-file byte reads are separately capped.

**PROVEN:** when initial staging encounters an analyzer-excluded directory name (`.git`, `.hg`,
`.svn`, `.vs`, `.idea`, `bin`, `obj`, `node_modules`, `packages`, or `TestResults`), it still
performs the directory-entry reparse and depth checks but does not create the directory in the
staged repository, recurse into it, copy or hash its contents, or charge its descendants against
file, byte, or entry limits. `LargeAnalyzerExcludedDirectoryTreeIsAbsentFromStagedRepository` and
`AnalyzerExcludedContentsDoNotConsumeFileOrByteLimits` exercise this rule. The resulting staged
manifest and snapshot identity match the scanner's exclusion behavior, as verified by
`StagedSnapshotIdentityMatchesTheScannerOutputWhenExclusionsExist`.

**PROVEN:** before accepting an envelope, the host recaptures the entire staged repository tree and
compares its path/kind/length/hash fingerprint with the state captured during staging.
`WorkerCreatedAnalyzerExcludedTreeRejectsOtherwiseValidOutput` adds an excluded
`obj/project.assets.json` after analysis and proves that even a change outside the analyzer-visible
manifest rejects the output. The graph's snapshot ID must also equal the snapshot ID derived by the
trusted staging pass; the `wrong-snapshot` case of
`UntrustedResultEnvelopeMustPassEveryTrustedGate` proves rejection on mismatch. This is a pre/post
comparison, not a read-only filesystem boundary; a transient change restored before recapture is
not proven detectable.

Workspace cleanup runs from a `finally` path and reports `Succeeded`, `Failed`, or `NotRequired`;
a cleanup failure prevents an otherwise successful result from being accepted. Cleanup uses a
bounded iterative traversal, and cleanup assertions exercise it across normal, crash, timeout,
cancellation, and mutation outcomes. Cleanup-failure injection, adversarial cleanup races, and
maximum-depth stress are **NOT PROVEN**.

**PROVEN:** the host maintains a deadline cancellation source in addition to caller cancellation.
It applies during staging, worker lifetime, bounded result reading, and asynchronous post-run staged-
tree verification. If either control is terminal before or by the end of trusted result validation,
the result is rejected. During a still-running worker, the host calls
`Process.Kill(entireProcessTree: true)`, waits for the worker process to exit, and then cleans the
workspace.
[The .NET API documentation](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.kill)
confirms that tree termination is optional and forced, but also notes that process-exit observation
does not prove every descendant has exited and that descendants hidden by insufficient permission
may be skipped. The passing fixture covers a descendant while its worker parent remains alive.
Containment or discovery of a detached descendant after its parent has already exited is **NOT
PROVEN**.

**NOT PROVEN:** independent preemption of synchronous trusted deserialization, hashing, graph/
semantic validation, result construction, process reaping, or cleanup. A deadline that becomes
terminal during synchronous validation prevents acceptance when validation returns, but it cannot
interrupt that code. Consequently, the configured value bounds worker acceptance semantics; it is
not a hard upper bound on total `RunAsync` latency.

**NOT PROVEN:** CPU, memory, process-count, handle, aggregate worker-written disk, or kernel-object
quotas. Cleanup is ordinary deletion, not cryptographic erasure. Recovery of abandoned workspaces
after host or machine failure, diagnostic retention, storage encryption, and cleanup telemetry are
production gaps. The result-byte cap is not a hard memory ceiling: byte buffers, decoded strings,
hash inputs, and deserialized object graphs can amplify allocation. On Windows, the result is
opened by handle without following reparse points and is rejected if it is a directory, reparse
point, or multiply linked file; `ReparsePointResultArtifactIsRejectedBeforeItIsRead` exercises a
directory junction and `HardLinkedResultArtifactIsRejectedBeforeItIsRead` exercises the independent
single-link gate. A hard deadline around the synchronous open, equivalent no-follow semantics
on future non-Windows workers, and protection from every filesystem/device race remain **NOT
PROVEN**.

### Environment is not identity isolation

**PROVEN:** the worker does not inherit the caller's environment wholesale. The host clears it,
then supplies only fixed runtime values plus the Windows directory values needed by the runtime;
CWD, `TEMP`, `TMP`, and `DOTNET_CLI_HOME` point inside the job workspace. The named sentinel secret
and `PATH` are absent in the test worker.

**NOT PROVEN:** a least-privileged or different Windows identity. Clearing environment variables
does not change the access token, filesystem ACLs, registry access, network access, inherited OS
rights, or access to resources available to the host account. Windows
[restricted tokens](https://learn.microsoft.com/en-us/windows/win32/secauthz/restricted-tokens)
can remove privileges, mark SIDs deny-only, and add restricting SIDs, but the spike does not create
or test one.

### Output is untrusted until gated

**PROVEN:** the trusted host rejects oversized or malformed envelopes (including a missing required
terminal outcome), protocol mismatch, job-ID
mismatch, PID mismatch, analysis-hash mismatch, malformed analysis JSON, and invalid Evidence
Graphs. `UntrustedResultEnvelopeMustPassEveryTrustedGate` exercises the malformed, tampered, and
correlation cases; `ExcessWorkerOutputIsDrainedButRejectedAtTheBound` verifies bounded capture.

Evidence Graph JSON must pass both its envelope SHA-256 check and `AnalysisGraphValidator`, and its
snapshot ID must match the trusted staging snapshot. A
completed envelope must also contain semantic JSON and its SHA-256 digest. The trusted host
strict-deserializes it against the accepted manifest and applies `LegacySemanticAnalysisValidator`;
the theory cases `semantic-missing`, `semantic-hash-mismatch`, `semantic-malformed`, and
`semantic-invalid` prove typed rejection. The semantic artifact remains a separate deterministic
feasibility result; the spike does not silently project those observations into the Evidence Graph.
Content hashes provide integrity and correlation, not digital signature, worker attestation, or
authorization.

## Semantic enrichment without repository build execution

### Fixed trusted reference strategy

`DomainLens.Semantics` references
[`Microsoft.NETFramework.ReferenceAssemblies.net472` 1.0.3](https://www.nuget.org/packages/Microsoft.NETFramework.ReferenceAssemblies.net472/)
as a private build dependency of DomainLens itself. DLLs from the package's reference root and
`Facades` directory are copied into a fixed `trusted-reference-assemblies/net472` subtree beside
the deployed analyzer. Before any file becomes a compiler input, runtime code verifies a fixed
SHA-256 commitment over the complete sorted package DLL set (relative path, byte length, and
per-file SHA-256). Missing, extra, stale, or modified files invalidate the whole catalog. The
runtime then admits the known compiler-readable metadata files and excludes two pinned native or
mixed-mode files. The result names the reference set
`microsoft.netframework.referenceassemblies.net472/1.0.3` and exposes only tool-relative assembly/
path descriptors, not absolute installation paths.

The reference-set ID alone is not the gate. The trusted validator independently reconstructs the
tool-owned descriptor catalog and requires the result to contain that exact set: no missing or
additional path, mismatched assembly identity, or resolved external assembly is accepted.
`Metadata_inputs_are_fixed_tool_owned_reference_identities_and_repository_build_is_inert` compares
the emitted descriptors with `TrustedNet472ReferenceCatalog.GetReferenceDescriptors`, while
`Validator_rejects_forged_reference_descriptors_and_resolved_assemblies` covers forged catalog and
resolved-assembly claims. `Pinned_reference_catalog_rejects_extra_missing_and_corrupt_dlls`
exercises the deployed-catalog content commitment. “Exact” means both the pinned package file
content/set and the assembly-simple-name/relative-path descriptor set reconstructed by trusted
code. The commitment detects local drift; it is not a digital signature or independent attestation
of the DomainLens deployment, so production supply-chain and tool-directory integrity remain
required.

This distinction is essential:

- restoring and building **DomainLens** supplies trusted analyzer assets;
- analyzing a repository reads manifest-verified `.cs` bytes from the staged snapshot;
- repository-declared package, analyzer, generator, binary, import, target, and task inputs remain
  untrusted data and are not admitted as compiler inputs; and
- the implemented workflow has no repository package-restore, project-build, repository-assembly-
  load, generator/analyzer execution, or endpoint/database-access path. Its bounded loaded-assembly
  evidence is described below; this is not general process attestation.

### Roslyn boundary

**PROVEN for the fixture:** `LegacySemanticAnalyzer` constructs syntax trees for every `.cs` entry
in the repository manifest and flattens them into one synthetic `CSharpCompilation`, irrespective
of project membership or project-reference, target-framework, conditional-item, or preprocessor
configuration. The result therefore declares
`SemanticCompilationScope.RepositoryManifestCSharpSources` and sets
`CompilationResolutionQuality` to `ResolutionQuality.Partial`; source-to-source bindings are
`Partial`, even when Roslyn
finds a unique symbol in that synthetic scope. The trusted validator rejects any synthetic-source
binding forged as `Exact`. The analyzer obtains `SemanticModel` instances for
declarations, base/interface types, attributes, invocations, and member access. Before parsing, it
opens each selected manifest source as a seekable stream, checks its length before allocation,
allocates and reads at most the captured manifest length in bounded chunks, computes SHA-256
incrementally, and rechecks the open handle's position and length after the exact read. Shorter,
larger, same-length-but-changed, or concurrently grown content is rejected before decoding and
parsing; cancellation remains observable during the read. Missing types remain
explicit diagnostics or unresolved/partial bindings instead of triggering repository dependency
resolution. No compilation is emitted.

The semantic tests provide the following direct evidence:

- `Net472_compilation_binds_framework_and_local_symbols_without_hiding_missing_types`;
- `Metadata_inputs_are_fixed_tool_owned_reference_identities_and_repository_build_is_inert`;
- `Repository_wide_synthetic_bindings_are_explicitly_partial`;
- `Only_manifest_sources_with_their_captured_hash_are_analyzed`;
- `Source_larger_than_manifest_expectation_is_rejected_as_a_length_mismatch`;
- `Source_shorter_than_manifest_expectation_is_rejected_as_a_length_mismatch`;
- `Same_length_source_change_is_rejected_as_a_hash_mismatch`;
- `Obvious_length_drift_is_rejected_without_consuming_source_bytes`;
- `Growth_during_read_is_rejected_without_consuming_beyond_the_captured_bound`;
- `Cancellation_during_bounded_source_read_is_propagated`;
- `Valid_manifest_source_still_analyzes_after_bounded_verification`;
- `Pre_cancelled_analysis_stops_before_source_or_compiler_work`;
- `Strict_json_round_trip_is_deterministic_camel_case_and_path_free`;
- `Validator_rejects_forged_reference_descriptors_and_resolved_assemblies`;
- `Pinned_reference_catalog_rejects_extra_missing_and_corrupt_dlls`;
- `Strict_json_rejects_unknown_members_numeric_enums_and_wrong_property_case`;
- `Validator_rejects_unbound_or_unsafe_paths_invalid_spans_enums_and_reference_identities`; and
- `Manifest_aware_deserialization_rejects_a_structurally_safe_but_unbound_path`.

**FEASIBLE WITH CONSTRAINTS:** this establishes a narrow .NET Framework 4.7.2 symbol-enrichment
mechanism. It does not establish effective project configuration, conditional compilation,
generated code, repository package types, full framework/version coverage, a call graph, control
flow, data flow, business behavior, WCF configuration semantics, or Domain Knowledge findings.

### Build-system decision

| Mechanism | Milestone 0/V1 position | Reason |
|---|---|---|
| Repository-controlled MSBuild or `MSBuildWorkspace` evaluation as the default V1 analysis path | **REJECTED** | Imports, tasks, build events, SDK resolution, analyzers, generators, and restore hooks are repository-controlled execution surfaces. |
| Build or restore the analyzed repository | **PROHIBITED** | Analysis permission does not grant execution, package acquisition, script, target, generator, or analyzer authority. |
| Parse project/XML and source as data | **PROVEN** | Existing scanner tests retain literal configuration and explicit uncertainty without evaluation. |
| Construct one flattened in-memory Roslyn compilation without emit, using the exact tool-owned reference catalog | **FEASIBLE WITH CONSTRAINTS** | The net472 fixture binds supported symbols with declared `Partial` source resolution while repository binaries and build logic remain excluded. Every additional reference profile requires explicit trusted packaging, versioning, tests, and coverage diagnostics. |

`Legacy_project_is_read_declaratively_without_MSBuild_evaluation`,
`Unevaluated_repository_wide_source_configuration_is_explicitly_partial`, and
`Repository_controlled_build_target_remains_inert` preserve the declarative scanner boundary.
`RealWorkerKeepsRepositoryControlledBuildAndCompilerPayloadsInert` extends the check across the
real child worker: repository `Exec`, imported targets, build events, script, inline task, and
generator/analyzer constructor probes leave all external and in-repository markers absent. The
repository payload DLL remains visible in the captured manifest but is absent from the exact trusted
metadata-reference catalog and from resolved-assembly observations. In addition, the production
worker enumerates the assemblies currently loaded in its `AppDomain` after analysis and rejects the
attempt if any non-dynamic assembly location is under the staged repository; successful completion
of this real-worker test passes that assertion. This is a point-in-time managed loaded-assembly
check, not general OS process or native-module attestation.

The fixture also contains repository-controlled NuGet/package-source configuration. Because the
worker has no repository restore or package-resolution path, that configuration is treated as data.
The test does not instrument DNS or outbound connections, so package-source nonuse is workflow
evidence only and does not prove OS-enforced network denial.

## Windows mechanism categories

Milestone 0 compares categories; it does not select a production mechanism or an Azure host.

| Category | What it could add | Compatibility/cost questions | Milestone 0 status |
|---|---|---|---|
| Plain child process with bounded protocol | Crash separation, explicit lifetime, independent deadline, output gate, simple local debugging. | Same account, host filesystem and network authority; no kernel/resource boundary by itself. | **PROVEN as a feasibility baseline; NOT sufficient for hosted hostile input.** |
| Job object plus restricted/low-privilege identity and ACL boundary | Kernel resource/process accounting and reduced access to securable objects; a fixed workspace/tool ACL model. | Correct token construction, process assignment, desktop/window station, child escape prevention, service account lifecycle, filesystem layout, and network policy need implementation and adversarial tests. | **OPEN; FEASIBLE category, not implemented.** |
| Windows process-isolated container | Filesystem, registry, network-port, PID/thread, and object namespaces with resource controls while sharing the host kernel. | Host/image version compatibility, legacy reference assets, startup time, patching, image supply chain, and actual network/volume policy. | **OPEN; NOT PROVEN.** |
| Hyper-V-isolated Windows container or disposable VM | A dedicated kernel and hardware-backed boundary between workload and host. Microsoft describes Hyper-V isolation as running each container in an optimized VM with its own kernel. | Higher startup/compute/operations cost; image compatibility, nested virtualization, throughput, diagnostics, and Azure placement must be measured. | **OPEN; NOT PROVEN.** |

Microsoft documents the distinction between shared-kernel Windows
[process isolation and Hyper-V isolation](https://learn.microsoft.com/en-us/virtualization/windowscontainers/manage-containers/hyperv-container).
The architecture decision must be based on threat model, compatibility, operational complexity,
and measured workload behavior—not on the existence of the child-process prototype.

## Security and deployment conclusions

| Area | Conclusion |
|---|---|
| Repository execution | **PROHIBITED:** no repository build, restore, targets, tasks, analyzers, generators, binaries, scripts, or custom tools. |
| Process feasibility | **PROVEN for the tested topology:** a trusted host can stage input, invoke the allowlisted worker, reject bad output, terminate and reap the still-running worker plus its test descendant, and clean up. Detached/post-parent descendants are **NOT PROVEN**. |
| Hosted isolation | **NOT PROVEN:** the prototype is not the final hostile-workload boundary. |
| Worker identity | **NOT PROVEN:** environment minimization is implemented; a non-admin/restricted/disposable OS identity is not. |
| Network | **NOT PROVEN:** no OS-enforced egress denial or no-egress test exists. |
| Filesystem | **FEASIBLE WITH CONSTRAINTS:** bounded staging, initial reparse rejection, omission of source analyzer-excluded trees without traversing their contents, job uniqueness, exhaustive post-run staged-repository comparison (including worker-created excluded names), bounded iterative cleanup, and Windows no-follow/single-link result-handle validation work; atomic/race-safe directory traversal, a cancellable result open, general device handling, ACL confinement, encrypted scratch storage, secure disposal, and crash scavenging remain. |
| Resource governance | **FEASIBLE WITH CONSTRAINTS:** file/entry/depth/byte/output/result-file and worker-acceptance deadline limits exist; hard memory/allocation bounds for result decoding/validation, CPU, process, handle, complete disk quotas, and independent synchronous trusted-postprocessing preemption remain. |
| Result trust | **FEASIBLE WITH CONSTRAINTS:** deterministic integrity/structure gates exist; authenticated transport, artifact signing/attestation, durable replay protection, and schema migration remain. |
| Legacy symbol enrichment | **FEASIBLE WITH CONSTRAINTS:** the exact net472 catalog and `SemanticModel` work over one flattened manifest-source compilation with declared `Partial` resolution and no repository build execution; project-faithful compilation, supported framework profiles, and projection into evidence remain open. |
| Git/SSRF intake | **NOT PROVEN:** the spike starts from an authorized local directory and does not implement public Git retrieval. |
| Azure deployment | **OPEN:** no compute, dispatch, storage, network, identity, secrets, telemetry, region, or SKU selection was made. |

## Production gaps before hosted analysis

At minimum, Product V1 still needs:

1. an approved Windows containment mechanism and threat model;
2. a non-administrative, job-scoped identity and tested filesystem/tool ACLs;
3. OS/platform CPU, memory, process, handle, disk, and wall-clock enforcement;
4. deny-by-default network enforcement with a no-egress verification suite;
5. safe public Git URL/ref/redirect/DNS/submodule/LFS intake and immutable snapshot acquisition;
6. authenticated dispatch/result transport, replay protection, worker artifact provenance, and
   durable attempt state;
7. workspace encryption, crash scavenging, retention policy, deletion verification, and redacted
   operational telemetry;
8. adversarial symlink/junction/race, special-file, archive/resource, and process-escape tests on
   every supported worker OS/image;
9. approved production limits, concurrency, cancellation/termination guarantees, and operational
   failure procedures; and
10. explicit analyzer/reference profiles, evidence projection rules, compatibility tests, and
    coverage diagnostics for every supported legacy framework variant.

No item in this list implies AKS, containers, VMs, a queue product, or any other Azure service.
Those remain architecture decisions to be made after containment and workload requirements are
measured.

## Evidence index

| Evidence source | What it supports |
|---|---|
| [`AnalyzerProcessHostTests`](../tests/DomainLens.Analyzer.Tests/AnalyzerProcessHostTests.cs) | Process separation, crash/timeout/cancellation, `WideDirectoryIsRejectedBeforeUnboundedMaterializationOrWorkerStart`, file/entry/depth limits, initial exclusion-tree omission and limit behavior, scanner/staging snapshot parity, snapshot binding, post-run staged-repository-tree verification including worker-created excluded names, Windows result-junction and hard-link rejection, environment stripping, strict result gates, output limits, distinct workspaces, bounded cleanup, and still-running-parent tree termination. |
| [`AnalyzerProtocolTests`](../tests/DomainLens.Analyzer.Tests/AnalyzerProtocolTests.cs) | Strict job/result wire round trips and rejection behavior, plus the neutral protocol assembly's lack of DomainLens project dependencies. |
| [`HostileRepositoryEndToEndTests`](../tests/DomainLens.Analyzer.Tests/HostileRepositoryEndToEndTests.cs) | Real-worker proof that repository build/compiler payloads remain inert, repository payload metadata is excluded from the trusted catalog and resolved assemblies, and the worker's loaded-assembly assertion passes. |
| [`LegacySemanticAnalyzerTests`](../tests/DomainLens.Semantics.Tests/LegacySemanticAnalyzerTests.cs) | Exact net472 catalog, flattened manifest-source/partial compilation semantics, compiler binding, bounded seekable source reads with exact pre/post length and incremental-hash verification, manifest/source integrity, strict semantic JSON, cancellation, and semantic-result validation. |
| [`ScannerAcceptanceTests`](../tests/DomainLens.Scanner.Tests/ScannerAcceptanceTests.cs) | Declarative project reading, explicit partial coverage for unevaluated build configuration, and inert repository build targets. |
| [Runtime Architecture](architecture/03-runtime-architecture.md) | Product runtime boundary and lifecycle requirements. |
| [Security Architecture](architecture/09-security-architecture.md) | Threat model and defense-in-depth requirements. |
| [Deployment Architecture](architecture/10-deployment-architecture.md) | Conceptual deployment roles and still-open Azure choices. |
