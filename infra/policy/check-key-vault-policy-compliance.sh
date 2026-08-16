#!/usr/bin/env bash

set -euo pipefail

if [[ $# -ne 1 ]]; then
    echo "Usage: $0 <resource-group>" >&2
    exit 2
fi

readonly resource_group="$1"

if ! command -v az >/dev/null 2>&1; then
    echo "ERROR: Azure CLI is required." >&2
    exit 2
fi

if ! subscription_id="$(az account show --query id --output tsv)"; then
    echo "ERROR: Sign in to Azure CLI before running this check." >&2
    exit 2
fi

if ! group_error="$(
    az group show \
        --name "$resource_group" \
        --subscription "$subscription_id" \
        --output none 2>&1
)"; then
    echo "ERROR: Unable to read resource group '$resource_group'." >&2
    echo "$group_error" >&2
    exit 2
fi

readonly scope="/subscriptions/${subscription_id}/resourceGroups/${resource_group}"
failures=0

readonly assignments=(
    "emf-kv-rbac|12d4fa5e-1f9f-4c21-97a9-b99b3c6611b5|effect|Deny"
    "emf-kv-no-public|405c5871-3e91-4644-8a63-58e19d68ff5b|effect|Deny"
    "emf-kv-delete-protect|0b60c0b2-2dc2-4e1c-b5c9-abbed971de53|effect|Deny"
    "emf-kv-key-expiry|152b15f7-8e1f-4c1f-ab71-8c010ba5dbc0|effect|Deny"
    "emf-kv-private-link|a6abeaec-4d90-4a02-805f-6b26c4d3fbe9|audit_effect|Audit"
)

echo "Checking EMF Key Vault policy baseline"
echo "Scope: $scope"

for expected in "${assignments[@]}"; do
    IFS='|' read -r \
        assignment_name \
        definition_id \
        parameter_name \
        parameter_value \
        <<< "$expected"

    if ! az policy assignment show \
        --name "$assignment_name" \
        --scope "$scope" \
        --subscription "$subscription_id" \
        >/dev/null 2>&1; then
        echo "FAIL: Missing assignment '$assignment_name'."
        failures=$((failures + 1))
        continue
    fi

    query="[policyDefinitionId,enforcementMode,parameters.${parameter_name}.value]"

    values="$(
        az policy assignment show \
            --name "$assignment_name" \
            --scope "$scope" \
            --subscription "$subscription_id" \
            --query "$query" \
            --output tsv
    )"

    IFS=$'\t' read -r \
        actual_definition \
        actual_mode \
        actual_value \
        <<< "$values"

    expected_definition="/providers/Microsoft.Authorization/policyDefinitions/${definition_id}"

    if [[ "$actual_definition" != "$expected_definition" ]]; then
        echo "FAIL: '$assignment_name' uses an unexpected policy definition."
        failures=$((failures + 1))
        continue
    fi

    if [[ "$actual_mode" != "Default" ]]; then
        echo "FAIL: '$assignment_name' is not enforced."
        failures=$((failures + 1))
        continue
    fi

    if [[ "$actual_value" != "$parameter_value" ]]; then
        echo "FAIL: '$assignment_name' has an unexpected policy effect."
        failures=$((failures + 1))
        continue
    fi

    noncompliant="$(
        az policy state list \
            --resource-group "$resource_group" \
            --subscription "$subscription_id" \
            --filter "PolicyAssignmentName eq '${assignment_name}' and ComplianceState eq 'NonCompliant'" \
            --query 'length(@)' \
            --output tsv
    )"

    if [[ "$noncompliant" != "0" ]]; then
        echo "FAIL: '$assignment_name' has $noncompliant noncompliant resource(s)."
        failures=$((failures + 1))
        continue
    fi

    echo "PASS: $assignment_name"
done

if (( failures > 0 )); then
    echo "EMF policy compliance check failed with $failures issue(s)." >&2
    exit 1
fi

echo "EMF policy baseline is assigned and compliant."
