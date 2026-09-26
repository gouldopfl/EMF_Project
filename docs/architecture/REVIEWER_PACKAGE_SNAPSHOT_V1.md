# Persisted reviewer-package snapshot V1

Starting checkpoint: `d1ce3bfa`. This change addresses historical renderer inputs; it is not a new hardening audit or a summary reuse redesign.

## Existing architecture and capture boundary

`EvidencePackageService` and `SqliteEvidencePackageRepository` persist package identity, claim/basis, purpose/reviewer role, member artifact IDs, content roles, and reviewer page selections. The existing Veterans Claims migration ledger provides ordered, transactional migrations with a compatibility recheck under the writer transaction.

`VeteransReviewerPackageDetailsService` resolves current artifact metadata, source names/provenance/relationships, appendix classification, reviewed literature associations/excerpts, normalized literature text, and selected printable renditions. Its PDF/text extraction and print providers read the existing evidence/content store.

`VeteransReviewerPackageAssemblyService` adds these current projections:

- `VeteransReviewerPackageCurrentMedicationService`: the reconciled current medication ledger, scoped to member evidence and selected source pages.
- Medication progression and clinical-context services: relevant ledger history, basis labels, entry/source mappings, attributed indication reconciliation, prescription matching identifiers, and clinical context.
- Source clarification and clinical progression services: member-scoped annotations and corrections, including internal evidence matching IDs and replacement text.
- `VeteransReviewerMedicalOpinionRequestService`: independent opinion request wording and applicable citations from condition, service connection, and regulatory repositories.
- Explicit preparer and veteran display values previously read from the environment for every output.

`VeteransReviewerPackageDocumentOutputService` obtains regulatory text/version/provenance and calls the existing DOCX renderer, then the existing PDF converter if requested. This is the capture boundary: all meaning-bearing renderer inputs are present. The first output is rendered from a deserialized manifest copy, ensuring first and subsequent output use the same representations.

No new evidence store, renderer, migration ledger, encryption mechanism, or medical inference is introduced. Selected printable renditions (not another source-artifact repository) are embedded as renderer inputs in the manifest. This avoids recreating historical pages through changed extraction/rendering providers or mutable fallback text. The original evidence remains in the existing store.

## Schema and lifecycle

Migration **90, `AddReviewerPackageSnapshots`**, uses the existing Veterans Claims migrator:

- `VeteransClaims_EvidencePackages.ReviewerSnapshotVersion`: `0` for legacy packages; repository-created new packages explicitly write `1`.
- `VeteransClaims_ReviewerPackageSnapshots`: one primary-key row per `EvidencePackageId`, with `Version`, canonical JSON `Payload`, and uppercase hexadecimal `Sha256`; package foreign key and required-field/check constraints.
- `ReviewerSnapshotSealed`: initially `0`; an AFTER INSERT trigger sets it to `1` in the manifest insert transaction. Reads require the marker and row to agree, so a missing row on a sealed package fails closed. There is no separately committed lock transition.
- Database triggers reject snapshot updates/deletes/replacement, package replacement or eligibility changes, sealed package changes/deletion, and insertion/update/deletion of sealed members (including selections and moves between packages).
- Insert triggers explicitly prevent `INSERT OR REPLACE` from bypassing delete triggers when SQLite recursive triggers are disabled.

For new packages, no row means **pending**, not historical. Membership and selection may still be edited. First-output sealing means:

1. Assemble the current reviewer view and obtain the complete required regulatory text.
2. Validate and canonicalize V1; deserialize and validate the exact persisted representation.
3. Render DOCX; if PDF or both were requested, finish and validate PDF conversion too.
4. In one repository write transaction, recheck package identity, purpose, basis, reviewer role, exact member IDs/roles/selections against the captured view. Check for an existing identical row or a conflict. Insert the complete version/payload/hash row; its trigger sets the sealed marker. Commit both together. Trigger-based locks become effective with that same commit.
5. Only after commit does the output service return bytes for file publication.

Identical saves are idempotent. Conflicting concurrent captures for one package identity fail; they never overwrite the winner. Membership/selection changes while inputs are being assembled prevent sealing. Cancellation before commit, serialization/rendering/conversion failure, or persistence rollback does not publish bytes or leave a partial manifest. A commit can succeed before cancellation or failure is reported to a caller; retry reads that immutable row.

A subsequent filesystem publication failure **does not undo a successful seal**. Retry uses the sealed input contract. DOCX/PDF publication still uses the existing per-file atomic replacement; publication of two files is not a cross-filesystem transaction. A successful seal records validated rendering inputs, not proof that a recipient received an output file.

The console checks for a sealed manifest before constructing current evidence/content, clinical, or regulatory retrieval dependencies. Assembly and output services also support snapshot repository injection; the console always supplies it. The low-level DTO rendering API without a repository remains an explicit in-memory/current-view operation, not a historical reconstruction API. Current detail/projection services remain available for preparing pending packages.

## Exact V1 wire contract

`VeteransReviewerSnapshotV1Contract.Fields` is the authoritative, explicit field map. Serializer attributes on renderer DTOs, including `JsonIgnore`, do not define this contract. Only mapped types/fields can be serialized, with V1 nullability pinned independently of DTO annotations. Every mapped property is required on read, **including nullable properties**: a missing value is not an explicit null. Unknown properties/types, duplicate JSON names, null collection elements, missing members, mismatched identities/selections, unsupported versions, incomplete regulation sets, and bad hashes fail closed. A regression test requires explicit review when renderer input properties are added.

The root object contains `Version = 1`, `Details`, and `Regulations`. Details includes:

- Package identity/claim/basis/purpose/reviewer role and exact member IDs, roles, and selections.
- Original and reviewer-facing artifact metadata, names/types/fingerprints/timestamps; text, normalized literature text, appendix and source labels; provenance and relationships.
- Selected printable page numbers/order, bytes, media types, source artifact ID, fallback status, rotation, native text geometry and glyphs.
- Reviewed literature associations (basis, requirement, literature source, guidance role, description), excerpts, review/provenance/execution fields.
- Current reconciled medications; prescribed medication records/history; relevant medication progressions, basis labels, entry source mappings, attributed indication reconciliation; clinical context with prescription matching identifiers.
- Clarifications (source member ID, locator, original wording, clarification, match/replacement text) and progression events (source member ID, date/type/locator/summary).
- Medical opinion request wording and regulatory citations, veteran display name and preparer.
- Regulatory citation, text, source URI, up-to-date date, retrieval timestamp, and source SHA-256.

Only the projections actually supplied to rendering are historical facts in this contract. It does not claim to snapshot unrelated claim/database state. Existing medical opinions remain underlying evidence or the existing labeled presentation; requests remain requests for an independent opinion. No medical/legal/adjudicative conclusion is added. Diagnosis wording is preserved, including **Bilateral pes planus**.

Canonicalization is UTF-8 JSON with no BOM/insignificant whitespace. Object/map property names are ordered ordinally at every nesting level. Arrays retain input order because page, evidence, and clinical presentation ordering is meaningful. Strings use `System.Text.Json` escaping. Nulls are emitted explicitly. Identities are nonempty JSON strings (including identity-keyed maps). Binary page inputs use the framework's base64 representation. Dates use invariant ISO representation; `DateTimeOffset` retains its offset and uses round-trip `O` formatting so a displayed local date cannot shift on capture. Numeric tokens are normalized by exact decimal digits/exponent expansion (invariant, no floating-point rounding), with V1 limits of 4096 coefficient digits and absolute exponent/decimal-point position. Equivalent trailing zeros and exponent spellings normalize identically; values exceeding the contract limits fail closed. The SHA-256 covers the entire canonical payload, **including the root version**; the row version must agree. No timestamp/random ID is generated during serialization.

V1 is frozen. Changes to its wire fields or their interpretation require a new version and an explicit reader policy, not an in-place rewrite of old rows. The hash is corruption detection and identity binding, not a keyed signature against someone able to rewrite the database/schema. Existing storage protections apply; this change adds no encryption scheme.

## Legacy policy

Migration records eligibility only; it does **not** backfill historical values. Existing package rows retain version `0`, regardless of whether a document was previously published. Historical output reports that the legacy package has no historical snapshot and immutable reconstruction is unavailable, and instructs the operator to create a new package from reviewed current inputs. It never infers an original state or silently converts a legacy package to V1. New raw SQL inserts default to legacy unless they explicitly participate in the new repository contract.

Corrupt/incomplete/unknown-version sealed rows also fail closed. They are never treated as pending and never trigger current-state reconstruction. Correcting inputs or changing selected pages after seal requires a **new package identity**.

## Reuse boundary and limitations

Summary reuse keys and output fingerprinting are unchanged. Existing reuse can select a sealed package; rendering that identity returns its historical view, even if current clinical or metadata inputs changed. Reuse can also select a legacy package, whose historical render then fails explicitly. Choosing when to request a new current-view package, separating summary reuse from package-output reuse, and incorporating manifest identity into output fingerprints remain a **separate follow-on**. No compatibility change to the reuse key was required here.

The manifest freezes the assembled renderer view, not a single database-wide read timestamp across all repositories and content providers. Later source edits cannot affect sealed output; concurrent edits during initial assembly can affect which input values are first captured. The membership/selection transaction specifically protects the package boundary.

This is meaning/input reproducibility, not byte-identical DOCX/PDF reproduction across renderer, font, converter, runtime, or layout upgrades. Existing static renderer rules remain code; V1 changes to those rules require compatibility review. Printable renditions increase database/backup size and retain PHI under the existing database security/retention controls. Large image-heavy package sizing has not been benchmarked.

## Read-state behavior

| Persisted state | Historical read/output |
| --- | --- |
| Version 1, sealed marker 0, no manifest | Pending; assemble current inputs for first-output capture. No historical claim is made. |
| Version 0 | Legacy; explicit immutable-reconstruction-unavailable error, no capture/backfill. |
| Version 1, sealed marker 1, complete valid V1 row | Validate integrity/contract; use only frozen inputs. |
| Sealed marker/row disagree; missing, incomplete, corrupt, or unsupported row | Fail closed. Never use current inputs or recapture under this identity. |

## Validation and review handoff

Focused tests were run first, followed by the broader reviewer/package/migration/dependency selection. After the final serializer refinements, the focused integration selection was rerun:

- Snapshot regressions: **29 cases** in `VeteransReviewerPackageSnapshotTests`.
- Final focused integration run: **57 passed, 0 failed** (snapshot, package repository, migration, document output, and console package tests).
- Broader relevant run: **566 passed, 10 skipped, 0 failed**. The skipped cases are opt-in LibreOffice pagination/layout tests (`EMF_REVIEWER_LAYOUT_TESTS=true`); these were not enabled.
- The full solution suite was not run: the changed surface is the reviewer/package persistence and output path, covered by the focused integration run plus the broader relevant selection.
- `git diff --check` passes. No commit or staging was performed.

Regression coverage includes V1 golden serialization/hash and version binding, culture/map ordering, exact high-precision numeric values, explicit nullability and internal `JsonIgnore` fields, selected pages and printable bytes/geometry, changed metadata/literature/classification/medication/clarification/progression inputs, regulatory lookup bypass, persisted historical assembly, conflicting and identical concurrent seals, migration concurrency/idempotency, membership races, legacy behavior, missing/corrupt sealed rows, transaction-trigger failure rollback, PDF conversion failure, and post-seal filesystem publication failure/retry. Direct SQL tests cover member/page-selection mutation and replacement attempts outside service methods.

Tests use disposable local databases and synthetic inputs. No live Azure OpenAI/Azure Monitor/Key Vault calls were made; no PHI was transmitted externally. Live data, `EMF_Output/`, backups, and the pre-existing audit/handoff material were not modified.

Changed files:

- `src/EMF.Extensions.VeteransClaims/Contracts/IEvidencePackageRepository.cs`
- `src/EMF.Extensions.VeteransClaims/Models/Adjudication/ReviewerPackageSnapshot.cs` (new)
- `src/EMF.Extensions.VeteransClaims.Persistence.Sqlite/Repositories/SqliteEvidencePackageRepository.cs`
- `src/EMF.Extensions.VeteransClaims.Persistence.Sqlite/VeteransClaimsSqliteMigrations.cs`
- `src/EMF.Extensions.VeteransClaims.Orchestration/VeteransReviewerPackageAssemblyService.cs`
- `src/EMF.Extensions.VeteransClaims.Orchestration/VeteransReviewerPackageDocumentOutputService.cs`
- `src/EMF.Extensions.VeteransClaims.Orchestration/VeteransReviewerPackageSnapshot.cs` (new)
- `src/EMF.Extensions.VeteransClaims.Orchestration/VeteransReviewerSnapshotV1Contract.cs` (new)
- `src/EMF.Console/VeteransConsoleCommand.cs`
- `tests/EMF.Tests/VeteransClaimsSqliteMigrationTests.cs`
- `tests/EMF.Tests/VeteransReviewerPackageSnapshotTests.cs` (new)
- `docs/architecture/REVIEWER_PACKAGE_SNAPSHOT_V1.md` (this document)
