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

## Operational Boundaries

- Routing must authorize the capability and protection classification.
- Availability does not imply authorization.
- Azure failure does not permit fallback to Development.
- Prompts and responses are excluded from security-audit facts.
- Generated output requires review before Evidence promotion.
- Live tests never run automatically in the ordinary test suite.

## References

- [ADR-026](architecture/ADR-026-intelligence-services-agent-boundary.md)
- [ADR-027](architecture/ADR-027-initial-production-intelligence-provider.md)
- [Managed identity](https://learn.microsoft.com/azure/foundry-classic/openai/how-to/managed-identity)
- [Azure OpenAI RBAC](https://learn.microsoft.com/azure/foundry-classic/openai/how-to/role-based-access-control)
