# Migration 94 review and clone rehearsal — 2026-09-30

Migration: **94 — FreezeReviewerPackagePresentation**. Review is based on implemented SQL and actual clone schema, not the design document.

The live lumbar database is **M90**, not M93. A read-only SQLite backup was advanced to M93 using the normal migration engine with the actual catalog capped at 93. Its checkpoint was backed up again, then normal `VeteransClaimsSqliteSchema.InitializeAsync` applied M94. No live database was migrated. No candidate build or real reviewer package was generated.

Final clone: `/home/michael/EMF_Output/m94-validation/run-20260930-012418/lumbar-m94-final.db`.
Detailed machine-readable results, full row counts, test TRX and independent Astra script/results: `/home/michael/EMF_Output/m94-validation/run-20260930-012418`.

## Acceptance

- Final focused tests: **175 executed / 175 passed / 0 failed / 0 skipped**. All 20 repeat-render/cross-claim determinism tests executed, including real synthetic PDF conversion/layout checks with `EMF_REVIEWER_LAYOUT_TESTS=true`.
- Astra independent adversarial review: **29/29 cases passed**; 27 invalid operations rejected, two valid controls accepted. Exact original four attacks, isolated guards, three-version cycle attempts, update/delete/replace and JSON type/duplicate-key attacks tested with foreign keys disabled.
- `integrity_check`: **ok**. `foreign_key_check`: **no violations**.
- All pre-M94 table contents (except the added migration ledger row) and schema objects are unchanged.
- Seven existing packages readable; one historical source snapshot passed repository integrity validation.
- Second initialization: identical logical database dump, one M94 ledger row, no duplicate objects or rows.
- Injected DDL failure after first M94 CREATE: new table and ledger entry rolled back; ledger stayed at 93. Normal retry after removing the deliberate collision succeeded.
- Full regression after the atomic version-creation follow-up: **3,127 total / 3,125 executed / 3,125 passed / 0 failed / 2 skipped** across all three Release suites. Only disabled Azure OpenAI/Azure Monitor live integration tests were skipped. Focused acceptance after the follow-up: **183/183 passed**, including eight boundary-failure cases. Logs, TRX, exact durations, protected hashes and Astra side-effect review are in `handoff/M94_atomic_version_validation_2026-09-30`.

## Exact schema summary

M94 creates two WITHOUT ROWID tables; it does not alter any existing table. Every column below has **no default**. Only `PreviousPackageId` is nullable. There are no additional UNIQUE constraints or explicitly created indexes. Each table's primary key has its SQLite primary-key index; no rowid replacement surface exists. Full literal CHECK constraints, foreign keys and trigger SQL appear below.

| Table | Column | Type / constraint |
| --- | --- | --- |
| ReviewerPresentations | EvidencePackageId | TEXT NOT NULL PRIMARY KEY; FK to ReviewerPackageSnapshots.EvidencePackageId |
| ReviewerPresentations | SourceSnapshotSha256 | TEXT NOT NULL; 64 uppercase hex characters |
| ReviewerPresentations | PackagePreparedDate | TEXT NOT NULL; exact ISO yyyy-MM-dd calendar date, later than default 0001-01-01 |
| ReviewerPresentations | Payload | TEXT NOT NULL; valid JSON object; required typed fields agree with relational columns |
| ReviewerPresentations | Sha256 | TEXT NOT NULL; 64 uppercase hex; presentation-envelope hash |
| ReviewerPresentations | DocxSha256 | TEXT NOT NULL; 64 uppercase hex; frozen DOCX hash |
| ReviewerPresentations | PreviousPackageId | TEXT NULL; nonempty if present, not self; FK to ReviewerPresentations.EvidencePackageId |
| ReviewerFrozenPdfs | EvidencePackageId | TEXT NOT NULL PRIMARY KEY; FK to ReviewerPresentations.EvidencePackageId |
| ReviewerFrozenPdfs | PresentationSha256 | TEXT NOT NULL; 64 uppercase hex; matching presentation hash required by trigger |
| ReviewerFrozenPdfs | Payload | TEXT NOT NULL; valid typed JSON object; fields agree with relational columns |
| ReviewerFrozenPdfs | Sha256 | TEXT NOT NULL; 64 uppercase hex; frozen PDF envelope hash |
| ReviewerFrozenPdfs | PdfSha256 | TEXT NOT NULL; 64 uppercase hex; frozen PDF byte hash |
| ReviewerFrozenPdfs | ConverterIdentity | TEXT NOT NULL; nonempty trimmed value |
| ReviewerFrozenPdfs | ConverterVersion | TEXT NOT NULL; nonempty trimmed value; contains controlled converter/profile version identity |

All table names above have the `VeteransClaims_` prefix. Foreign keys use default **NO ACTION**, with no cascades. BEFORE UPDATE/DELETE triggers always abort for both frozen tables; BEFORE INSERT prevents replacement even with recursive triggers disabled.

`PackagePreparedDate` is both an ISO text column and the matching top-level JSON date. `GeneratedUtc` remains the existing output-provenance audit field, not an M94 presentation field. JSON `Version=1` is the envelope format version; business revisions use a fresh EvidencePackageId and optional PreviousPackageId, not an incremented relational Version column.

DOCX and PDF bytes are inline base64 in their JSON envelopes, not external file references. Presentation JSON also carries canonical cover, render profile and preparation renderer build. PDF JSON carries converter identity/version. Repository SHA/header/base64/canonical serialization validation remains mandatory.

Source binding: EvidencePackageId → immutable V1 ReviewerPackageSnapshots → EvidencePackages; the insertion trigger independently requires the matching source SHA and a V1 sealed package. The lineage trigger independently requires an already-frozen predecessor in the same claim issue, including with FK checks off. Insert-only predecessor ordering plus immutable package/lineage records prevents multi-version cycles. Branching is allowed; no current-version pointer exists.

## Corrections and adversarial assessment

Original M94 accepted self-reference, missing prior reference with FK off, malformed date and malformed payload. Corrections reject all four. Required top-level identity/version/date fields now have `json_type` checks and exact relational binding; integer format Version must be 1. Nonstring required profile/role/base64 fields, nonhex hashes, invalid calendars, default/null dates and duplicate object keys (including nested objects) are rejected.

New presentation freezing also checks historical DOCX hashes/dates; frozen PDF creation checks historical PDF hashes/converter identity. Subsequent output insertion must match source, prepared date, frozen DOCX or PDF hash, and PDF converter identity. Existing legacy output paths without a presentation remain supported under unchanged M91/M92/M93 protections; M94 does not fabricate historical presentations.

M92 and M93 SQL and tables are unchanged. M92 immutable build links still reference existing output provenance and reject replacement/orphans independently of FK settings. M93 build-manifest archives remain immutable. M94 adds guards to the existing output-provenance table; it neither removes nor bypasses earlier guards. The actual clone has zero M91/M92/M93 output/build/manifest rows, so populated-row protection is established by synthetic provenance tests, not falsely attributed to existing real rows.

Structural correctness is enforced in SQLite; cryptographic integrity, valid encoded document bytes, deeper semantic/source consistency and exact canonical serialization remain repository responsibilities. A structurally coherent forged hash can be inserted by direct SQL, but repository validation fails closed. A SQLite administrator who disables CHECK constraints or removes triggers is outside the normal application boundary.

Repository plan/PDF saves each use an immediate transaction and are exact-hash retry idempotent. Normal migration applies its DDL and ledger row in one transaction, with compatibility recheck; interruption cannot commit partial M94 schema. Follow-up correction: new-version service creation now persists the package, artifact membership, source snapshot and presentation in one immediate transaction. Failure before commit rolls back the entire new identity, permitting retry with that identity. Before/after INSERT failure injection covers all four persistence tables. Preserve still requires a validated presentation. Explicit incomplete-version recovery now completes matching, caller-reviewed membership/source state atomically and rejects existing presentations, legacy identities and output history. Recovery never runs automatically or fabricates historical inputs. This recovery follow-up is verified by focused tests only; the full regression counts above precede it.

## Relevant row counts

| Table | M93 checkpoint | M94 / restart |
| --- | ---: | ---: |
| `VeteransClaims_EvidencePackages` | 7 | 7 |
| `VeteransClaims_EvidencePackageArtifacts` | 177 | 177 |
| `VeteransClaims_ReviewerPackageSnapshots` | 1 | 1 |
| `VeteransClaims_ReviewerPackageOutputProvenance` | 0 | 0 |
| `VeteransClaims_ReviewerPackageOutputBuildProvenance` | 0 | 0 |
| `VeteransClaims_ReviewerBuildManifests` | 0 | 0 |
| `VeteransClaims_ReviewerPresentations` | absent | 0 |
| `VeteransClaims_ReviewerFrozenPdfs` | absent | 0 |
| `VeteransClaims_SchemaMigrations` | 93 | 94 |

All other table counts and row-content fingerprints are in `final-rehearsal.json`. M90 → M93 added the M91/M92/M93 provenance structures with no fabricated rows.

## Protected inputs and working tree

Live DB before **and** after: `cbde12e1718ed146f61aac76640cd5a6016bbb7fb52307483512a7d1a867dae6`.

Gold before **and** after: `1822c8f0437c1d617560e0cc595250d2ab11a441ce60685d6b422840538a6a4f`.

Trusted dfee83b7 manifest before **and** after: `800704a70c5fabfbb7f7aa57e00463a8462558750a1c49dc65b7987691878e6a`.

Azure OpenAI launch flag remained unset; no external AI service was invoked by the EMF application. No live database migration, real package generation, production candidate build, staging, commit or push. `git diff --check` passes. Prior working-tree changes are retained.

Files changed for this correction:

- `src/EMF.Extensions.VeteransClaims.Persistence.Sqlite/VeteransClaimsSqliteMigrations.cs` — M94 only.
- `tests/EMF.Tests/VeteransReviewerPackageArchitectureTests.cs` — synthetic structural attack regressions.
- `docs/M94_REVIEW_AND_CLONE_REHEARSAL.md` — this report.

M94's reviewed structural holes are closed and the focused candidate-validation foundation passes. **No candidate generation now**; the full Release regression passed after the atomic version-creation follow-up. No production rollout or live migration is authorized by this report.

## Actual M94 schema objects

These definitions were read from the final migrated clone's `sqlite_master`.

```sql
CREATE TABLE VeteransClaims_ReviewerPresentations (
    EvidencePackageId TEXT PRIMARY KEY NOT NULL,
    SourceSnapshotSha256 TEXT NOT NULL CHECK (length(SourceSnapshotSha256) = 64 AND SourceSnapshotSha256 NOT GLOB '*[^0-9A-F]*'),
    PackagePreparedDate TEXT NOT NULL CHECK (length(PackagePreparedDate) = 10 AND PackagePreparedDate GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]' AND PackagePreparedDate > '0001-01-01' AND date(PackagePreparedDate, '+0 days') IS PackagePreparedDate),
    Payload TEXT NOT NULL CHECK (json_valid(Payload) AND json_type(Payload) = 'object'),
    Sha256 TEXT NOT NULL CHECK (length(Sha256) = 64 AND Sha256 NOT GLOB '*[^0-9A-F]*'),
    DocxSha256 TEXT NOT NULL CHECK (length(DocxSha256) = 64 AND DocxSha256 NOT GLOB '*[^0-9A-F]*'),
    PreviousPackageId TEXT NULL,
    FOREIGN KEY (PreviousPackageId) REFERENCES VeteransClaims_ReviewerPresentations(EvidencePackageId),
    CHECK (PreviousPackageId IS NULL OR (length(trim(PreviousPackageId)) > 0 AND PreviousPackageId != EvidencePackageId)),
    CHECK (json_type(Payload, '$.PackageId') IS 'text' AND json_extract(Payload, '$.PackageId') IS EvidencePackageId
        AND json_type(Payload, '$.Version') IS 'integer' AND json_extract(Payload, '$.Version') IS 1
        AND json_type(Payload, '$.SourceSnapshotSha256') IS 'text' AND json_extract(Payload, '$.SourceSnapshotSha256') IS SourceSnapshotSha256
        AND json_type(Payload, '$.PackagePreparedDate') IS 'text' AND json_extract(Payload, '$.PackagePreparedDate') IS PackagePreparedDate
        AND json_type(Payload, '$.Sha256') IS 'text' AND json_extract(Payload, '$.Sha256') IS Sha256
        AND json_type(Payload, '$.DocxSha256') IS 'text' AND json_extract(Payload, '$.DocxSha256') IS DocxSha256
        AND (json_type(Payload, '$.PreviousPackageId') IS NULL OR json_type(Payload, '$.PreviousPackageId') IN ('null', 'text'))
        AND json_extract(Payload, '$.PreviousPackageId') IS PreviousPackageId
        AND json_type(Payload, '$.Cover') IS 'object'
        AND json_type(Payload, '$.Cover.ReviewerRole') IS 'text'
        AND coalesce(length(trim(json_extract(Payload, '$.Cover.ReviewerRole'))), 0) > 0
        AND json_type(Payload, '$.RenderProfile') IS 'text'
        AND coalesce(length(trim(json_extract(Payload, '$.RenderProfile'))), 0) > 0
        AND json_type(Payload, '$.PreparationRendererBuild') IS 'text'
        AND coalesce(length(trim(json_extract(Payload, '$.PreparationRendererBuild'))), 0) > 0
        AND json_type(Payload, '$.DocxBase64') IS 'text'
        AND coalesce(length(json_extract(Payload, '$.DocxBase64')), 0) > 0),
    FOREIGN KEY (EvidencePackageId) REFERENCES VeteransClaims_ReviewerPackageSnapshots(EvidencePackageId)
) WITHOUT ROWID;
```

```sql
CREATE TABLE VeteransClaims_ReviewerFrozenPdfs (
    EvidencePackageId TEXT PRIMARY KEY NOT NULL,
    PresentationSha256 TEXT NOT NULL CHECK (length(PresentationSha256) = 64 AND PresentationSha256 NOT GLOB '*[^0-9A-F]*'),
    Payload TEXT NOT NULL CHECK (json_valid(Payload) AND json_type(Payload) = 'object'),
    Sha256 TEXT NOT NULL CHECK (length(Sha256) = 64 AND Sha256 NOT GLOB '*[^0-9A-F]*'),
    PdfSha256 TEXT NOT NULL CHECK (length(PdfSha256) = 64 AND PdfSha256 NOT GLOB '*[^0-9A-F]*'),
    ConverterIdentity TEXT NOT NULL CHECK (length(trim(ConverterIdentity)) > 0),
    ConverterVersion TEXT NOT NULL CHECK (length(trim(ConverterVersion)) > 0),
    CHECK (json_type(Payload, '$.PackageId') IS 'text' AND json_extract(Payload, '$.PackageId') IS EvidencePackageId
        AND json_type(Payload, '$.Version') IS 'integer' AND json_extract(Payload, '$.Version') IS 1
        AND json_type(Payload, '$.PresentationSha256') IS 'text' AND json_extract(Payload, '$.PresentationSha256') IS PresentationSha256
        AND json_type(Payload, '$.Sha256') IS 'text' AND json_extract(Payload, '$.Sha256') IS Sha256
        AND json_type(Payload, '$.PdfSha256') IS 'text' AND json_extract(Payload, '$.PdfSha256') IS PdfSha256
        AND json_type(Payload, '$.ConverterIdentity') IS 'text' AND json_extract(Payload, '$.ConverterIdentity') IS ConverterIdentity
        AND json_type(Payload, '$.ConverterVersion') IS 'text' AND json_extract(Payload, '$.ConverterVersion') IS ConverterVersion
        AND json_type(Payload, '$.PdfBase64') IS 'text'
        AND coalesce(length(json_extract(Payload, '$.PdfBase64')), 0) > 0),
    FOREIGN KEY (EvidencePackageId) REFERENCES VeteransClaims_ReviewerPresentations(EvidencePackageId)
) WITHOUT ROWID;
```

```sql
CREATE TRIGGER ReviewerPresentation_LineageRequired BEFORE INSERT ON VeteransClaims_ReviewerPresentations
WHEN NEW.PreviousPackageId IS NOT NULL AND NOT EXISTS (
    SELECT 1 FROM VeteransClaims_ReviewerPresentations prior
    JOIN VeteransClaims_EvidencePackages old ON old.Id = prior.EvidencePackageId
    JOIN VeteransClaims_EvidencePackages current ON current.Id = NEW.EvidencePackageId
    WHERE prior.EvidencePackageId = NEW.PreviousPackageId
        AND old.ClaimIssueId = current.ClaimIssueId
        AND prior.EvidencePackageId != NEW.EvidencePackageId)
BEGIN SELECT RAISE(ABORT, 'Reviewer version requires an existing predecessor in the same claim issue'); END;
```

```sql
CREATE TRIGGER ReviewerPresentation_UniqueJsonKeys BEFORE INSERT ON VeteransClaims_ReviewerPresentations
WHEN EXISTS (
    SELECT 1 FROM json_tree(CASE WHEN json_valid(NEW.Payload) THEN NEW.Payload ELSE '{}' END) child
    JOIN json_tree(CASE WHEN json_valid(NEW.Payload) THEN NEW.Payload ELSE '{}' END) parent
        ON parent.id = child.parent
    WHERE parent.type = 'object'
    GROUP BY child.parent, child.key HAVING count(*) > 1)
BEGIN SELECT RAISE(ABORT, 'Frozen reviewer JSON contains duplicate object keys'); END;
```

```sql
CREATE TRIGGER ReviewerPresentation_NoReplace BEFORE INSERT ON VeteransClaims_ReviewerPresentations
WHEN EXISTS (SELECT 1 FROM VeteransClaims_ReviewerPresentations WHERE EvidencePackageId = NEW.EvidencePackageId)
BEGIN SELECT RAISE(ABORT, 'Reviewer presentation already exists'); END;
```

```sql
CREATE TRIGGER ReviewerPresentation_NoUpdate BEFORE UPDATE ON VeteransClaims_ReviewerPresentations
BEGIN SELECT RAISE(ABORT, 'Reviewer presentation is immutable'); END;
```

```sql
CREATE TRIGGER ReviewerPresentation_NoDelete BEFORE DELETE ON VeteransClaims_ReviewerPresentations
BEGIN SELECT RAISE(ABORT, 'Reviewer presentation is immutable'); END;
```

```sql
CREATE TRIGGER ReviewerPresentation_SourceRequired BEFORE INSERT ON VeteransClaims_ReviewerPresentations
WHEN NOT EXISTS (
    SELECT 1 FROM VeteransClaims_ReviewerPackageSnapshots s
    JOIN VeteransClaims_EvidencePackages p ON p.Id = s.EvidencePackageId
    WHERE s.EvidencePackageId = NEW.EvidencePackageId AND s.Version = 1
    AND p.ReviewerSnapshotVersion = 1 AND p.ReviewerSnapshotSealed = 1
    AND s.Sha256 = NEW.SourceSnapshotSha256)
OR EXISTS (SELECT 1 FROM VeteransClaims_ReviewerPackageOutputProvenance o
    WHERE o.EvidencePackageId = NEW.EvidencePackageId AND (o.SourceReviewDate != NEW.PackagePreparedDate OR (o.Format = 'docx' AND o.OutputSha256 != NEW.DocxSha256)))
BEGIN SELECT RAISE(ABORT, 'Reviewer presentation requires the sealed source and preserved date'); END;
```

```sql
CREATE TRIGGER ReviewerFrozenPdf_UniqueJsonKeys BEFORE INSERT ON VeteransClaims_ReviewerFrozenPdfs
WHEN EXISTS (
    SELECT 1 FROM json_tree(CASE WHEN json_valid(NEW.Payload) THEN NEW.Payload ELSE '{}' END) child
    JOIN json_tree(CASE WHEN json_valid(NEW.Payload) THEN NEW.Payload ELSE '{}' END) parent
        ON parent.id = child.parent
    WHERE parent.type = 'object'
    GROUP BY child.parent, child.key HAVING count(*) > 1)
BEGIN SELECT RAISE(ABORT, 'Frozen reviewer JSON contains duplicate object keys'); END;
```

```sql
CREATE TRIGGER ReviewerFrozenPdf_NoReplace BEFORE INSERT ON VeteransClaims_ReviewerFrozenPdfs
WHEN EXISTS (SELECT 1 FROM VeteransClaims_ReviewerFrozenPdfs WHERE EvidencePackageId = NEW.EvidencePackageId)
BEGIN SELECT RAISE(ABORT, 'Reviewer PDF already exists'); END;
```

```sql
CREATE TRIGGER ReviewerFrozenPdf_NoUpdate BEFORE UPDATE ON VeteransClaims_ReviewerFrozenPdfs
BEGIN SELECT RAISE(ABORT, 'Reviewer PDF is immutable'); END;
```

```sql
CREATE TRIGGER ReviewerFrozenPdf_NoDelete BEFORE DELETE ON VeteransClaims_ReviewerFrozenPdfs
BEGIN SELECT RAISE(ABORT, 'Reviewer PDF is immutable'); END;
```

```sql
CREATE TRIGGER ReviewerFrozenPdf_PresentationRequired BEFORE INSERT ON VeteransClaims_ReviewerFrozenPdfs
WHEN NOT EXISTS (SELECT 1 FROM VeteransClaims_ReviewerPresentations
    WHERE EvidencePackageId = NEW.EvidencePackageId AND Sha256 = NEW.PresentationSha256)
OR EXISTS (SELECT 1 FROM VeteransClaims_ReviewerPackageOutputProvenance o
    WHERE o.EvidencePackageId = NEW.EvidencePackageId AND o.Format = 'pdf'
        AND (o.OutputSha256 != NEW.PdfSha256 OR o.ConverterIdentity != NEW.ConverterIdentity
            OR o.ConverterVersion != NEW.ConverterVersion))
BEGIN SELECT RAISE(ABORT, 'Reviewer PDF requires the frozen presentation and matching historical outputs'); END;
```

```sql
CREATE TRIGGER ReviewerOutput_FrozenPresentationRequired BEFORE INSERT ON VeteransClaims_ReviewerPackageOutputProvenance
WHEN EXISTS (SELECT 1 FROM VeteransClaims_ReviewerPresentations p WHERE p.EvidencePackageId = NEW.EvidencePackageId
    AND (p.SourceSnapshotSha256 != NEW.SnapshotSha256 OR p.PackagePreparedDate != NEW.SourceReviewDate
        OR (NEW.Format = 'docx' AND p.DocxSha256 != NEW.OutputSha256)))
OR (NEW.Format = 'pdf' AND EXISTS (SELECT 1 FROM VeteransClaims_ReviewerPresentations WHERE EvidencePackageId = NEW.EvidencePackageId)
    AND NOT EXISTS (SELECT 1 FROM VeteransClaims_ReviewerFrozenPdfs WHERE EvidencePackageId = NEW.EvidencePackageId))
OR EXISTS (SELECT 1 FROM VeteransClaims_ReviewerFrozenPdfs p WHERE p.EvidencePackageId = NEW.EvidencePackageId
    AND NEW.Format = 'pdf' AND (p.PdfSha256 != NEW.OutputSha256
        OR p.ConverterIdentity != NEW.ConverterIdentity OR p.ConverterVersion != NEW.ConverterVersion))
BEGIN SELECT RAISE(ABORT, 'Reviewer output differs from frozen presentation'); END;
```
