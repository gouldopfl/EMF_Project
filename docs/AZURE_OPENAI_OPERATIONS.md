# Azure OpenAI Operations

## Purpose

EMF uses Azure OpenAI in Microsoft Foundry as its first production
semantic-intelligence provider.

The adapter preserves the provider-neutral EISL boundary defined by
ADR-026 and the production-provider controls defined by ADR-027.

## Security Requirements

Production access uses Microsoft Entra ID through
`DefaultAzureCredential`.

Do not configure or store Azure OpenAI API keys in EMF source,
configuration, command arguments, logs, audit records, or provider
metadata.

The workload identity should receive the least-privilege
`Cognitive Services OpenAI User` role scoped to the configured Azure
OpenAI resource.

A system-assigned managed identity is selected automatically. For a
user-assigned managed identity, configure its client ID explicitly.

## Required Configuration

| Environment variable | Required | Description |
| --- | --- | --- |
| `EMF_AZURE_OPENAI_ENDPOINT` | Yes | HTTPS endpoint for the Azure OpenAI resource |
| `EMF_AZURE_OPENAI_DEPLOYMENT` | Yes | Model deployment name |
| `EMF_AZURE_OPENAI_PROVIDER_ID` | No | Provider ID; defaults to `azure.openai` |
| `EMF_AZURE_OPENAI_MANAGED_IDENTITY_CLIENT_ID` | No | User-assigned managed-identity client ID |
| `EMF_AZURE_OPENAI_TIMEOUT_SECONDS` | No | Request timeout; defaults to 120 seconds |
| `EMF_AZURE_OPENAI_MAX_RETRIES` | No | Bounded retries; defaults to 2 |

The existing console identity, classification, and audit settings also
apply:

| Environment variable | Default |
| --- | --- |
| `EMF_SUBJECT_ID` | `console-steward` |
| `EMF_PROTECTION_CLASSIFICATION` | `confidential` |
| `EMF_SECURITY_AUDIT_DATABASE` | Application-local audit database |

Configuration contains endpoint and deployment identifiers but no
reusable authentication secret.

## Subscription and Quota Prerequisites

Azure OpenAI quota is governed by the Azure subscription, model,
deployment type, and region. It is not determined by whether the
account uses a Microsoft-hosted or third-party email address.

Azure Free Trial subscriptions are not eligible for quota increases.
Upgrade the existing subscription to Pay-As-You-Go before requesting
model quota. Upgrading preserves any remaining trial credit through
its original 30-day availability period, but usage can become billable
after that credit expires or is exhausted.

A subscription upgrade permits quota requests but does not guarantee
model availability or approval. Confirm that the requested model is
currently supported, request only the capacity required for the
controlled test, and configure Azure budgets and cost alerts before
enabling live execution.

Do not enable the live integration test until the deployment exists
and quota is available.

## Console Usage

Configure the endpoint and deployment:

```bash
export EMF_AZURE_OPENAI_ENDPOINT="https://RESOURCE.openai.azure.com"
export EMF_AZURE_OPENAI_DEPLOYMENT="DEPLOYMENT_NAME"
```

Run summarization:

```bash
dotnet run --project src/EMF.Console -- \
  intelligence summarize PATH_TO_TEXT_FILE
```

Missing configuration fails before provider invocation. Azure failure
does not trigger fallback to the Development provider.

## Live Integration Test

The Azure integration test is disabled by default. Normal test runs
report it as skipped and make no Azure request.

Run it only with an authorized identity and configured resource:

```bash
export EMF_AZURE_OPENAI_LIVE_TESTS=true
export EMF_AZURE_OPENAI_ENDPOINT="https://RESOURCE.openai.azure.com"
export EMF_AZURE_OPENAI_DEPLOYMENT="DEPLOYMENT_NAME"

dotnet test tests/EMF.Tests/EMF.Tests.csproj \
  --filter 'Category=AzureIntegration'
```

The test uses benign synthetic text. Do not use protected evidence to
test connectivity. Afterward, disable it:

```bash
unset EMF_AZURE_OPENAI_LIVE_TESTS
```

## Live Validation Record

On 2026-08-17, the Azure OpenAI production adapter was validated
against a real Azure deployment using the EMF development VM's
system-assigned managed identity.

Validated configuration:

- Azure region: East US 2
- Azure OpenAI resource: `emf-intelligence-eastus2`
- Endpoint: `https://emf-intelligence-eastus2.openai.azure.com/`
- Deployment: `emf-gpt-4-1`
- Model: `gpt-4.1`
- Model version: `2025-04-14`
- Deployment type: `Standard`
- Deployment capacity: 10
- Authentication: system-assigned managed identity
- Runtime role: `Cognitive Services OpenAI User`
- API keys: not used

The deployment reported `Running` and provisioning state `Succeeded`.

Regional capacity investigation showed no usable interactive
capacity for the tested GPT-5.6-sol and GPT-5.5 deployments in the
queried U.S. regions. GPT-5.4 exposed batch capacity but no interactive
Standard capacity. GPT-4.1 exposed regional Standard capacity in
East US 2 and multiple other U.S. regions.

Because suitable GPT-4.1 Standard capacity was available in East US 2,
the EMF development VM and existing Azure OpenAI resource did not need
to be migrated to another Azure region.

GPT-4.1 is being used as an authorized live integration-validation
model. This does not change the provider-independent intelligence
architecture or establish GPT-4.1 as the long-term production model.

The targeted live integration test completed successfully:

- Tests run: 1
- Passed: 1
- Failed: 0
- Skipped: 0

The complete EMF test suite was then executed with live Azure testing
enabled:

- Tests run: 515
- Passed: 515
- Failed: 0
- Skipped: 0

This validated real managed-identity authentication, Azure OpenAI
connectivity, deployment invocation, adapter translation, normalized
response handling, and coexistence with the complete EMF regression
suite.

## Operational Boundaries

- Routing must authorize the capability and protection classification.
- Availability does not imply authorization.
- Azure failure does not permit fallback to Development.
- Prompts and responses are excluded from security-audit facts.
- Audit metadata records the deployment name as the engine name and
  the provider-returned model version as the engine version.
- Generated output requires review before Evidence promotion.
- Live tests never run automatically in the ordinary test suite.

## References

- [ADR-026](architecture/ADR-026-intelligence-services-agent-boundary.md)
- [ADR-027](architecture/ADR-027-initial-production-intelligence-provider.md)
- [Managed identity](https://learn.microsoft.com/azure/foundry-classic/openai/how-to/managed-identity)
- [Azure OpenAI RBAC](https://learn.microsoft.com/azure/foundry-classic/openai/how-to/role-based-access-control)
- [Upgrade an Azure subscription](https://learn.microsoft.com/azure/cost-management-billing/manage/upgrade-azure-subscription)
- [Azure OpenAI quotas and limits](https://learn.microsoft.com/azure/foundry/openai/quotas-limits)
