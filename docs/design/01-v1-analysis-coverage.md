# V1 Analysis Coverage

## Purpose and scope

This document defines the intended source-artifact coverage of the configured
legacy C#/.NET Framework/classic WCF path for Product V1. It turns broad product
requirements into an explicit support posture so that DomainLens does not imply
complete business-rule, workflow, persistence, or security recovery while
ignoring artifacts in which that knowledge may reside.

This is a target coverage design, not a statement that every row is implemented
today. The current implementation is the Milestone 1 Repository Structure
Scanner described in
[Repository Structure Scanner 0.1](../11-milestone-1-repository-scanner.md).
All entries marked **Planned V1** still require versioned extraction rules,
fixtures, provenance tests, and coverage diagnostics before they are supported.

Product V1 runs a configured, approved .NET/WCF analyzer set. It does not use
generalized automatic technology discovery or generate an Analysis Plan; those
capabilities remain future work. A bounded deterministic qualification check may
reject input outside the configured path or identify partial coverage.

## Treatment vocabulary

| Treatment | Meaning |
|---|---|
| **V1 Supported** | Product V1 intends deterministic extraction of the artifact's listed evidence for explicitly recognized forms. This does not mean every language, framework, configuration, or runtime variant is understood. Unsupported forms still reduce coverage. |
| **V1 Partial** | Product V1 intends a bounded, documented subset. Unhandled constructs must be detected where practical and reported as partial, ambiguous, or unresolved rather than guessed. |
| **V1 Metadata/Detection Only** | V1 may inventory the artifact, record safe literal metadata, or detect that a construct exists, but does not interpret its full semantics. Presence may affect coverage and retrieval; it cannot support conclusions that require a missing parser. |
| **Deferred** | Artifact-specific semantic analysis is outside Product V1. Generic manifest inventory may still record the file. |
| **Open Decision** | The baseline does not yet authorize a V1 semantic-coverage commitment. At minimum, the artifact must not be silently ignored when it creates a known coverage limitation. |

These treatments describe deterministic analyzer coverage, not the strength of
a semantic finding. A supported parser can still produce partial, ambiguous, or
unresolved relationships, and a metadata-only artifact cannot be promoted to
semantic evidence by a model.

## Deterministic ownership

The owner names below are logical capabilities within the configured V1
analyzer set; they do not establish a plug-in API or independently deployable
service.

| Owner | V1 responsibility |
|---|---|
| Snapshot and inventory | Capture immutable repository bytes, paths, sizes and hashes; classify files without executing them. |
| Repository/C# structural analyzer | Read safe solution/project metadata and C# syntax; emit declarations and statically resolvable source relationships. |
| Classic WCF analyzer | Recognize WCF source and configuration constructs and contribute normalized contract, operation, endpoint, binding, behavior and hosting evidence. |
| Relationship / persistence / behavioral evidence | Extract bounded calls, conditions, validation, mutations, data access, transactions, security checks, external calls and other supported behavioral evidence from C# and configuration patterns. This is one configured V1 capability boundary, not a separate security analyzer or the future standalone database/persistence analyzer family. |
| Evidence Kernel | Validate identity, provenance, source spans, hashes, resolution quality, graph references and diagnostics before contributions enter the Evidence Graph. |

## Repository, build, source and configuration artifacts

| Artifact or concept | V1 treatment | Delivery state | Evidence contribution | Expected deterministic owner | Major limitations and security constraints |
|---|---|---|---|---|---|
| `.sln` | **V1 Supported** | Current foundation | Solution identity, project membership and solution-relative project paths. | Repository/C# structural analyzer | No build or solution evaluation. Malformed, missing, duplicate or external project paths produce diagnostics. |
| `.csproj` | **V1 Supported** | Current foundation | Project identity, target-framework literals, assembly/root namespace, literal compile items, project/assembly/package references and declared dependency edges. | Repository/C# structural analyzer | MSBuild properties, imports, conditions, globs, tasks and item transforms are not executed or fully evaluated. Resulting uncertainty must reduce coverage. |
| `.cs` declarations and syntax-resolvable structure | **V1 Supported** | Current structural foundation | Namespaces, declarations, members, attributes, inheritance/interface declarations and supported source dependencies. | Repository/C# structural analyzer | Source is parsed as untrusted data. External binding, aliases, conditional compilation and generated members may remain partial or unresolved. DDD-like names are not proof of DDD roles. |
| `.cs` method-body and behavioral evidence | **V1 Partial** | Planned V1; exact extraction contract open | Supported calls, mutations, conditions, validation, exceptions, transactions, security checks, state changes and side effects. | Relationship / persistence / behavioral evidence | V1 must define supported constructs and control/data-flow depth. External code, indirection, reflection and dynamic dispatch may remain partial or unresolved; unsupported behavior must reduce coverage rather than be guessed. |
| `.props` | **V1 Metadata/Detection Only** | Current file-presence and applicable build-risk diagnostics; broader construct detection planned | Current: inventory/provenance and applicable build-file diagnostics. Planned: safe literal indicators that build-wide configuration may affect projects. | Snapshot and inventory; Repository/C# structural analyzer | Never import or evaluate the file. Properties, conditions, tasks and item mutations can change effective source membership, references or settings, so affected projects require a coverage diagnostic. |
| `.targets` | **V1 Metadata/Detection Only** | Current file-presence and applicable build-risk diagnostics; broader construct detection planned | Current: inventory/provenance and applicable build-file diagnostics. Planned: detection of target, task, build-event or structural-item mutation constructs. | Snapshot and inventory; Repository/C# structural analyzer | Targets, `UsingTask`, `Exec`, generators and custom tasks remain inert data. Detection does not establish their runtime effect. |
| Generic `.config` | **V1 Partial** | Planned V1 | Safe XML structure and allowlisted literal configuration relevant to dependencies, persistence, security or external systems. | Classic WCF analyzer and Relationship / persistence / behavioral evidence | No config execution, type activation, secret use, remote lookup or unrestricted value egress. Effective values may depend on transforms, external files, inheritance or machine-level configuration. |
| `web.config` | **V1 Partial** | Planned V1 | WCF hosting/service activation, `system.serviceModel`, supported authentication/authorization indicators, connection/dependency declarations and configuration-driven behavior. | Classic WCF analyzer and Relationship / persistence / behavioral evidence | It is not proof of deployed IIS state. Inheritance, locked sections, transforms, custom providers/modules and machine-level settings may prevent exact effective resolution. Secrets and personal data require redaction policy before persistence or model egress. |
| `app.config` | **V1 Partial** | Planned V1 | WCF client/service configuration, supported endpoint/binding/behavior declarations, connection/dependency indicators and configuration-driven behavior. | Classic WCF analyzer and Relationship / persistence / behavioral evidence | Build-time renaming and transforms, external `configSource` files, environment substitution and machine-level configuration may make the effective runtime configuration unknown. |
| Configuration transforms | **V1 Metadata/Detection Only** | Planned V1 detection | Presence, base-file relationship when statically identifiable and an explicit indication that effective configuration may vary by environment. | Snapshot and inventory; configuration portions of the WCF/relationship capabilities | Transforms are not executed. Ordering, selected environment and custom transform semantics remain unknown, so base configuration must not be reported as the sole effective runtime truth. |

## Classic WCF artifacts and concepts

| Artifact or concept | V1 treatment | Delivery state | Evidence contribution | Expected deterministic owner | Major limitations and security constraints |
|---|---|---|---|---|---|
| `.svc` | **V1 Partial** | Planned V1 | Static service directive metadata such as service, factory, code-behind and language values; candidate linkage to a service implementation and hosting declaration. | Classic WCF analyzer | No ASP.NET compilation or factory activation. Inherited directives, build substitution, custom factories and types not present in source may remain unresolved. |
| `ServiceContract` | **V1 Supported** | Planned V1 | Contract-bearing type, callback contract, configured name/namespace and other statically present attribute arguments. | Classic WCF analyzer over C# evidence | Attribute aliases, derived/custom attributes and externally defined contract types may be ambiguous without safe binding. A contract declaration is implementation evidence, not a business-capability conclusion. |
| `OperationContract` | **V1 Supported** | Planned V1 | Contract operation, method linkage and static action/reply, one-way or asynchronous metadata. | Classic WCF analyzer | Overloads, explicit interface implementations, inherited contracts and external symbols require conservative resolution. An operation is not automatically a command or use case. |
| `FaultContract` | **V1 Supported** | Planned V1 | Declared fault type and static action/name/namespace metadata linked to an operation. | Classic WCF analyzer | Does not prove the fault is thrown on every runtime path or enumerate undocumented exceptions. External fault types may be unresolved. |
| `DataContract` | **V1 Supported** | Planned V1 | Contract type, static contract name/namespace, inheritance and relationships to operations/messages. | Classic WCF analyzer | Serialization callbacks, known-type discovery, surrogates and external or generated types can reduce coverage. A data contract is not automatically a domain entity. |
| `DataMember` | **V1 Supported** | Planned V1 | Contract member and static name, order, required/default-emission metadata. | Classic WCF analyzer | Runtime versioning and serializer behavior are not inferred beyond supported declarations. A member name is not a business definition. |
| `MessageContract` and message members | **V1 Supported** | Planned V1 | Message shape, wrapper settings, headers and body members, with operation relationships. | Classic WCF analyzer | Custom serialization, inspectors, formatters and external types may alter runtime messages. Sensitive headers and payload examples must not be exposed without policy. |
| `ServiceHost` | **V1 Partial** | Planned V1 | Statically recognizable host construction, hosted service type and direct endpoint/configuration relationships. | Classic WCF analyzer plus Relationship / persistence / behavioral evidence | Custom host/factory logic, dependency injection, reflection and runtime endpoint addition can make the actual host graph partial. Code is never executed. |
| `ChannelFactory<T>` | **V1 Partial** | Planned V1 | Candidate client contract, construction site, configured endpoint name/address when literal and outbound call dependency. | Classic WCF analyzer plus Relationship / persistence / behavioral evidence | Wrappers, factories, runtime configuration, reflection and dynamically selected generic types may be unresolved. No endpoint is contacted. |
| `ClientBase<T>` | **V1 Partial** | Planned V1 | Proxy/client type, service contract, inherited operation surface and candidate external dependency. | Classic WCF analyzer | Inheritance chains in unavailable assemblies, custom wrappers and runtime endpoint selection limit exact resolution. |
| Generated WCF clients/proxies | **V1 Partial** | Planned V1 | Checked-in generated C# declarations, service-reference markers, contracts, proxy methods and external-service relationships. | Repository/C# structural analyzer plus Classic WCF analyzer | Generators are never run. Absent generated output is unavailable; duplicated client/server contracts require reconciliation. Generated names/comments do not establish business ownership or domain meaning. |
| `system.serviceModel` configuration | **V1 Partial** | Planned V1 | Static services, clients, endpoints, bindings, behaviors, extensions, hosting and supported security/diagnostic settings. | Classic WCF analyzer | V1 must define an allowlisted schema/rule set. External files, inherited defaults, custom extension types, transforms and machine configuration can prevent construction of the effective runtime model. |
| Endpoints | **V1 Partial** | Planned V1 | Static address, contract, binding, binding configuration, identity and service/client direction; relationship to operations and external systems where resolvable. | Classic WCF analyzer | Addresses can be relative, environment-specific or sensitive. Dynamic endpoint creation, discovery and configuration selection remain partial. No address is contacted. |
| Bindings | **V1 Partial** | Planned V1 | Binding kind and supported transport, encoding, message-size, timeout, credential and security-mode declarations. | Classic WCF analyzer | Custom bindings/extensions and inherited/default values may be only partly resolvable. Declared security settings are evidence, not proof of effective deployment security. |
| Behaviors | **V1 Partial** | Planned V1 | Supported service, endpoint and client behavior declarations, including candidate authorization, credentials, metadata and diagnostics policy. | Classic WCF analyzer | Custom behavior implementations and runtime registration require source linkage and may remain unresolved. Types are not instantiated. |
| Hosting declarations | **V1 Partial** | Planned V1 | Relationships among `.svc`, service activation, host factories, `ServiceHost`, IIS/ASP.NET configuration and service implementations. | Classic WCF analyzer | Static declarations cannot prove deployment topology, enabled sites, process identity, activation success or runtime overrides. |

## Persistence, schemas and integration artifacts

The configured Product V1 Relationship / persistence / behavioral evidence capability extracts
supported evidence from legacy C# and configuration. It must not be described
as the future standalone **database/persistence technology analyzer family**,
which remains deferred. This distinction creates known V1 coverage gaps where
behavior exists only in database-native artifacts.

| Artifact or concept | V1 treatment | Delivery state | Evidence contribution | Expected deterministic owner | Major limitations and security constraints |
|---|---|---|---|---|---|
| `.edmx` | **V1 Metadata/Detection Only** | Planned V1 detection; semantic analysis deferred | Presence, file identity, project linkage and a limitation indicating that an Entity Data Model may contain persistence structure unavailable to V1 reasoning. | Snapshot and inventory | V1 does not promise full conceptual/storage/mapping model parsing. External schema references are not fetched, and custom tooling is not invoked. |
| Entity Framework mappings | **V1 Partial** | Planned V1 | Common statically recognizable C# model/context, entity-set, mapping, query, save and transaction patterns; candidate entity/store access relationships. | Relationship / persistence / behavioral evidence | Supported EF versions and fluent/attribute patterns are an **Open Decision**. Conventions, interceptors, dynamic model building, external assemblies and `.edmx` mappings may hide effective behavior. |
| ADO.NET | **V1 Partial** | Planned V1 | Common connection, command, parameter, command-type, transaction and literal query/procedure invocation patterns; candidate store and side-effect relationships. | Relationship / persistence / behavioral evidence | Wrappers, provider abstractions, dynamic SQL, encrypted/external configuration and runtime-generated command text may be unresolved. Connection strings and SQL literals may contain sensitive data. Nothing is connected to or executed. |
| Repository/data-access classes | **V1 Partial** | Planned V1 | Data-access calls, reads/writes, mutations, transaction participation, called procedures/tables when statically established and dependencies on domain/application types. | Relationship / persistence / behavioral evidence | Naming or interface shape alone does not prove the DDD Repository pattern or data ownership. Indirection, ORMs, reflection and external implementations may reduce coverage. |
| SQL files | **V1 Metadata/Detection Only** | Planned V1 detection; semantic parsing deferred | File presence, provenance and an explicit limitation that schema, behavior or rules may exist outside analyzed C#. | Snapshot and inventory | No V1 SQL-dialect parser is committed. SQL includes/imports are not followed, scripts are never executed, and credentials or production data in scripts require sensitive-evidence handling. |
| Stored-procedure definitions in the repository | **Deferred** | Post-V1 database/persistence analyzer family | Generic file inventory may reveal candidate definitions; C# invocation evidence may record a literal procedure name. V1 does not recover procedure-body conditions, branches or mutations as Observed evidence that could support business-rule findings. | Future database/persistence analyzer; V1 inventory and Relationship / persistence / behavioral evidence only | Dialect-specific parsing, dynamic SQL, dependencies, transaction semantics and effective deployed definitions are unavailable. This is a material limitation for rule, workflow, ownership and side-effect recovery. |
| Typed DataSets / `.xsd` | **V1 Metadata/Detection Only** | Planned V1 detection; schema semantics deferred | Artifact presence and linkage to any checked-in generated C# that is independently parsed. | Snapshot and inventory; Repository/C# structural analyzer for generated `.cs` | V1 does not promise DataSet schema, relation, query or TableAdapter interpretation from `.xsd`. Code generation is never invoked. |
| WSDL | **Open Decision** | Not committed for V1 beyond inventory | At minimum, file presence and provenance; if later approved for V1, service/operation/message/schema declarations and their relationship to WCF source/configuration. | Candidate Classic WCF analyzer extension | Import resolution, policy assertions, generated-client reconciliation and supported WSDL versions need an approved rule set. Remote imports and service metadata endpoints must never be fetched by default. |
| XML schemas other than typed DataSets | **Open Decision** | Not committed for V1 beyond inventory | At minimum, file presence and provenance; if approved, declared types/elements and local import relationships. | Candidate WCF or future schema-specific capability | XML entities and external resolution remain disabled. Namespace/import resolution, schema versions and semantic linkage need explicit scope and fixtures. |

## Cross-cutting and difficult-to-resolve sources

| Artifact or concept | V1 treatment | Delivery state | Evidence contribution | Expected deterministic owner | Major limitations and security constraints |
|---|---|---|---|---|---|
| Third-party assemblies | **V1 Metadata/Detection Only** | Current reference-name foundation; V1 enrichment unresolved | Declared assembly/package identity, version/public-key metadata when safely present and unresolved external dependency edges. | Repository/C# structural analyzer | Repository binaries are not loaded, executed or decompiled. The trusted reference-assembly policy for safe symbol enrichment is an **Open Decision**. Missing implementation limits calls, inheritance and behavior evidence. |
| Checked-in generated source | **V1 Partial** | Current syntax coverage; planned classification/reconciliation | The same supported C# evidence as other captured `.cs` files plus safe markers indicating generated origin where detectable. | Repository/C# structural analyzer and relevant framework capability | Source generators, custom tools, T4 and service-reference generation are never run. Generated files absent from the snapshot do not exist for analysis; stale/duplicated output requires diagnostics and reconciliation. |
| Reflection and dynamic dispatch | **V1 Metadata/Detection Only** | Planned V1 detection | Reflection/dynamic API usage, literal type/member names where present and explicit ambiguous or unresolved relationship candidates. | Relationship / persistence / behavioral evidence | V1 must not guess runtime targets from names alone. Runtime values, late binding, proxy interception, expression compilation and container wiring can prevent exact resolution. |
| Batch and scheduled jobs | **V1 Partial** | Planned V1 | Supported entry-point, timer/scheduler registration, invoked workflow, state/data effects and external dependencies when visible in C# or configuration. | Relationship / persistence / behavioral evidence | The allowlisted frameworks and registration patterns are an **Open Decision**. Operating-system schedules, external orchestrators, dynamic registration and unavailable configuration may be invisible. No job is started. |
| External service configuration | **V1 Partial** | Planned V1 | WCF client endpoints plus supported literal service identifiers, addresses, protocols, credentials-mode indicators and configuration-to-client relationships. | Classic WCF analyzer and Relationship / persistence / behavioral evidence | V1 does not probe endpoints or use credentials. Environment substitution, encrypted sections, secrets, service discovery and custom clients may remain unresolved; sensitive values require redaction. |

## Mandatory gap behavior

For every artifact category in this matrix that occurs in the selected
snapshot, the configured V1 analyzers must either contribute their supported
evidence or make the limitation visible. They must not silently treat generic
inventory as semantic analysis.

- Unsupported, excluded, malformed, conditional, too-large, ambiguous or
  unresolved artifacts and relationships produce typed diagnostics with
  artifact provenance and an explicit coverage effect.
- A metadata-only or deferred artifact that could contain business rules,
  mutations, security policy, state transitions, transactions or integration
  behavior must appear as a known limitation for affected reasoning tasks.
- A missing parser cannot be compensated for by asking the LLM to parse raw
  source into `Observed` evidence. Models may create only evidence-backed
  `Inferred` or `Proposed` findings from a sealed ContextPack.
- “Not found within the analyzed and supported scope” must never be rendered as
  “does not exist.” High support for a local finding does not imply high
  repository coverage.
- Findings whose evidence depends on partial resolution must preserve that
  resolution quality, assumptions, alternatives and counterevidence through
  the Finding Graph and Domain Knowledge Model.
- DDD vocabulary in identifiers, comments, schemas or generated code remains
  untrusted source data. It is neither necessary nor sufficient for a DDD
  classification.

## Security invariants

All repository content is untrusted. Product V1 preserves the default
no-execution boundary established by Milestone 1:

- do not build or run the repository;
- do not execute MSBuild imports, targets, tasks, build events, transforms,
  analyzers, source generators, T4 templates, custom tools, SQL, jobs or config;
- do not load repository assemblies or activate types, extensions, behaviors,
  factories, serializers or providers;
- do not restore packages or invoke restore hooks;
- do not resolve XML entities or fetch schemas, WSDL imports, metadata endpoints
  or other network resources;
- do not connect to configured databases, queues or external services; and
- constrain paths, bytes, time and resources while applying redaction and
  authorization policy before sensitive evidence reaches persistence, logs,
  model context or the UI.

Any future proposal for safe semantic project evaluation or trusted reference
enrichment requires an explicit threat model and approval. It must not silently
weaken these constraints.

## V1 coverage gaps and open decisions

1. **Behavioral extraction contract:** the exact C# patterns and resolution
   rules for calls, mutations, conditions, validation, exceptions, state
   transitions, transactions, security checks, messages and side effects have
   not been approved. Without them, the expanded Domain Knowledge Model is not
   adequately supported by structural evidence alone.
2. **Persistence depth:** V1 includes relationship/persistence evidence from
   supported C# and configuration, but full `.edmx`, SQL, stored-procedure,
   typed-DataSet and database-schema analysis belongs to a future analyzer
   family. Data ownership, invariants and transaction conclusions must expose
   this gap.
3. **Safe symbol enrichment:** whether trusted reference assemblies or another
   non-executing mechanism may improve external type binding is unresolved.
   Loading repository binaries or running restore/build is not an option.
4. **Effective WCF/configuration resolution:** the approved WCF schema subset
   and handling of inheritance, `configSource`, custom extensions, transforms,
   machine configuration and code-created endpoints require detailed rules.
5. **WSDL and general XSD:** V1 semantic parsing has not been approved. The
   decision must include local-import boundaries, version support,
   reconciliation and fixtures; remote fetching remains prohibited.
6. **Framework/version allowlists:** supported .NET Framework, classic WCF,
   Entity Framework, ADO.NET provider and scheduler variants require an
   explicit compatibility statement and unsupported-version diagnostics.
7. **Generated artifacts:** origin detection, stale-output reporting and
   duplicate server/client contract reconciliation need deterministic rules.
8. **Sensitive evidence:** connection strings, endpoint identities,
   certificates, credentials, SQL literals, source comments and configuration
   values require a concrete classification/redaction policy before model
   egress or user display.
9. **Coverage accounting:** each analyzer needs an approved denominator and
   rules for how unsupported, metadata-only, excluded, ambiguous and unresolved
   artifacts affect coverage and completeness. No numeric threshold is fixed
   here.

## Related documentation

- [V1 Scope](../02-v1-scope.md)
- [Functional Requirements](../03-functional-requirements.md)
- [Analysis Pipeline](../architecture/04-analysis-pipeline.md)
- [Evidence Architecture](../architecture/05-evidence-architecture.md)
- [Security Architecture](../architecture/09-security-architecture.md)
- [Extensibility Architecture](../architecture/11-extensibility-architecture.md)
- [Roadmap](../10-roadmap.md)
- [ADR-006: Language- and Framework-Neutral Analyzer Architecture](../adr/006-language-and-framework-neutral-analyzer-architecture.md)
