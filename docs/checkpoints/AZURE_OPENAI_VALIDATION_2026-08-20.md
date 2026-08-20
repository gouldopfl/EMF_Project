# Azure OpenAI Validation — 2026-08-20

## Validation

UTC: 2026-08-20 12:54:25

EMF successfully completed a live Azure OpenAI integration test from
the Azure development VM using its system-assigned managed identity.

## Azure Deployment

- Resource: emf-intelligence-eastus2
- Resource group: EMF-Lab-R
- Region: eastus2
- Deployment: emf-gpt-4-1
- Model: gpt-4.1
- Model version: 2025-04-14
- Provisioning state: Succeeded

## Live Test

Test:

AzureOpenAIIntegrationTests.ExecuteAsync_ReturnsLiveNormalizedSummary

Result:

- Total: 1
- Passed: 1
- Failed: 0
- Skipped: 0
- Exit code: 0

The test exercised the production EMF.Intelligence.AzureOpenAI adapter
and authenticated through the VM system-assigned managed identity.

## Repository Baseline

07f6439 Run veterans evidence development from console

The repository was clean and synchronized with origin/main when the
Azure validation completed.
