# EMF Azure Policy Baseline

This directory defines the minimum Azure Policy assignments for an EMF production Key Vault resource group.

The baseline:

- requires the Azure RBAC permission model
- denies public network access
- requires deletion and purge protection
- requires expiration dates on Key Vault keys
- audits private-link configuration

Private-link compliance is audit-only because Microsoft deprecated the built-in policy's Deny effect.

## Validation

Run Bicep lint and build against `key-vault-policy.bicep`. CI performs both checks.

## Deployment

The template targets a resource group and is not deployed automatically by CI.
Run an Azure deployment what-if and obtain approval before production deployment.
Hosted EMF users do not deploy this template. Self-hosted non-Azure environments must implement equivalent controls.

## Compliance Monitoring

After an approved deployment, run:

```bash
./infra/policy/check-key-vault-policy-compliance.sh <resource-group>
```

The active Azure CLI identity must have read access to the target resource
group, policy assignments, and Azure Policy compliance state. Assign this
access to an approved monitoring identity at the smallest necessary scope.

The check is read-only and uses the active Azure CLI subscription. It verifies:

- all five expected policy assignments exist
- each assignment references the expected built-in policy
- enforcement mode and policy effects match the baseline
- Azure Policy reports no noncompliant resources
