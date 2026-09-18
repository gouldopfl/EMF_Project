# Migration 87 — completed through 87G

Migration 87 scopes medical-literature associations, accepted review decisions,
excerpts, reviewer selection, and supersession lineage to a service-connection
basis. Migration fan-out preserves linked legacy rows and rejects unsafe orphan
rows transactionally. Ambiguous writes require an explicit basis.

87G closes the final console regression: a reviewer command containing medical
literature now resolves and persists the sole basis, or requires --basis before
any AI invocation when selection is ambiguous. The console integration fixture
now includes the required basis/requirement and secondary-condition relationships.
Tests verify inferred single-basis selection, explicit selection, ambiguous
selection rejection without publishing or invoking AI, and the active literature
content in the generated reviewer document.

## Final validation

.NET SDK 10.0.100, Debug configuration:

- Veterans/literature/service-connection/package group: 758 passed, 0 failed.
- Complete EMF.Tests: 2,241 passed, 0 failed, 2 skipped; 2,243 total.
- Skips: opt-in live Azure OpenAI and Azure Monitor integration tests.
- Full EMF.sln build: succeeded, 0 warnings, 0 errors.
- git diff --check: clean.

Commands (live Azure integrations disabled):

```bash
export EMF_AZURE_OPENAI_LIVE_TESTS=false
export EMF_AZURE_MONITOR_LIVE_TESTS=false
dotnet test tests/EMF.Tests/EMF.Tests.csproj -m:1 --filter 'FullyQualifiedName~Veterans|FullyQualifiedName~MedicalLiterature|FullyQualifiedName~ServiceConnection|FullyQualifiedName~EvidencePackage'
dotnet test tests/EMF.Tests/EMF.Tests.csproj --no-build --no-restore -m:1
dotnet build EMF.sln -m:1
```

Validation used temporary test databases and fake intelligence providers. The
production database, source deployment on the VM, and GitHub remote were not
modified. Existing packages without a persisted basis cannot export medical
literature through the strict reviewer details path; regenerate/select a
basis-scoped package. Preserve the production database before first initializing
it with the updated application. Migration 87 refuses unsafe legacy orphan data
rather than discarding it.

## Preserved checkpoint history

- 07042a6 — schema migration
- d4d5f01 — 87B1 models/contracts
- 15da239 — 87B2 basis-scoped reads
- a22d798 — 87B3 reviewed decisions and lineage
- f86832e — 87B4 compatibility/isolation tests
- 3df2816 — 87C association/classification workflows
- 8ce62f2 — 87D operator basis selection
- de8e6f4 — 87E active reviewed literature selection
- 121e366 — 87F supersession/orphan hardening
- 87G — final console fix and regression validation (this commit)
