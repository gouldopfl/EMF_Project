# Independent hardening follow-up

Status: implemented and tested in `/tmp/emf-hardening-followup-bzorycjm`; **not applied to the shared source tree** while ownership of Astra's ongoing edits is unconfirmed. No commit was created. The patch is incremental over the existing uncommitted hardening changes at HEAD `52f16384`, not over clean HEAD.

## Confirmed findings and fixes

1. **Core/security identity integrity:** `IdentityValueValidator` accepted unpaired UTF-16 surrogates. Distinct artifact ID strings therefore encoded to identical UTF-8 bytes under replacement fallback, including the bytes used by `EncryptedArtifactContentStore` for authenticated identity context. Reject malformed Unicode in the shared validator. Valid supplementary characters, composed/decomposed text, and a literal replacement character remain unchanged and distinct. Existing malformed persisted IDs will now fail validation; no live data is migrated or repaired.
2. **Veterans progression integrity:** progression events without page coordinates skipped `VeteransReviewerPackageEvidenceScope.Contains`. Such events could be projected from supplied content despite a missing authoritative artifact or a membership row for another package. Extract shared member validation and invoke it independently of page coordinates. Existing legitimate unlocated observations remain supported when membership is valid and no page selection requires coordinates. No source range or historical state is fabricated.

The review also inspected the existing workflow diagnostic changes, content-store confinement/private creation, encrypted-envelope version enforcement, external alert fact allowlist, migration ledger/transaction checks, state revision guard, package details, medication scoping, clarification scoping, page selection, and semantic reuse input. This is a targeted independent follow-up, not a claim of exhaustive security verification.

## Validation

- Before fixes: new synthetic regressions **3 failed, 1 passed**. Both progression adversarial cases and malformed identity rejection failed as expected; valid Unicode preservation passed. Evidence: `followup-before-retry.log`.
- After fixes: **387 passed, 1 skipped, 0 failed** across identity, reviewer packages, encrypted/filesystem stores, envelope encryption, workflow and migration concurrency. The single skip was the optional LibreOffice continuation-identity test, matched by the identity filter with layout tests disabled. Evidence: `followup-focused.log`.
- The earlier full-suite result (2,596 passed, 2 Azure skips) belongs to the prior patch, not this follow-up. No new full-suite result is claimed.
- `git apply --check` and `git apply --check --whitespace=error` passed against the shared workspace; `git diff --check` passed; HEAD remained `52f16384`.
- Incremental patch: **5 files, +98 / -8**.

Offline restore used only `/home/michael/.nuget/packages`, with NuGet advisory querying disabled for this isolated restore. The existing advisory scan was not repeated. Azure OpenAI/Monitor live tests and layout export were disabled. No Key Vault or live-data operations were performed.

The first isolated build exhausted disk space while copying native dependencies and aborted before tests. Only this attempt's disposable isolated `bin` directories were removed. The successful retry used hard links for local build dependencies and left approximately 2.8 GB free. Test data, backups, generated `EMF_Output`, and shared source files were not changed by this follow-up.

Commands run from the isolated directory:

```bash
dotnet restore EMF.sln --source /home/michael/.nuget/packages -p:NuGetAudit=false

env -u EMF_REVIEWER_LAYOUT_ARTIFACTS EMF_AZURE_OPENAI_LIVE_TESTS=false EMF_AZURE_MONITOR_LIVE_TESTS=false EMF_REVIEWER_LAYOUT_TESTS=false dotnet test tests/EMF.Tests/EMF.Tests.csproj --no-restore -p:CreateHardLinksForCopyLocalIfPossible=true -p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true --filter 'FullyQualifiedName~Identity|FullyQualifiedName~VeteransReviewerPackage|FullyQualifiedName~EncryptedArtifactContentStore|FullyQualifiedName~FileSystemArtifactContentStore|FullyQualifiedName~EnvelopeEncryption|FullyQualifiedName~WorkflowRunner|FullyQualifiedName~SqliteMigrationConcurrencyHardening' --verbosity minimal
```

## Integration and residuals

Review `followup.patch` with Astra's final changes before applying it; do not reset existing work or apply the older full patch over the current tree. Resolve ownership before integration, then rerun checks appropriate to the combined final diff. Keep the original handoff and regression evidence intact.

Historical package snapshots and a complete rendered-output fingerprint remain absent. Legacy unbound ciphertext requires verified-identity migration. Plaintext metadata/exports/backups, existing permissions, filesystem races, parser isolation, audit anchoring, and hard spending caps still require the previously documented architectural or deployment work. These changes do not establish production PHI readiness or compliance certification.
