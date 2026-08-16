# Azure Key Vault Production Baseline

This directory defines the Azure production profile for EMF envelope
encryption. It is required only when an organization self-hosts EMF in Azure
and uses Azure Key Vault for protected production content.

Hosted users, individual VSOs using a hosted service, local development with
synthetic data, and non-Azure providers do not deploy this template.

## Templates

- `main.bicep` creates the protected vault boundary:
  - Azure RBAC authorization
  - purge protection and 90-day soft deletion
  - public network access disabled
  - deny-by-default network ACLs
  - private endpoint and private DNS integration
  - workload wrap/unwrap role assignment
  - centralized diagnostics
  - deletion lock
- `key.bicep` creates the envelope key separately:
  - RSA 3072 or 4096
  - wrap and unwrap operations only
  - non-exportable key
  - annual expiry and automated rotation
  - expiration notification

The separate key template preserves separation of duties. The workload
identity cannot create, rotate, delete, recover, or purge keys.

## Required Inputs

Deployment owners must supply approved values for:

- Key Vault name and Azure region
- workload managed-identity object ID
- private-endpoint subnet resource ID
- virtual-network resource ID
- Log Analytics workspace resource ID
- tags and, when necessary, the Azure-cloud private DNS zone

## Deployment Preconditions

Before deployment:

1. Obtain security and network approval.
2. Confirm private DNS and private-endpoint routing.
3. Confirm the workload identity and least-privilege scope.
4. Confirm lifecycle, recovery, monitoring, and emergency-access owners.
5. Confirm retention and historical-key requirements.
6. Use synthetic content for initial verification.
7. Run Bicep lint, build, validation, and what-if review.
8. Require approval before applying the reviewed deployment.

No command in this directory automatically deploys Azure resources.
