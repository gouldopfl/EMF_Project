# Reviewer package output reuse V1

## Architecture and separation

The starting checkpoint is `ef539697`. `VeteransConsoleCommand.RunReviewerPackageAsync`
previously retained the package owning a reviewed summary when its existing semantic
summary key matched. That key does not represent every renderer input. Historical
output correctly restored the sealed V1 manifest, but the selection could therefore
mistake historical content for a requested current view.

The existing summary key and provenance checks are unchanged. After they succeed,
`VeteransReviewerPackageReuseService` makes a separate output decision. The console
prefers the latest eligible summary owner. It prepares a fresh package through
`EvidencePackageService`, including existing page-selection inheritance, without
persisting it yet. Current details use the existing assembly services and regulatory
provider. The existing V1 serializer captures those inputs once.

No schema, migration, second snapshot store, renderer, encryption, or persisted
fingerprint column is introduced. `ReadReviewerSnapshotAsync` exposes legacy versus
pending state using the existing single-query persistence read. Historical reads
continue to reject legacy reconstruction.

## Exact fingerprint contract

`VeteransReviewerPackageOutputReuse.Fingerprint` first restores and validates the
supported V1 manifest, including integrity and completeness. It projects that explicit
manifest contract, rather than serializing renderer DTOs:

1. Replace the package ID and every member's owning package ID with the fixed string
   `output-reuse-identity`. Artifact IDs, roles, order, selections, claim, purpose,
   reviewer role and service-connection basis remain unchanged.
2. Replace each regulation's `RetrievedUtc` with its calendar date formatted
   invariantly as `yyyy-MM-dd`, preserving the date displayed by the renderer without
   converting its offset to UTC. Subday time is not displayed.
3. Preserve every other V1 field, including explicit nulls, ordered arrays, metadata,
   literature/classification, medical opinions and requests, annotations/progression,
   medication reconciliation, clarifications, and regulatory text/source/version/hash.
4. Use the existing V1 canonical JSON writer and hash UTF-8 text consisting of
   `reviewer-output-reuse-v1`, one LF, and that canonical projection with SHA-256.
   The V1 contract version remains inside the projection. Unsupported versions fail.

This fingerprint is transient. The original sealed manifest ID/version/SHA remain
the historical authority. Fingerprint equality permits retaining that exact sealed
package; it does not overwrite or normalize its persisted manifest. A summary-key
match alone never authorizes immutable output reuse.

| Requested/current condition | Decision after summary reuse succeeds |
| --- | --- |
| Same V1 inputs; different package identity or retrieval time on the same displayed date | Reuse original sealed package and manifest |
| Different displayed regulatory retrieval date | Create fresh pending package |
| Changed regulatory text, version, source URI or source hash | Create fresh pending package |
| Changed metadata, pages/order, medication/reconciliation, clarification/progression, literature/classification, opinion/request, role/purpose/basis or any other retained V1 value | Create fresh pending package |
| Legacy version 0 or pending owner | Reuse summary only; create fresh pending package |
| Corrupt, incomplete, identity-inconsistent or unsupported sealed state | Fail closed; do not rebuild or reinterpret history |
| Current input/regulatory lookup fails | Fail; do not silently choose historical output |
| DOCX versus PDF/both | Same content decision; conversion runs for requested format |
| No document output requested | Reuse summary into fresh pending package without claiming output equivalence or querying regulatory providers |

## First output, concurrency and failures

On a miss, the existing repository atomically adds the fresh package and members.
The selection carries the **exact comparison snapshot** to document output. Output
restores that snapshot and does not re-query mutable providers or recapture inputs.
On a hit, it carries the original sealed manifest instead. A conflicting seal between
selection and rendering is rejected. Membership/selections are revalidated by the
existing seal transaction and protected by existing post-seal triggers.

The existing output lifecycle remains: render DOCX and any required PDF, then
atomically seal, then publish. Failed rendering/conversion leaves a new package
pending. Failed publication after sealing permits explicit historical retry from the
sealed manifest. A later *current-view* request performs a new comparison. It may
choose a new identity if inputs changed. Direct historical output remains a separate
explicit operation and never reinterprets the package as current.

## Verification and limitations

Focused tests exercise renderer-input changes, regulatory date/time/source/version,
legacy/pending/corrupt/unsupported states, preserved summary membership and page
selections, all three formats, exact prepared-input rendering despite later changes,
provider failure and conflicting concurrent sealing. Providers and converters are
synthetic; no Azure or regulatory live calls are needed.

The comparison intentionally retains the complete V1 payload except the two explicit
normalizations. Non-displayed fields already in V1 can cause conservative misses.
It reassembles current inputs to avoid maintaining a second dependency inventory;
this costs reads and may require regulatory retrieval. Assembly is not a cross-store
transactional view: it freezes the values observed during that capture. Subsequent
provider changes cannot alter the selected first output.

Only the selected summary owner's seal is compared. A newer pending owner can cause
a conservative miss even if an older equivalent sealed package exists. Concurrent
requests may create separate valid fresh packages; this is not global deduplication.
The fingerprint covers meaning under V1, not DOCX/PDF bytes or renderer/converter
binary versions. Renderer contract changes require an intentional version decision.

Validation completed: 30 focused tests passed; the broader reviewer/package/schema
selection passed 614 tests with 10 layout skips; the full solution passed 2,645 tests
with 12 skips (10 opt-in layout tests and two Azure live tests), with zero failures.
Azure OpenAI and Azure Monitor live-test gates were explicitly disabled. Existing
layout test policy is unchanged. No defects or code fixes were needed during broader
validation. No medical/legal conclusions or diagnosis terminology are changed, and
the existing summary reuse key has no required follow-on redesign here.
