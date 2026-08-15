# EMF Session Checkpoint

## Checkpoint Date

August 14, 2026 session, closed August 15 UTC.

## Repository State Before This Checkpoint

- Branch: `main`
- Remote: `origin/main`
- Last pushed commit: `f85b940 Document Azure OpenAI operations`
- Full suite: 437 passed, 0 failed, 1 intentionally skipped
- Target framework: .NET 10
- Projects: 19

## Completed This Session

- Added and pushed the opt-in Azure OpenAI live integration test.
- Added and pushed Azure OpenAI operational documentation.
- Added the root README.
- Installed Azure CLI 2.89.1 on the Ubuntu VM.
- Enabled the VM system-assigned managed identity.
- Verified managed-identity authentication.
- Verified Cognitive Services bearer-token acquisition.
- Preserved tenant Security Defaults after device-code login was
  correctly blocked.
- Confirmed that no Azure OpenAI resource currently exists.

## Azure State

- VM: `EMF-Dev-VM`
- Resource group: `EMF-Lab-R`
- Region: East US 2
- Virtual network: `vnet-eastus2-1`
- Subnet: `snet-eastus2-1`
- VM managed identity: enabled and authenticated
- Azure OpenAI resource: not created
- Model deployment: not created
- Azure OpenAI role assignment: not created
- Live Azure test: disabled
- Remaining Azure credit observed: $184.96
- Credit expiration observed: September 1, 2026

## Safe Resume Point

Resume in the Azure portal with Azure OpenAI resource creation.

Use:

- Name: `emf-intelligence-eastus2`
- Resource group: `EMF-Lab-R`
- Region: East US 2
- Tier: Standard S0
- Network access: Selected networks
- VNet: `vnet-eastus2-1`
- Subnet: `snet-eastus2-1`
- Firewall IP ranges: none
- Test content: benign synthetic text only

After creation, deploy a low-cost model, assign the VM identity the
`Cognitive Services OpenAI User` role, configure EMF environment
variables, and run the categorized live integration test.
