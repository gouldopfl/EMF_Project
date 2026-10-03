# ADR-050: Long-Running Operation Coordination and Snapshot Consistency

## Status

Accepted

## Date

2026-10-03

## Context

Evidence ingestion and reviewer-package generation may run concurrently. A package
must consume one coherent set of inputs even when ingestion commits more Evidence
before rendering finishes. Neither operation may invalidate the other's resources.

Existing reviewer snapshots preserve assembled renderer inputs, but their initial
assembly is not a coherent database-wide read. ADR-047 provides exact bytes and a
physical revision for an individual read. ADR-048 provides durable ingestion,
adoption and recovery. Neither capability alone establishes an operation snapshot.

This decision adds a narrowly scoped operation boundary. It does not redesign
Evidence, reviewer snapshot V1, physical storage, security audit or workflow recovery.

## Decision: five guarantees

1. **Coherent immutable inputs.** Establish a declared coherent input snapshot.
   After establishment, later commits cannot enter the running operation.
2. **Bounded durable writes.** Writers commit admitted, bounded units and release
   persistence coordination between units.
3. **Durable recovery.** Restart recognizes confirmed durable work and resumes
   without duplicating effects or substituting newer inputs.
4. **Short coordination windows.** Expensive preparation and processing execute
   outside broad persistence write transactions and exclusive authority fences.
   Promotion reacquires the applicable short fence and validates its preconditions.
5. **Independent resource ownership.** Each operation owns its execution,
   persistence, cancellation and disposable-resource lifetimes. One operation
   cannot close or invalidate resources owned by another.

Consistency is required; instantaneous freshness is not. This decision permits
short serialization necessary for authoritative commits and existing security
protocols. It does not serialize entire operations or promise arbitrary starvation
bounds.

## Milestone 6A: one integrated proof path

The writer path is:

```text
EvidenceFileIngestionService
  -> ArtifactIngestionCoordinator
  -> SQLite ingestion persistence
```

It runs concurrently with the existing Veterans reviewer-package details/assembly
and document-output services. Tests use synthetic Evidence, actual SQLite
persistence and local security dependencies. Azure OpenAI and live Azure are off.
The writer and package use the same metadata database so the demonstration does
not avoid relevant contention by assigning each a different Evidence database.

Milestone 6A supplies only the scoped capture, retained inputs, durable operation
binding, bounded file progression and reconciliation needed for this path. Actual
assembly and output services consume captured dependencies; hand-built renderer
DTOs or mocked ingestion persistence do not satisfy the integrated proof.

## A. First supported snapshot capture boundary

### Supported profile and participating dependencies

The first profile covers **one pending reviewer package, one selected claim issue,
one SQLite metadata database, one ADR-047-capable content root, and a bounded set
of already adopted package artifacts and supporting dependencies**. Platform and
Veterans metadata used by this profile must reside in that same SQLite database.

Initial source media are self-contained UTF-8 text with stored fingerprints and
local extraction/print providers. Selected generated organizational material must
already exist as committed Evidence. This profile does not require AI, remote
regulatory retrieval, PDF/OCR source extraction, PAP parsing, or new derivation
during capture. Requested unsupported media or dependency branches are rejected,
not silently omitted or represented as empty data.

The capture includes the following bounded query closure. An empty collection or
absent optional value is itself captured from the same read view; it is never an
invitation to query current state later.

| Dependency | Captured scope |
| --- | --- |
| `IEvidencePackageRepository` | Package identity, claim/basis, purpose, reviewer role, ordered members, content roles and page selections; pending/legacy/sealed eligibility. |
| `IEvidenceRepository` | Selected Artifact rows, full metadata and fingerprints, provenance, relationships, and supporting Artifact rows/content needed for source names or existing member-scoped projections. |
| `IEvidenceClassificationRepository` | Member classification associations and definitions used for reviewer labels and appendix placement. These are domain classifications, not authorization authority. |
| `IClaimIssueRepository`, `IClaimRepository`, `IConditionRepository` | Selected issue, its claim/veteran linkage, and claimed/medical conditions actually consumed by assembly and cover resolution. |
| `IServiceConnectionRepository` | Selected basis/theory, required sibling bases, condition and medication associations consumed by the existing selected package path; explicit absence when no basis is selected. |
| `IMedicationRepository` | Linked veteran's ledger selection inputs, relevant entries/reconciliations, prescribed medication/history and clinical-context records needed by the member-scoped assembly queries. |
| `ISourceClarificationRepository`, `IClinicalProgressionRepository` | Selected issue's bounded annotation/progression query results and their member/source bindings. |
| `IRegulatoryRepository` | Local regulatory metadata/citation mappings consumed by the selected opinion-request path. |
| Existing Security classification authority | Actual resource classification and authoritative `ClassificationRevision` required for captured-source access; provisional authority cannot substitute for adopted Artifact authority. Its capture adapter must participate in the same SQLite view where a coherent classification binding is required. |
| Local regulatory-text input, when required | A complete immutable citation-to-text/provenance map supplied before capture by the trusted local composition; no network lookup or mutable provider callback. An empty required citation set is recorded explicitly. |

Medical-literature/reviewer-text-cache branches are outside the first profile.
They cannot fall back to live `IMedicalLiteratureRepository` reads or cache writes.
The same exclusion applies to any dependency outside the captured closure. A
profile admission check must reject a requested branch it cannot fully capture.
An absent optional provider cannot silently erase data required by the package.

Preparation inputs include explicit veteran display/preparer values, package
prepared/review dates, output format and the existing renderer/converter profile
identities applicable to output. Environment values and clocks may be resolved
once before capture, but cannot override retained inputs during restart or print.
Veterans interpretation and existing member/page filtering remain domain-owned.

### One coherent capture operation

`Capture` is one declared provider-supported operation with these boundaries:

1. Admit the profile, providers, closure and limits before entering capture
   coordination. Persist the reviewer-operation and `OperationSnapshotId`
   bindings in state `Capturing` before capture begins.
2. Read all participating SQLite metadata/query results through **one connection
   and one consistent read transaction**, whose first read establishes the
   metadata view. Separate repository connections or unrelated live reads do not
   qualify. Copy the bounded raw rows/results and required absence results.
   Within this declared capture window, obtain each required source through an
   admitted ADR-047 versioned content capability. Capture the exact bytes returned
   by that capability and the exact store-issued physical/content `Revision`
   returned for those bytes, together. Persistence/coordinator code MUST NOT reach
   beneath `EncryptedArtifactContentStore` or another security decorator. Honor
   any authoritative metadata-to-content revision binding; mismatch fails closed.
   Release all read coordination after the
   bounded copies. The admitted capability owns its existing security transforms;
   no separate domain interpretation, parsing, encryption, rendering or conversion
   is added to the read window. Its entire secured read/copy is subject to the
   capture duration and work limits.
3. Durably retain that complete raw capture, including the exact copied source
   bytes and revisions. Atomically bind its immutable references/hashes to the
   operation and advance `Capturing -> Materializing`. Partial capture cannot
   advance. The implementation may protect/encode the retained bundle outside
   the read window before this short persistence commit. No newer rows or source
   generations may be fetched to fill gaps after that window closes.
4. In `Materializing`, use only those retained copies. Verify copied identity,
   physical revision association and captured-byte hash; verify
   the returned content against the captured fingerprint algorithm/value and
   existing content-integrity requirements. Missing material, unsupported
   validation or mismatches prevent establishment. Fingerprint agreement alone
   does not permit another physical revision to replace the captured one.
   Metadata text summaries cannot replace source verification.
5. Durably retain the complete validated input bundle and final immutable capture
   manifest. Atomically bind their references/hashes and advance
   `Materializing -> Ready`. A reference to missing, partial or unverified
   material cannot make the snapshot ready. **Only `Ready` may be consumed by
   reviewer assembly/document generation.**

The admitted capture profile MUST enforce maximum snapshot members, dependency
count, aggregate returned/retained source bytes, retained metadata bytes and
read/copy work, plus a maximum capture/read-window duration. The deadline starts
before acquisition of capture coordination and covers all participating reads and
copies; repeated reads or waits cannot reset it. Providers must support bounded
termination and resource release or admission fails. Exceeding any bound prevents
`Ready`, terminates the read transaction and releases owned coordination/resources.
Persist a defined conflict/recovery or terminal-cleanup outcome for that capture.
A policy-approved retry starts a new operation and new `OperationSnapshotId`;
it cannot reuse the interrupted capture's identity or complete it with newer
Evidence. Following interruption, complete material captured within the admitted
bounds and already retained may still be reconciled under its original binding,
without reopening the lost read view. Material from a capture that exceeded its
bounds cannot be promoted to `Ready`.

The consistency scope is the step-2 SQLite metadata view, exact per-Artifact
bytes/revisions selected and copied during that capture window, and fixed
preparation inputs. Materialization verifies this recorded identity tuple; it
cannot select merely equivalent current bytes. Physical revisions identify the
actual copied generations, not a claim that all physical catalog entries were
observed at the same instant as the metadata view. Any authoritative link between
metadata and content is an additional required precondition, not replaced by a
fingerprint test. A historic creation receipt alone is not a current-content
revision binding after legitimate later mutations.

This is a scoped, exact-copy materialization protocol, **not** an arbitrary
cross-store snapshot protocol. It relies on existing admitted revision,
fingerprint and integrity semantics. Supporting dependencies must obey the same
binding; unbounded discovery, missing closure, unsupported combinations, provider
changes or an inability to supply the coherent SQLite view fail closed.

After `Ready`, existing assembly uses only the captured repository/source adapters
and fixed inputs. It produces the unchanged canonical reviewer V1 payload and any
selected text/printable renditions outside capture coordination. Persist their
validated identities/hashes as preparation results bound to the same
`OperationSnapshotId` before advancing the package's prepared checkpoint; they
do not amend the ready input manifest or introduce live inputs. Restart may reuse
these results or deterministically prepare them again from the same ready bundle.

### Exact binding and retained material

The immutable capture manifest records:

- `OperationSnapshotId`, reviewer-operation identity, capture profile/schema
  version and bounded provider/database/content-root identities (no protected
  paths or credentials in coordination state).
- Existing package, claim issue, claim, veteran, selected basis/theory and other
  contributing domain identities; exact membership/order/roles/page selections.
- Each contributing `ArtifactId`, provider-exposed metadata/Evidence revision,
  fingerprint algorithm/value, exact copied physical/content revision,
  captured-byte hash and verified content binding.
- Canonical captured metadata/query results, including empty/absent results,
  relevant provider-exposed provenance/relationship revisions and SHA-256
  bindings of the full retained records/query sets. Existing SQLite Artifact,
  provenance and relationship rows do not uniformly expose revision columns:
  those rows are copied from the same view and explicitly marked unversioned.
  Their hashes bind retained records, not invented concurrency/ABA revisions.
  A row hash/fingerprint proves equality of the canonicalized captured
  representation; it establishes no ordering, causality or provider revision
  history. `OperationSnapshotId` identifies the retained capture, not a database
  commit sequence. The single SQLite read view establishes metadata consistency;
  explicit identity/lineage bindings and actual provider revisions carry their
  own semantics. Equal hashes cannot substitute for those bindings.
  If an operation/provider requires a revision precondition that this profile
  cannot provide, admission fails; it cannot downgrade to hash equality.
- Authoritative protection-classification identity and `ClassificationRevision`
  wherever required, separately from domain evidence-classification records,
  physical revision, ingestion intent revision and ownership identity.
- Fixed preparation/regulatory inputs and applicable source/renderer/converter
  contract identities. Canonical ordering/encoding is versioned and deterministic.
  Subsequent preparation records bind selected rendition hashes and the existing
  V1 payload version/SHA-256 to this manifest without rewriting it.

Snapshot equality tokens MUST be computed from a deterministic, versioned
canonical representation. The representation contract fixes field inclusion,
ordering, encoding, null/absence handling and value normalization. Its version
MUST be included in the hashed representation and retained with the token;
comparison and verification use that declared version. A change in canonicalization
rules requires a new representation/schema version and MUST NOT silently
reinterpret older hashes. Older retained representations are verified under their
original rules; unsupported versions fail closed. Existing reviewer V1
canonicalization and hashes remain unchanged. Exact-byte source/rendition hashes
continue to cover their actual bytes, without canonicalizing away differences.

Persistence retains operation-owned materialized inputs and their immutable
binding. Protected content and domain metadata remain in governed protected
storage using existing security capabilities. Retain the returned material with
the protection required by the existing security architecture; this protocol
neither assumes the returned bytes are ciphertext nor redefines encryption
ownership. They are not plaintext execution journal fields or a new provenance
ledger. Coordination records contain only
bounded identities, references, hashes, state and safe failure categories.

After `Ready`, assembly, output and package recovery MUST consume these
retained inputs. They MUST NOT reread current Evidence, domain repositories,
environment values or regulatory providers to reconstruct or refresh the
snapshot. Existing current authorization checks remain permitted and required;
they do not authorize substitution of new rendering inputs.

Snapshot establishment precedes and is distinct from existing reviewer-package
sealing. The established input bundle is operation recovery state, not proof that
rendering, sealing, publication or delivery succeeded. Existing sealing still
checks package membership and snapshot equality and rejects conflict. It does
not silently recapture newer inputs. Reviewer V1/presentation/PDF contracts and
historical rows are unchanged.

Retained material becomes eligible for release only after either:

- the operation completes, required renderer inputs are durably preserved under
  existing package contracts, and no remaining retry/reconciliation/audit
  obligation needs the operation copy or its exact revision/source binding; or
- an explicitly authorized terminal abandonment ends resumability and all
  dependent reconciliation obligations are resolved.

Both require applicable retention policy and a persisted, fenced release decision.
Cancellation, process death, age, `RequiresReview`, or an absent process-local
handle alone never permits release. An interrupted capture remains bound to its
original operation/snapshot identity: it resumes only from sufficient retained
material or performs authorized non-destructive cleanup/terminal review of that
same capture. It MUST NOT reread newer Evidence and declare the old snapshot
complete. A separately authorized new capture uses new operation/snapshot
identities rather than rebinding the old.

| Capture state | Consumption and restart |
| --- | --- |
| `Capturing` | Not consumable. Complete raw capture must be durably bound before advancing. A process-death loss of the coherent view with incomplete retained inputs cannot be filled from current Evidence; resolve/clean up this capture or require review. |
| `Materializing` | Not consumable. Resume validation/materialization from the retained raw capture and its exact source revisions; no live reconstruction. |
| `Ready` | Consumable. Ready bindings are immutable; repeated recovery retains the same snapshot. |

Normal progression is `Capturing -> Materializing -> Ready`. Failure/cancellation
does not masquerade as a forward transition; cleanup/review and final release are
explicit outcomes and do not restore eligibility to recapture under that identity.

## B. Durable operation and recovery binding

The following identity roles remain distinct. Names below describe required roles,
not a mandate to introduce a separate infrastructure subsystem for each.

| Identity/state | Minimum durable binding |
| --- | --- |
| Parent long-running ingestion operation | Stable identity; bounded admitted file plan/version/hash; ordered child bindings; checkpoint revision/current ownership token; confirmed-unit frontier and terminal/review state. |
| Each child file ingestion operation | Stable child identity and ordinal; parent identity; immutable retained-input identity/reference and versioned request/content equality binding; classification/authorization binding; provisional Artifact identity; explicit mapping to its existing authenticated ADR-048 operation and authorized-operation identities. |
| ADR-048/047 mutations | Preserve existing ownership, classification revisions, create/cleanup operation identities, exact encrypted candidate bindings, physical revisions, receipts, adoption/deduplication outcome and audit obligations. Persist mappings; retries do not allocate replacements. |
| Reviewer/package operation | Stable identity; existing package identity; immutable request/profile binding; `OperationSnapshotId`; retained manifest/V1 references and hashes; checkpoint revision/ownership; confirmed preparation/sealing/output state and safe review state. |
| `OperationSnapshotId` | One immutable input binding; capture profile/version, manifest hash, retained-material references, `Capturing`/`Materializing`/`Ready` and fenced release state. It is neither package identity, physical revision, mutation identity nor authorization. |

Each child binding is persisted **before invoking** ingestion. Restart must reuse
the same logical request, provisional identity and authenticated ingestion context,
not call a new-ID path as though the file were a new operation. Existing explicit
ADR-048-to-ADR-047 identity mappings remain valid; equal representations do not
make the different identity roles interchangeable. No new attempt/provenance ledger
is required.

Admission MUST durably retain immutable child input, or a durable representation
sufficient to reproduce the exact admitted ingestion request, before execution can
depend on it. Bind parent identity, child identity, retained-input identity,
versioned request/content equality tokens, original provenance/source descriptor,
classification/authorization context and existing ADR-048/047 mutation identities.
The representation must preserve the exact bytes and request fields needed for
recovery, including existing exact candidate bindings when applicable. A path and
hash alone are insufficient. Original source descriptors remain provenance;
recovery MUST NOT reopen the originally supplied path to replace admitted input.
Moving, deleting or changing that source cannot change the admitted child request.
Missing retained material requires reconciliation/review rather than substitution.
Current authorization/classification checks still apply; retained context is not
a bearer grant. Protected input belongs in governed storage, not duplicated
plaintext coordination fields. Existing durable staged material may satisfy this
requirement when its lifecycle and binding support the entire required recovery.

Writer progression follows this rule:

1. Load the recorded child binding and invoke or reconcile its existing lifecycle.
2. Confirm committed provisional adoption, or the verified durable canonical
   deduplication result, using ADR-048 adoption/result evidence and ADR-047 receipts.
3. Atomically advance the parent's confirmed frontier under its current expected
   checkpoint revision and ownership token. Advance only over confirmed children;
   gaps, unknown outcomes and cleanup-only outcomes cannot count as adopted work.

If effect commit and parent checkpoint are separate transactions, receipt/adoption
reconciliation closes the acknowledgement gap. A crash after adoption but before
checkpoint acknowledgement recognizes the original effect and advances once; it
does not re-ingest with new identities. Pending audit delivery remains pending
durable work even when the adopted-unit frontier advances. This decision does not
reinterpret lifecycle completion or permit compensating deletion of adopted data.

Package progression advances to `Ready` only when the complete retained
inputs and binding commit durably. Later checkpoints advance only after their
existing authoritative outcomes are confirmed. Restart retains the same package
and snapshot identities; lost acknowledgements reconcile existing persisted
snapshot/seal/output bindings rather than replace them.

Recovery is monotonic and idempotent: confirmed frontiers do not move backward,
immutable bindings do not change, and identical reconciliation does not repeat
effects. Current ownership/revision must be checked at checkpoint promotion so a
former worker cannot advance state after transfer. Process-local locks, open
transactions and in-memory objects are never recovery authority. Unknown or
contradictory state becomes durable review work; it is not permission to replay,
delete, refresh the snapshot or claim success. ADR-023 through ADR-025 and
ADR-029/030/033 remain authoritative for workflow recovery and claim decisions.

## Detached work and bounded writer unit

The general rule is:

```text
Prepare -> Detach -> Revalidate/Promote -> Reconcile
```

Prepare a bounded plan and durable identity binding; detach copied work from
transaction/authority lifetimes; perform expensive work outside broad fences;
reacquire the applicable short fence, revalidate and promote; reconcile durable
outcomes before advancing progress or reporting completion.

Parsing, encryption, AI, rendering and conversion MUST NOT execute inside broad
persistence write transactions or exclusive authority fences. Revalidation before
authoritative promotion includes the ownership, lifecycle, classification,
revision, candidate and idempotency assumptions required by the existing ADRs.
Releasing a fence is not permission to use a stale authorization or precondition.
ADR-047 individual read/GC safety and ADR-048 adoption/cleanup exclusion remain
mandatory. This ADR does not prescribe a replacement lifecycle or implement the
deferred rewrap/audit protocols.

For Milestone 6A, **one admitted file is the initial durable writer unit**. The
admission profile MUST specify positive, enforced limits for file/plaintext and
encrypted-candidate size, preparation memory/work, per-unit execution/recovery
budget and persistence/fence waiting. Capture additionally enforces the member,
dependency, byte, work and capture/read-window duration limits defined above.
Limit checks occur
before allocating unbounded work, with checks during processing where required.
Budget exhaustion cannot cancel already committed effects or their recovery duty.

The first implementation must declare concrete validated limits and test their
boundaries; this ADR does not choose deployment-wide numeric defaults. One file
cannot mean unbounded work. Persistence coordination is released before processing
the next file. No generalized batching engine is required, and existing atomic
adoption boundaries are not split to obtain smaller checkpoints.

## Resource ownership

Each concurrent operation owns its own execution scope, SQLite connection and
transaction lifetimes, cancellation lifetime and disposable resources. Shared
services have explicit host ownership; operation disposal cannot dispose them.
Existing bounded service recovery/delivery budgets may outlive caller cancellation
without taking ownership of another operation's resources.

Cancellation/disposal of one operation MUST NOT clear, invalidate or close resources
owned by another. Global SQLite pool clearing is not an application coordination
mechanism. Rendering and conversion retain no ingestion transaction. Small capture
or commit windows may contend; the required demonstration is concurrent progress
after those windows, not zero waiting or an arbitrary starvation bound.

## Layer ownership and existing authority

| Layer | Ownership in this slice |
| --- | --- |
| Core | Neutral operation/snapshot identity values, bounded-unit/checkpoint references and capability/outcome contracts genuinely needed here. No Veterans models, SQL types or concrete Security dependencies. |
| Persistence and owning adapters | Coherent scoped capture; retained materialized snapshot state; durable long-operation/checkpoint state; atomic bindings and revision/ownership checks. Domain schema remains owned by its adapter under ADR-014/016. |
| Orchestration | Execution coordination, file-unit progression, capture consumption, restart and reconciliation. Existing workflow recovery responsibilities remain separated. |
| Security | Existing authorization, authoritative classification, encryption/staging and audit behavior remain authoritative under ADR-017/031/047/048/049. Local test dependencies do not bypass them. |
| Veterans extension | Reviewer interpretation, domain-specific capture projection/query requirements and presentation through existing assembly/output contracts. Veterans concepts do not move into Core. |

Snapshot hashes and ownership tokens grant no access or export permission. Internal
read/render and actual export remain distinct under ADR-017. Current authorization
and classification revalidation apply at their existing boundaries, even for a
retained historical input. No security policy, lifecycle or canonical audit truth
is redefined by this decision.

## Normative Milestone 6A acceptance demonstration

The integrated test MUST:

1. Commit initial synthetic Evidence through the actual ingestion path.
2. Establish and durably retain the package snapshot through the coherent capture.
3. Pause package rendering at a deterministic barrier after `Ready`.
4. Continue ingesting additional files into the same metadata database.
5. Prove additional child effects commit while rendering remains paused.
6. Resume existing package rendering/document output.
7. Prove output contains only the original snapshot, including its original
   metadata/domain projections and source content; later commits do not enter it.
8. Kill/restart the writer around effect commit and checkpoint acknowledgement.
9. Prove recognition of committed children, stable identities and no duplicate
   effects after repeated recovery.
10. Restart the package operation from retained inputs, preserving its snapshot;
    make current Evidence unavailable/changed to detect forbidden reconstruction.
11. Cancel/dispose either operation in separate cases and prove the other continues
    with valid owned resources and persistence progress.
12. Use real task concurrency and independent process-death/restart tests wherever
    recovery crosses a process boundary. Sequential exception injection alone is
    insufficient. Bound waits and make failed progress observable.

Required supporting regressions are:

| Regression | Required result |
| --- | --- |
| Metadata/domain mutation during capture, including changed absence results | One coherent SQLite view; no mixed live lookup results. |
| Source replacement/missing content or fingerprint mismatch during capture/materialization | Bind the copied physical revision as well as fingerprint; never select a different current revision, including an identical-byte replacement. Use retained captured bytes or fail without `Ready`. |
| Provider-exposed metadata/provenance/relationship/classification revision mismatch | Reject the inconsistent binding; do not downgrade a required revision precondition to fingerprint/hash equality. |
| Equal canonical row hashes with different identity, lineage or provider revision history | Equality does not establish ordering/causality or authorize substitution; enforce the distinct captured bindings. |
| Canonical representation determinism and version transition | Equivalent inputs under one version yield the same token regardless of culture/map insertion order; changed rules require a new version. Original-version verification remains valid, and unknown versions or silent reinterpretation of older hashes are rejected. |
| Crash in `Capturing` or `Materializing` | Resume only the same retained capture or resolve/clean it up; never reread newer Evidence and declare the old snapshot ready. |
| Unsupported provider/combination, media or uncaptured dependency | Reject before claiming a coherent snapshot; no permissive fallback. |
| Capture member/aggregate-byte/work/read-window limits exceeded | No `Ready`; release the read transaction and owned resources within the enforced budget. Any approved fresh capture has new identities; no long-lived view or live gap filling. |
| Decorated ADR-047 versioned reads | Bind exactly the returned bytes and store-issued revision through the admitted capability; no direct access beneath the security decorator or assumed ciphertext/decryption ownership. |
| Crash/rollback during retained-input publication | No ready checkpoint pointing to partial/missing material; no automatic current-Evidence recapture. |
| Lost acknowledgement after snapshot publication or child adoption | Recognize the original durable binding/outcome; advance progress idempotently. |
| Changed request under an existing child/snapshot identity | Reject conflicting binding; do not overwrite or allocate an implicit replacement. |
| Original child source moved, deleted or replaced after admission and before restart | Reconcile/resume the same retained immutable input and exact request; never reopen the path to ingest different bytes. Missing retained input fails closed. |
| Stale worker after ownership transfer | Checkpoint promotion rejected; confirmed progress remains monotonic. |
| Expensive local encryption/extraction/render/converter paused | No broad ingestion write transaction/authority fence held around that work; unrelated admitted persistence can progress. |
| Size/work/wait budgets exceeded | Bounded failure with preserved committed effects and durable recovery/review work. |
| Cancellation, adopted data and audit delivery failure | Preserve ADR-048 irreversible adoption and ADR-049 pending obligations. |
| Snapshot release racing restart | Retain valid recovery inputs or reject an explicitly terminal/released operation; never reconstruct from current state. |
| Existing reviewer snapshot/seal/provenance behavior | Preserve V1 integrity, membership conflict checks, immutable history and existing output/build provenance guarantees. |

Reuse existing ingestion lifecycle/process recovery, versioned-store, workflow
concurrency and reviewer snapshot/provenance tests. The integrated output must use
the real document-output service and DOCX renderer. A local converter double may
test conversion coordination; it does not establish real PDF layout equivalence.
Existing PDF/layout acceptance requirements remain applicable when those behaviors
change. Process-kill tests do not claim power-loss durability.

## Deferred architecture, not acceptance prerequisites

| Deferred architecture | Milestone 6A boundary |
| --- | --- |
| Historical revision reads or durable generation pins | Retain bounded materialized inputs; existing individual ADR-047 reads remain authoritative. |
| General cross-store snapshot protocol | Prove the declared single-SQLite-view, exact-copy/revision-bound profile only. |
| Broad rewrap redesign | Rewrap remains under existing authority; only the chosen ingestion path's detached-work rule is in this implementation slice. |
| ADR-028 audit-chain optimization | Preserve canonical audit/delivery semantics. No constant-cost append or chain-wide lock-duration claim. |
| General scheduler/priority framework | Prove concurrent progress between bounded units. No priority policy or arbitrary starvation bound. |
| CPAP importer | Use existing file ingestion for the integrated proof. |
| Inventory/email/ZIP/Veterans derivation migration | Later consumer rollout; this decision does not claim those callers already comply. |
| New provenance ledger | Reuse existing Evidence provenance, receipts, canonical audit and reviewer hashes/output provenance. |
| Reviewer snapshot redesign | Preserve V1 and existing presentation/PDF/sealing contracts; retained capture is operation state. |

Extending media/dependency profiles or persistence providers requires an explicit
scope/capability review and regressions. Deferred work does not weaken the five
guarantees on the supported path and is not a prerequisite for accepting this ADR.

## Consequences and implementation status

The first slice can prove consistent package inputs, durable file progression and
independent operation lifetimes using existing services. Bounded materialization
costs memory/storage and may reject large or unsupported packages. Capture may
fail when current source content no longer matches its captured metadata; freshness
is not obtained by silently retrying against newer inputs under the same identity.

This decision is not evidence of implementation or passing regressions.
No production or test changes accompany its authoring. Acceptance approves the
scoped architecture; Milestone 6A completion requires the integrated demonstration.

## Related architecture

- [ADR-012](ADR-012-domain-extension-platform-boundary.md),
  [ADR-014](ADR-014-domain-extension-persistence-boundary.md),
  [ADR-015](ADR-015-persistence-provider-selection.md),
  [ADR-016](ADR-016-provider-owned-versioned-schema-migrations.md)
- [ADR-017](ADR-017-protected-regulated-information-boundary.md),
  [ADR-031](ADR-031-resource-neutral-authorization.md)
- [ADR-023](ADR-023-workflow-resume-restart-semantics.md),
  [ADR-024](ADR-024-workflow-recovery-policy.md),
  [ADR-025](ADR-025-workflow-recovery-coordinator-boundary.md),
  [ADR-029](ADR-029-workflow-optimistic-concurrency.md),
  [ADR-030](ADR-030-workflow-activity-claim-recovery.md),
  [ADR-033](ADR-033-workflow-operation-idempotency.md)
- [ADR-043](ADR-043-long-running-operation-progress-reporting.md)
- [ADR-047](ADR-047-versioned-artifact-content-mutation.md),
  [ADR-048](ADR-048-artifact-ingestion-ownership-and-recovery.md),
  [ADR-049](ADR-049-security-sensitive-mutation-audit-delivery.md)
- [Reviewer snapshot V1](REVIEWER_PACKAGE_SNAPSHOT_V1.md)
- [Reviewer package determinism](../REVIEWER_PACKAGE_DETERMINISM_CONTRACT.md)
