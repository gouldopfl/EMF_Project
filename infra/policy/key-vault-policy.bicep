targetScope = 'resourceGroup'

metadata description = 'EMF production Key Vault policy baseline.'

var policyRoot = '/providers/Microsoft.Authorization/policyDefinitions'

resource requireRbac 'Microsoft.Authorization/policyAssignments@2024-04-01' = {
  name: 'emf-kv-rbac'
  properties: {
    displayName: 'EMF - Require Key Vault RBAC'
    policyDefinitionId: '${policyRoot}/12d4fa5e-1f9f-4c21-97a9-b99b3c6611b5'
    enforcementMode: 'Default'
    parameters: {
      effect: {
        value: 'Deny'
      }
    }
  }
}

resource disablePublicAccess 'Microsoft.Authorization/policyAssignments@2024-04-01' = {
  name: 'emf-kv-no-public'
  properties: {
    displayName: 'EMF - Disable Key Vault public access'
    policyDefinitionId: '${policyRoot}/405c5871-3e91-4644-8a63-58e19d68ff5b'
    enforcementMode: 'Default'
    parameters: {
      effect: {
        value: 'Deny'
      }
    }
  }
}

resource requireDeletionProtection 'Microsoft.Authorization/policyAssignments@2024-04-01' = {
  name: 'emf-kv-delete-protect'
  properties: {
    displayName: 'EMF - Require Key Vault deletion protection'
    policyDefinitionId: '${policyRoot}/0b60c0b2-2dc2-4e1c-b5c9-abbed971de53'
    enforcementMode: 'Default'
    parameters: {
      effect: {
        value: 'Deny'
      }
    }
  }
}

resource requireKeyExpiration 'Microsoft.Authorization/policyAssignments@2024-04-01' = {
  name: 'emf-kv-key-expiry'
  properties: {
    displayName: 'EMF - Require Key Vault key expiration'
    policyDefinitionId: '${policyRoot}/152b15f7-8e1f-4c1f-ab71-8c010ba5dbc0'
    enforcementMode: 'Default'
    parameters: {
      effect: {
        value: 'Deny'
      }
    }
  }
}

resource auditPrivateLink 'Microsoft.Authorization/policyAssignments@2024-04-01' = {
  name: 'emf-kv-private-link'
  properties: {
    displayName: 'EMF - Audit Key Vault private link'
    policyDefinitionId: '${policyRoot}/a6abeaec-4d90-4a02-805f-6b26c4d3fbe9'
    enforcementMode: 'Default'
    parameters: {
      audit_effect: {
        value: 'Audit'
      }
    }
  }
}
