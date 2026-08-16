targetScope = 'resourceGroup'

param keyVaultName string
param keyName string = 'emf-envelope-kek'

@allowed([
  3072
  4096
])
param keySize int = 3072

resource keyVault 'Microsoft.KeyVault/vaults@2024-11-01' existing = {
  name: keyVaultName
}

resource envelopeKey 'Microsoft.KeyVault/vaults/keys@2024-11-01' = {
  parent: keyVault
  name: keyName
  properties: {
    kty: 'RSA'
    keySize: keySize
    keyOps: [
      'wrapKey'
      'unwrapKey'
    ]
    attributes: {
      enabled: true
      exportable: false
    }
    rotationPolicy: {
      attributes: {
        expiryTime: 'P365D'
      }
      lifetimeActions: [
        {
          action: {
            type: 'rotate'
          }
          trigger: {
            timeBeforeExpiry: 'P30D'
          }
        }
        {
          action: {
            type: 'notify'
          }
          trigger: {
            timeBeforeExpiry: 'P60D'
          }
        }
      ]
    }
  }
}

output keyName string = envelopeKey.name
output keyResourceId string = envelopeKey.id
