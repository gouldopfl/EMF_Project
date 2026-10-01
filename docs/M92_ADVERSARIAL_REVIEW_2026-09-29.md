**Decision: do not approve the current M92 state for release.** Runtime attribution and database immutability have reproducible failures. Several hardening claims in the review request are absent from the actual working tree.

Reviewed on 2026-09-29 against `d6a726a4da8d97e261a25622b6478c362e8b6a2a`, which is also HEAD. The tracked diff contains 12 changed files, 1,447 additions and 21 deletions. All 12 untracked M92 C# files were also inspected; ordinary `git diff` omits them. Repository architecture analysis covered 1,472 C# files and the project dependency rules. Relevant unchanged generation, reuse, conversion, snapshot, migration, publication, and identity code was traced. Existing handoff documents, logs, and output archives were inventoried; this review is not a renewed audit of their medical contents or every unrelated subsystem.

The prior M92 review and the promised latest M92 feature-gate result were not supplied or located. They were requested during review. Consequently, the reassessment below classifies the concerns and hardening claims supplied in the prompt; it cannot honestly certify a finding-by-finding reconciliation with an unavailable prior report. September 25 hardening reports concern other work.

**Findings, in priority order**

1. **R1 — P1: generation accepts an unrelated, self-consistent manifest as its generating build.**

   [Output service constructor](/home/michael/EMF_Project/src/EMF.Extensions.VeteransClaims.Orchestration/VeteransReviewerPackageDocumentOutputService.cs:16) calls only `EmfBuildManifestIdentity.Validate`. The generation path then copies the supplied BuildId/revision into the link and archives the supplied JSON. It never calls either runtime verification method. `EmfVerifiedRuntimeIdentity` exists but is not consumed by generation. The CLI's own capture does not secure other public service callers.

   Reproduced with a manifest containing only `Unrelated.Assembly`, configuration `Fake`, target framework `FakeFramework`, and invented hashes/revision/MVID. DOCX generation succeeded; SQLite stored the real M91 renderer MVID alongside this unrelated M92 claim and retained the fake manifest. The manifest did not even contain the renderer assembly.

   Bind new generation to a verified inventory of the actual execution assemblies, verify the renderer artifact's MVID equals M91 `RendererBuild`, and take an immutable defensive copy. Preserve M91 Version 1 identity encoding. Verification of an arbitrary caller-selected subset is insufficient. Keep verified historical reuse before the new-generation requirement.

2. **R2 — P1: replacement bypasses remain in both new immutable tables.**

   [Migration 92](/home/michael/EMF_Project/src/EMF.Extensions.VeteransClaims.Persistence.Sqlite/VeteransClaimsSqliteMigrations.cs:3107) checks only `LinkId` in its BEFORE INSERT guard. With `recursive_triggers=OFF`, `INSERT OR REPLACE` using a new LinkId and an existing `(ProvenanceId, BuildId, SourceRevisionId)` deletes the original row through the composite UNIQUE constraint. Reproduced: the original timestamp was replaced with `2030-01-01`.

   [Migration 93](/home/michael/EMF_Project/src/EMF.Extensions.VeteransClaims.Persistence.Sqlite/VeteransClaimsSqliteMigrations.cs:3128) also has an implicit SQLite `rowid`. Explicitly reusing that rowid with a different BuildId bypasses its BuildId guard; REPLACE deletes the old archive without firing its delete trigger. Reproduced using a newly computed, internally valid manifest, including with `foreign_keys=ON`: the original archive disappeared while its M92 link remained. The same implicit-rowid issue applies to migration 92. Same-primary-key replacement tests do not cover either alternative conflict path.

   Cover every replacement conflict, including explicit rowid aliases, or remove the implicit-rowid surface for the new tables. M91 already has an analogous rowid exposure; any additive parent protection must preserve its identity semantics and historical data.

3. **R3 — P1: migration 92 has no foreign-key-independent parent guard, and M93 has no archive-parent relationship.**

   [Build-link table definition](/home/michael/EMF_Project/src/EMF.Extensions.VeteransClaims.Persistence.Sqlite/VeteransClaimsSqliteMigrations.cs:3087) relies on a foreign key to M91. There is no insert trigger checking parent existence. Reproduced an orphan insertion with `PRAGMA foreign_keys=OFF`; no triggers were dropped. Separately, a link's BuildId has neither a foreign key nor an existence guard referencing the archive, even with foreign keys enabled. Link retrieval validates its own hash, not the parent or archived manifest.

   Add the intended parent/archive invariants independently of connection pragmas, with explicit treatment of legitimate pre-upgrade history. Normal repository connections enabling foreign keys do not satisfy the requested direct-SQL threat model. Arbitrary schema replacement or trigger removal remains outside a trigger-based integrity boundary.

4. **R4 — P1: public M92-capable repository APIs still permit incomplete new provenance.**

   [M91 save](/home/michael/EMF_Project/src/EMF.Extensions.VeteransClaims.Persistence.Sqlite/Repositories/SqliteEvidencePackageRepository.cs:190) remains an unrestricted insertion path on an adapter advertising M92. [Two-record save](/home/michael/EMF_Project/src/EMF.Extensions.VeteransClaims.Persistence.Sqlite/Repositories/SqliteEvidencePackageRepository.cs:536) explicitly passes a null manifest. Standalone build-link save checks only the M91 parent, not an archived manifest or its source revision. These are successful public methods, not hypothetical reflection bypasses.

   Reproduced both a newly created M91 record with zero build links and an atomic M91/M92 pair whose BuildId has no archive. Existing tests actually assert successful use of these incomplete paths. The normal service's three-record transaction is correct but cannot establish an invariant for the whole adapter.

   Distinguish verified historical reads/exact retries from new writes on M92-capable storage. Preserve default-interface compatibility for genuinely M91-only implementations. Do not backfill current-build claims onto historical output merely to satisfy the new invariant.

5. **R5 — P2: recursive closure resolution can choose the wrong assembly and silently collapse different binaries.**

   [Resolver](/home/michael/EMF_Project/src/EMF.Common/EmfBuildManifestIdentity.cs:231) selects the first matching assembly across the entire AppDomain, ignoring the referencing assembly's load context. [Deduplication](/home/michael/EMF_Project/src/EMF.Common/EmfBuildManifestIdentity.cs:196) compares FullName only; different builds commonly retain the same assembly name/version. A duplicate with the same FullName is skipped without comparing its MVID/hash.

   Reproduced with two loaded `EMF.Common` copies in different contexts: reversing the root order changed BuildId, while both calls silently reported one artifact. Thus sorting the final inventory does not make ambiguous resolution deterministic. Resolve dependencies through the actual executing context and reject ambiguous duplicate identities. Static `EMF.*` reference traversal does not inventory dynamically loaded plugins or every deployed first-party file; no separate production dynamic loader was found in the searched code.

6. **R6 — P2: a stable file buffer is not proof of the exact loaded binary.**

   [Capture](/home/michael/EMF_Project/src/EMF.Common/EmfAssemblyBuildIdentity.cs:41) correctly hashes and parses a single byte buffer. It checks that buffer against loaded metadata, but MVID, source revision, configuration, framework, and name are not cryptographic commitments to all loaded bytes.

   Reproduced by loading a private copied assembly, atomically replacing its path with a metadata-preserving modified file, then capturing the same loaded `Assembly`: capture accepted a different SHA-256 and length. The proof used an appended byte, not a demonstrated executable-code exploit; it establishes that the claimed exact file hash need not identify the file originally loaded. Metadata-preserving IL/resource modification is not rejected by the compared fields either. Sequential closure capture also lacks a deployment-wide snapshot, and an in-place writer can change bytes during `ReadAllBytes`.

   For an exact-runtime claim, pin immutable deployment files to the loading process or capture/load from the same retained bytes and govern subsequent resolution. Otherwise explicitly scope the claim to metadata-matched files observed at capture time. A deployment/container digest is useful only with controls binding execution to that deployment.

7. **R7 — P2: malformed manifest structure still crashes the CLI.**

   [Canonical builder](/home/michael/EMF_Project/src/EMF.Common/EmfBuildManifestIdentity.cs:46) sorts before rejecting null elements. [File loader](/home/michael/EMF_Project/src/EMF.Common/EmfBuildManifestFile.cs:29) lacks the archive document's structure guard. `{"Artifacts":[null,null]}` and `{"Artifacts":[{},null]}` cause `NullReferenceException`, which the build command does not catch. Reproduced through the actual console executable: process termination by SIGABRT, represented as return code `-6` by Python, rather than the intended failure code 1. A single null happens to return 1 and misses this defect.

   Validate structure before sorting in the shared boundary. Missing/unreadable files, ordinary malformed JSON, runtime mismatch, and an unwritable directory destination returned 1 in the exercised cases. Missing/unknown/extra command syntax returned 2. Success paths passed existing tests. Those successes do not cover the multiple-null case.

8. **R8 — P2: malformed UTF-16 produces distinct accepted manifests with the same canonical bytes.**

   [Text validation and encoding](/home/michael/EMF_Project/src/EMF.Common/EmfBuildManifestIdentity.cs:280) allow unpaired surrogates, while `Encoding.UTF8` uses replacement fallback. Reproduced public API calls with artifact names `X\uD800` and `X\uD801`: both were accepted and yielded the same BuildId. This is a canonicalization collision, not a SHA-256 collision. It does not establish that the JSON parser accepts such input; the public builder does.

   Reject invalid Unicode scalar sequences or use strict encoding. For valid text, ordinal ordering, invariant numeric formatting, UTF-8 byte-length prefixes, literal LF, duplicate-name rejection, and the fixed ASCII regression vector are present. Preserve M91's existing UTF-16-length-based Version 1 contract; this fix belongs only to M92 validation/canonicalization.

9. **R9 — P2 where independently compiled clients are supported: the old public constructor disappeared.**

   [Output service constructor](/home/michael/EMF_Project/src/EMF.Extensions.VeteransClaims.Orchestration/VeteransReviewerPackageDocumentOutputService.cs:16) changed from three parameters to four optional parameters. Old source generally recompiles, but optional parameters do not preserve the three-argument CLR method signature. Reflection confirmed that signature is absent; an already compiled caller referencing it cannot bind to the new assembly. Retain a forwarding overload if binary compatibility is required. Default methods on the repository interface preserve source compatibility for older implementations; they do not fix this constructor break.

**Fresh reassessment of the supplied concerns**

These are classifications of the supplied concern areas, not invented IDs from the missing prior review. “Resolved” is limited to the stated subproblem.

| # | Concern | Current classification and evidence |
|---|---|---|
| 1 | Actual runtime binding | **Still open.** R1; verification helpers are disconnected from generation. |
| 2 | M91 RendererBuild versus M92 | **Partially resolved.** M91 identity code/encoding is unchanged and the MVID helper preserves its value; no cross-check against the M92 artifact exists. |
| 3 | Recursive inventory | **Partially resolved.** Recursive sorted first-party traversal replaces a small hardcoded list; R5 prevents treating it as ambiguity-safe. |
| 4 | Third-party/native/runtime deployment | **Still open; explicit deferral can be acceptable for a narrowly scoped M92.** See trust boundary below. |
| 5 | Source revision authenticity | **Still open.** Revision is compiled metadata, not verified source provenance. |
| 6 | Stable capture and TOCTOU | **Partially resolved.** One buffer fixes hash/metadata reads from different file snapshots; R6 remains. |
| 7 | Immutable retrievable archive | **Partially resolved.** Validated retrieval by BuildId works independently of current binaries, and normal triple save is atomic. R2–R4 defeat universal retention/link guarantees. |
| 8 | E3c full validation | **Resolved for the architecture repair.** `ReviewerBuildManifestDocument` calls the shared canonical validator after structural validation. Artifact tampering with unchanged envelope IDs is rejected. It is not reduced to JSON/header checks. |
| 9 | Mandatory M92 for new M91 output | **Partially resolved.** The service rejects missing manifests after reuse checks. R4 leaves new insertion paths in the capable adapter. |
| 10 | Verified reuse attribution | **Resolved for the service path examined.** Reused formats skip new provenance/link writes. Upgraded M91-only historical bytes were reused without assigning a current build. |
| 11 | Atomicity/retry/concurrency/cancellation | **Partially resolved.** Normal triple writes use one immediate transaction; exact concurrent retries deduplicate. Batch publication and cancellation limits remain as described below. |
| 12 | Migration hardening | **Still open.** R2/R3; the described composite-key and foreign-key-off protections are absent. |
| 13 | Canonical BuildId | **Partially resolved.** The old delimiter/platform concerns are superseded by versioned length-prefixed encoding; malformed Unicode and pre-validation sorting remain. |
| 14 | Predictable CLI failures | **Partially resolved.** Ordinary cases pass; R7 is a reproduced unhandled exception. |
| 15 | Compatibility | **Partially resolved.** M91 hash semantics and genuine historical upgrade/reuse survive. R9 and three failing M90 snapshot fixtures prevent an unqualified compatibility claim. |
| 16 | Architecture inversion | **Resolved for the identified dependency.** Persistence.Sqlite directly references only VeteransClaims. The domain archive document uses its already-permitted Common dependency and exposes no Common types in its public contract. No new project dependency inversion found. |
| 17 | Security/privacy | **Partially resolved.** Generated manifest fields contain assembly metadata/hashes, not patient data, credentials, or absolute assembly paths. Generic CLI failure diagnostics are good; R7 exposes an unhandled stack trace. No bounded manifest/file input size is enforced. |
| 18 | Negative/upgrade tests | **Still open in the repository suite.** The review added temporary reproductions, including a real M91 schema upgrade; these need durable regression coverage. |

**Trust boundary and compatibility limits**

The first-party inventory excludes DocumentFormat.OpenXml and other third-party managed libraries, native raster/OCR/PDF components, `.deps.json`, `.runtimeconfig.json`, the installed .NET runtime/JIT, and the LibreOffice binary and its deployment. M91 records converter identity/version text; it does not hash that executable, and the version query is a separate process invocation from conversion. OS font fallback and converter environment remain outside the claim. However, the bundled DejaVu TTFs are embedded resources in the reviewer assembly, so their bytes are covered by an honest hash of that assembly. It would be incorrect to say all fonts are excluded.

These exclusions may be deferred to a deployment/container provenance milestone if M92 is explicitly defined as first-party managed artifact provenance. They block describing the present BuildId as the complete output-affecting runtime identity or a reproducible-output guarantee. Fixing R1–R4 is necessary even under that narrower scope.

`GetSourceRevisionId` parses a hexadecimal suffix of `AssemblyInformationalVersion`. The local SDK appends `SourceRevisionId`; project/build properties can influence this metadata. A build of this dirty working tree reported `d6a726a4da8d97e261a25622b6478c362e8b6a2a`, despite the uncommitted M92 changes. No dirty marker, source-tree digest, clean-checkout enforcement, or signed builder attestation was found. Assembly SHA-256 distinguishes changed bytes; it does not prove they were built from the named revision. A clean source/input digest plus a trusted build attestation is required before claiming that relationship as verified. A signed assertion without a trustworthy build process is not sufficient.

The archive addresses canonical manifest fields, not the exact original JSON text: whitespace/property ordering are not part of BuildId, and unknown JSON properties are not validated as identity fields. The first accepted JSON is retained. This is reasonable semantic addressing if documented; it should not be described as hashing every byte of the supplied JSON document. There is also no trusted signing/allowlist of acceptable release manifests.

Verified reuse in the service neither requires a new manifest nor reattributes old bytes. The console currently captures the whole build before calling that service, however, so a capture failure in an unrelated first-party component can still prevent otherwise valid historical reuse at the CLI. This is an availability limitation, not a demonstrated false-attribution path.

Normal three-record persistence validates the manifest and both identities before writing; under one immediate transaction it archives the manifest, checks the sealed snapshot, and inserts/reuses M91 and M92 rows. Existing retry comparisons preserve the original timestamps. Four simultaneous exact triple writes in the review produced one new M91 row, one link, and one archive. Existing tests exercise archive-insert and link-insert failures with rollback. The snapshot seal is a separate earlier transaction; DOCX and PDF triples are separate transactions; filesystem publication is later still. A failure/cancellation between these stages may leave a sealed snapshot or a committed first-format triple, but should not leave half of one triple. There is no evidence for an all-formats-plus-files atomicity guarantee. Token propagation and transaction disposal are present; mid-write cancellation timing and contention-timeout behavior were not exhaustively fault-injected. Cancellation after a successful commit cannot be equated with rollback; exact retry remains necessary.

The genuine checkpoint-schema upgrade succeeded: all 91 migration SQL scripts were extracted from `git show d6a726a4:...`, applied to a fresh database, and populated with a sealed synthetic snapshot and M91 row before applying current migrations 92/93. The row remained equal, no retroactive build link appeared, reinitialization succeeded, and verified reuse worked without a manifest. This is stronger than deleting ledger entries from a current schema, but it is one synthetic fixture, not an exhaustive production database upgrade test. The migrator checks version/name, not SQL hashes. Editing an already-applied migration 92/93 will not repair existing databases; deployed variants need an additive corrective migration.

Three existing snapshot tests fail because their real SQLite-backed service calls were not updated for the mandatory manifest:

- `Rerender_UsesSealedInputsAfterAllCurrentInputsChange_AndDoesNotFetchRegulations`
- `FailedPdfConversion_DoesNotSeal_AndSuccessfulRetrySeals`
- `PublicationFailureAfterSeal_RetryPublishesSnapshotWithoutConsultingMutableInputs`

The new fail-closed requirement is intentional; do not remove it to green these tests. Adapt the capable fixtures/call sites and retain their original M90 behavioral assertions, while testing genuine M91-only implementations separately. In their present form these tests no longer reach the behaviors they protect.

**Validation and remaining coverage**

| Evidence from this review | Result |
|---|---|
| Focused suite including M90 snapshot, M91, M92, archive, CLI, migration and output-service tests | **107 passed, 3 failed, 1 skipped** |
| M92-related class subset within that run: identity, archive, build-link model, output provenance/unit+integration, CLI router | **67 passed, 0 failed, 1 skipped**; this is a review-selected subset, not the missing supplied feature gate |
| Architecture auditor tests | **22 passed, 0 failed** |
| Architecture auditor against current repository | 1,472 C# files, zero parse errors; all architecture dependency rules pass; **6 resource-safety findings**, including three new whole-file reads in Common |
| `git diff --check` | Clean |
| Adversarial temporary harness | Reproduced R1–R8 conditions described above; confirmed missing old constructor signature |
| Actual checkpoint M91 → current migration 93 fixture | Historical row preserved, no invented link, reuse succeeded, concurrent triple retry deduplicated |

The skipped case is the optional LibreOffice identity integration test. The full repository test suite was not run. Azure OpenAI was not invoked; no external model calls were required. The reported E3c 13-pass result was treated as supplied evidence, not proof or an independently identified test selection.

Commands and evidence remain locally available:

- [Focused test log](/tmp/emf-m92-review-tests.log) and [TRX](/tmp/emf-m92-review-results/m92-review.trx).
- [Temporary adversarial harness](/tmp/emf-m92-adversarial/Program.cs), [project](/tmp/emf-m92-adversarial/Review.csproj), [initial results](/tmp/emf-m92-adversarial/run.log), [CLI structure results](/tmp/emf-m92-adversarial/cli.log), and [upgrade/concurrency results](/tmp/emf-m92-adversarial/upgrade.log).
- [Architecture test log](/tmp/emf-m92-architecture-tests.log) and [repository audit](/tmp/emf-m92-architecture-audit.log).
- [Reviewed tracked patch](/tmp/emf-m92-adversarial/reviewed-tracked.patch) and [untracked inventory](/tmp/emf-m92-adversarial/untracked-inventory.txt).

The harness uses existing synthetic test fixtures and private temporary databases/assembly copies. Production source and existing tests were not edited. Temporary evidence is not committed and may be removed by system cleanup.

Before release, retain negative tests for unrelated/omitted/mismatched renderer manifests, duplicate load contexts, metadata-preserving file replacement, alternate UNIQUE and explicit-rowid replacement, foreign-key-off orphans, missing archives, all public new-write paths, malformed/null artifact arrays, strict Unicode and non-ASCII/culture vectors, genuine M91 upgrades, cancellation between persistence stages, and ABI compatibility where supported. Add cross-platform execution of fixed canonical vectors; this review ran on Linux only. These checks should protect the repaired invariants without changing M90 or M91 Version 1 identity semantics.
