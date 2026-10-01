# EMF hardening review

Baseline: `52f16384158ab06ae1ff175d090868f662431906` (`52f16384`). Date: 2026-09-25.

This phase is hardening of the existing architecture. The tracked working tree was clean at the authoritative HEAD; the only initial untracked path was generated `EMF_Output/`. Source, test, persistence, security, and rendering contracts were reviewed before edits. No commits, Azure OpenAI calls, Key Vault calls, medical-data uploads, live-database modifications, generated-output modifications, or backup deletions were performed. Public standards/advisory lookups contained no medical data.

## Findings and enforced invariants

| ID / severity | Defect and why it matters | Remediation and affected paths | Adversarial coverage |
| --- | --- | --- | --- |
| HARD-01 / High integrity | Native page-range rendering accepted gaps or truncated ranges. A clinical source could also avoid boundary enforcement through its filename/type; missing lineage could fall back to extracted text. This could omit evidence or include neighboring records without a visible failure. | `VeteransReviewerPrintableSourceResolver`: contiguous complete ranges, explicit provenance, supported bounded formats, clinical record boundaries independent of parent filename, and no native fallback for missing/unsupported declared bounds. Existing native excerpt implementation remains the sole clipping implementation. | Missing first/interior/last pages; absent parent; missing ranges; unsupported parent; contradictory PDF/DOCX/binary derivative format; renamed Blue Button source; native failure and adjacent records. |
| HARD-02 / High integrity | Medication assembly read veteran-wide current and historical ledgers and clinical contexts without package scope; later claim evidence could appear in an existing reviewer package. Clarifications also used a parent-to-derived fallback. | Reviewer medication APIs now require package details. The existing current-ledger selector filters the authorized source set **before** choosing the latest ledger. Shared factual scope requires a direct underlying member, matching artifact/content, valid lineage range, and any persisted page selection. Clarifications require direct membership. No new package members are added. | Empty package plus later reconciled medication; member ledger preserved after a newer nonmember ledger arrives; nonmember parent/context exclusion; source selection boundaries; missing artifact/content rejection. |
| HARD-03 / High integrity | Loader could silently remove a literature member after current review classification no longer applied, and accepted inconsistent package identity/roles until a later renderer boundary. | `VeteransReviewerPackageDetailsService` rejects inconsistent package IDs, rows, roles and duplicates before content lookup; stale literature review fails closed instead of omitting the persisted member. | Wrong package/row IDs, duplicate member, unsupported role, changed literature basis, later claim-only literature. |
| HARD-04 / Medium integrity | Clinical progression could narrate pages deliberately excluded from reviewer output, or silently omit a member with missing content. | Progression validates source ranges against persisted selections, rejects unlocated progression for selected evidence, and rejects missing member content. Existing factual progression renderer and medical/nexus wording are retained. | Selected/unselected pages; missing event range; direct-member/parent exclusion; factual versus nexus and terminology regressions. |
| HARD-05 / Medium correctness | Reuse keys hashed formatted source output with GUIDs and repository order, used ambiguous text separators, and insufficiently identified semantic graph associations. | Versioned semantic reuse input resolves known graph identities to content values, canonically sorts unordered collections, keeps full evidence text and duplicate multiplicity, includes prompt guardrails, rejects contradictory/dangling inputs, and preserves which fact belongs to which basis. This is a hashing contract, not another renderer. Old keys intentionally miss. | Rename graph IDs; reorder sources/classifications/conditions; tied event order; relevant changes; duplicate multiplicity; delimiter/newline distinctions; conflicting same-ID sources; unresolved requirements; fact-to-basis reassociation. |
| HARD-06 / High integrity | Migration runners could fill a ledger hole by replaying an old migration against a newer schema; concurrent initializers could act on stale ledger reads. | Veterans and intelligence SQLite migrators require a contiguous supported prefix and recheck it under each migration write transaction. Existing migration histories were not rewritten. | Missing middle migration rejected before replay; eight concurrent initializers; existing migration idempotency/version/name regressions. |
| HARD-07 / Medium integrity | A stale positive-revision intelligence state could insert a missing/deleted row, defeating optimistic concurrency. | `SqliteIntelligenceAgentStateStore`: only revision zero creates a new row; positive revisions require an existing matching state. Negative revisions rejected. | Missing/deleted state resurrection and normal compare-and-swap tests. |
| HARD-08 / Medium integrity | Persisted progression reads bypassed the repository's write validation and used culture-sensitive dates/ID tie ordering. | Existing validation now applies to reads, dates use invariant ISO parsing/writing, and tied rows use factual ordering. | Corrupted event types/page bounds/dates and tied semantic row order. |
| HARD-09 / Medium correctness | Equally ranked medication snapshots used `First()` despite differing factual fields; insertion order could choose different indication, source page or prescriber. | Conflicting equally ranked snapshots fail closed; exact semantic duplicates can collapse without a GUID tie-break. Package membership itself is unchanged. | Ten cases across both insertion orders: exact duplicates and conflicting indication, source page, facility, or date. |
| HARD-10 / Medium availability | A selected range ending at `int.MaxValue` overflowed the page loop. | Existing selector uses a wider loop counter and retains its range-size guard. | Maximum singleton range and excessive-range tests. |

Security findings SEC-01 through SEC-10, their scenarios, mitigations, safeguard mappings, and deployment gaps are detailed in [SECURITY_HIPAA_NIST_AUDIT.md](SECURITY_HIPAA_NIST_AUDIT.md). Implemented fixes include identity-bound encrypted-store envelopes; private Unix content-store/temp creation; symlink rejection; bounded external diagnostic facts; provider/process/workflow diagnostic redaction; shared live-call accounting-rate validation; and exact audit-input binding before bounded evidence promotion.

## Architecture and contract review

The package renderer, native excerpt restriction helper, page selector, current-ledger service, cryptographic envelope implementation, migration histories, and audit records remain the existing mechanisms. New small scope/hash helpers make cross-module invariants explicit; they do not create alternate renderers, encryption schemes, evidence stores, or migration ledgers.

Reviewer medication projection now takes `VeteransReviewerPackageDetails` instead of a bare `EvidencePackage`, because the latter cannot express authoritative evidence membership or reviewer page selection. Assembly and tests were updated together. The shared non-reviewer current-medication API retains its normal unscoped use; package assembly supplies an explicit source set. Direct references are required for factual reviewer projections: parent source membership is not inferred through a derived item.

Artifact selection and source page selection are distinct. The entire intended native range must be present before the stored reviewer subset is selected. Missing/inconsistent native input does not justify extraction or neighboring-record substitution. A source line range is not repurposed as a PDF page coordinate.

Duplicate package artifact IDs and conflicting content roles are rejected. Separate members that share source material are preserved; deduplicating membership would change the package. Semantic hashing preserves duplicate evidence multiplicity. Medication projection only collapses equivalent snapshots under its existing grouping policy; contradictory tied facts now fail rather than being chosen by row order.

Reviewer-facing diagnosis text continues to use **Bilateral pes planus**. Factual observations, source clarifications, existing medical opinions, and the request for an independent nexus opinion remain distinct. This phase does not generate new medical/legal/adjudicative conclusions.

## Validation

All test commands explicitly disable Azure OpenAI and Azure Monitor live tests. No provider credentials were used. Security tests use synthetic payloads, local fake processes, and in-memory providers.

| Run | Result | Evidence |
| --- | --- | --- |
| Consolidated reviewer/package/native/selection/reuse focused | 268 passed, 0 failed, 0 skipped | `/tmp/emf-hardening-consolidated-focused.log` |
| Semantic reuse, migrations, agent state and progression persistence focused | 47 passed, 0 failed, 0 skipped | `/tmp/emf-hardening-reuse-persistence-focused.log` |
| Content store, encrypted envelopes and security monitoring focused | 44 passed, 0 failed, 0 skipped | `/tmp/emf-security-focused.log` |
| External call, diagnostic, temporary file and promotion focused | 48 passed, 0 failed, 0 skipped | `/tmp/emf-live-hardening-focused.log` |

Broader module/Veterans run: **1,233 passed, 12 skipped, 0 failed**, 8 minutes 33 seconds. Evidence: `/tmp/emf-hardening-broad.log`. Its skips were 10 opt-in layout tests and 2 prohibited Azure live tests.

Full solution run: **2,596 passed, 2 skipped, 0 failed**, build completed with **0 warnings and 0 errors**, 9 minutes 59 seconds. Main EMF.Tests: 2,571 passed / 2 skipped; ArchitectureAuditor: 22 passed; DeveloperAssurance: 3 passed. All **10 synthetic LibreOffice layout tests passed**. Only Azure OpenAI and Azure Monitor live tests were skipped. Evidence: `/tmp/emf-hardening-full.log`; the test command exited 0.

```bash
env -u EMF_REVIEWER_LAYOUT_ARTIFACTS EMF_AZURE_OPENAI_LIVE_TESTS=false EMF_AZURE_MONITOR_LIVE_TESTS=false EMF_REVIEWER_LAYOUT_TESTS=true dotnet test EMF.sln --no-restore --verbosity normal
```

An overlapping invocation left cancellation text beyond the active process's log offset. The mixed output was preserved as `/tmp/emf-hardening-full-overlapping-output.log`; only its stale tail was removed from the active log. The active process continued without restart, subsequently produced the successful totals above, and exited 0. This was a log collision, not a failing assertion. No test result is inferred from the abandoned invocation.

Final `git status --short`, `git diff --stat`, and `git diff --check` were run. The whitespace check passes; HEAD remains the authoritative baseline. Complete review patch (tracked and new source/tests/reports, excluding pre-existing generated `EMF_Output/`): `/tmp/emf-hardening-final.patch`. Status and diff-stat snapshots: `/tmp/emf-hardening-git-status.txt` and `/tmp/emf-hardening-complete-diff-stat.txt`.

Complete change set, including new source/tests and the three review/handoff documents: **55 files changed, 1645 insertions(+), 341 deletions(-)**. Generated `EMF_Output/` is excluded.

Public dependency advisory scan: `dotnet list EMF.sln package --vulnerable --include-transitive --format json` completed successfully: 25 projects, zero reported vulnerability entries, zero reported problems. This does not cover unknown vulnerabilities, every native/OS package, or deployment security. Evidence: `/tmp/emf-hardening-vulnerabilities.json`.

## Remaining concerns and intentional deferrals

1. **High: full persisted-output immutability is not implemented by the current schema.** Packages preserve member IDs/roles/selections, but not historical snapshots of every member's metadata, classification, medical request, clinical annotation, medication reconciliation, or regulatory text. A later change attached to an already-member source may still change meaning on rerender. Membership filtering closes new nonmember leakage; it cannot recover history that was never stored. A versioned snapshot/manifest and an explicit legacy-package migration policy are needed before claiming immutable or byte-reproducible historical output. No inferred creation timestamp or fabricated historical state was introduced.
2. **High: reuse is not a complete rendered-output fingerprint.** Console uses the summary reuse decision to retain a package ID, then assembles current rendering inputs. Renderer-only state absent from the key's API (for example selections, native boundary anchors, reconciliations, and clarifications) remains outside that key. Unknown `Timeline.ReferenceId` values remain conservatively significant because the model does not distinguish external references from opaque storage IDs. The new tests prove determinism for known semantic graph links, not all possible future/unknown identity fields. Future work must distinguish summary reuse from immutable package-output reuse using an explicit persisted contract.
3. **High: legacy ciphertext compatibility requires controlled migration.** The artifact store rejects v0/v1 identity-unbound envelopes. Generic crypto legacy-decryption APIs remain available for explicit migration, but no automatic identity assumption or live-content migration occurred. Review trusted provenance and backups before migration; do not enable a fallback.
4. **High: deployment confidentiality remains unresolved.** Artifact encryption does not encrypt PHI in SQLite metadata, WAL/SHM, database backups, DOCX/PDF/ZIP outputs, memory, swap or crash dumps. Existing group-readable outputs/databases were inventoried without modifying them. Windows ACLs, full-volume encryption, access ownership, retention, approved sharing, recovery and incident response require operator evidence and policy decisions.
5. **Medium: migration/audit attestation is incomplete.** Version/name checks do not attest SQL checksums or every table/column. Local hash-chain audit logging is tamper-evident under its documented threat model, not a trusted external append-only record; suffix deletion/full rollback requires independently protected checkpoints. Same-artifact ciphertext rollback is not prevented by identity binding alone.
6. **Medium: exact filesystem race and parser isolation require deployment controls.** Symlink checks assume trusted parent-directory ownership; they are not an OS-level race-free `openat`/no-follow confinement mechanism. LibreOffice/native PDF parsers need low-privilege, resource-limited, network-isolated execution. Private temporary directories and normal cleanup do not guarantee secure erasure or crash cleanup.
7. **Medium: live-call accounting is not a hard budget.** Existing authorization gates, endpoint validation and cost-rate checks remain or are strengthened. Admission/reservation limits, provider configuration/RBAC, region/retention assurance, and organizational permission to transmit PHI remain separate. No live run validated those settings.
8. **Medium: test fixture provenance needs a dedicated pass.** Existing tests contain person-name/identifier patterns. No real-data fixture was copied into the new tests, but this audit does not certify every historical fixture as synthetic. The security report identifies a local-only provenance/secret-scan and release-allowlist follow-up.

These are explicit residual risks, not assertions of completed HIPAA or NIST compliance. I recommend merging reviewed, passing hardening fixes but **not claiming immutable package reproduction or production PHI readiness** until the high-priority gaps are addressed.

## Commit recommendation

Prefer logically separated commits after user review:

1. Package source/membership/native-rendering and medication/progression invariants with their regressions.
2. Semantic reuse hashing with graph/determinism regressions and version invalidation.
3. Persistence migration/concurrency/state-integrity changes with adversarial tests.
4. Security content-store/encryption/diagnostic/temp/live-boundary changes with security regressions and audit documentation.

Keep dependent interfaces/call sites/tests in the same commit. Shared integrated tests must pass for the final combined tree. No commit has been created.
