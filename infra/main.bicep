// LeaseVault infrastructure: App Service (with staging slot), Azure SQL, Storage (blob versioning),
// Key Vault, Application Insights and a system-assigned managed identity with Key Vault + Storage access.
//
//   az deployment group create -g rg-leasevault-prod -f infra/main.bicep \
//     -p namePrefix=leasevault environmentName=prod sqlAdminLogin=lvadmin sqlAdminPassword=<from Key Vault>

targetScope = 'resourceGroup'

@description('Short prefix used to build resource names, e.g. "leasevault".')
@minLength(3)
@maxLength(12)
param namePrefix string = 'leasevault'

@description('Environment tag / suffix (dev, test, prod).')
@allowed(['dev', 'test', 'prod'])
param environmentName string = 'prod'

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('App Service plan SKU. Slots require Standard or higher.')
@allowed(['S1', 'S2', 'P1v3', 'P2v3'])
param appServiceSku string = 'S1'

@description('Azure SQL database SKU name.')
param sqlSkuName string = 'S0'

@description('SQL administrator login (used for provisioning only; the app uses managed identity or Key Vault secret).')
param sqlAdminLogin string

@secure()
@description('SQL administrator password. Pass from a pipeline secret; never commit.')
param sqlAdminPassword string

@description('Object id of the operator/group granted Key Vault secret management rights.')
param keyVaultAdminObjectId string = ''

@description('Optional custom hostname (e.g. leasevault.contoso.com). Leave empty to skip the binding.')
param customHostname string = ''

var suffix = '${namePrefix}-${environmentName}'
var planName = 'asp-${suffix}'
var webAppName = 'app-${suffix}'
var sqlServerName = 'sql-${suffix}'
var sqlDbName = 'sqldb-${suffix}'
var storageName = 'st${uniqueString(resourceGroup().id)}${take(toLower(replace(namePrefix, '-', '')), 9)}' // 15-24 chars
var keyVaultName = 'kv-${suffix}'
// Built from the name (not keyVault.properties) because the vault's access policies depend on the web app identity.
var vaultUri = 'https://${keyVaultName}${az.environment().suffixes.keyvaultDns}/'
var appInsightsName = 'appi-${suffix}'
var logWorkspaceName = 'log-${suffix}'
var tags = {
  application: 'LeaseVault'
  environment: environmentName
}

// ---- Monitoring -------------------------------------------------------------------------------
resource logWorkspace 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: logWorkspaceName
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logWorkspace.id
  }
}

// ---- Compute ----------------------------------------------------------------------------------
resource plan 'Microsoft.Web/serverfarms@2023-01-01' = {
  name: planName
  location: location
  tags: tags
  sku: {
    name: appServiceSku
  }
  kind: 'linux'
  properties: {
    reserved: true
  }
}

resource webApp 'Microsoft.Web/sites@2023-01-01' = {
  name: webAppName
  location: location
  tags: tags
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|8.0'
      alwaysOn: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      healthCheckPath: '/health'
      appSettings: [
        { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
        { name: 'ApplicationInsightsAgent_EXTENSION_VERSION', value: '~3' }
        { name: 'KeyVault__Uri', value: vaultUri }
        { name: 'Storage__AccountUri', value: storage.properties.primaryEndpoints.blob }
        { name: 'Storage__ContainerName', value: 'documents' }
        // Secrets are referenced from Key Vault, never stored in app settings.
        { name: 'ConnectionStrings__Default', value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=ConnectionStrings--Default)' }
        { name: 'AzureAd__ClientSecret', value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=AzureAd--ClientSecret)' }
      ]
    }
  }
}

resource stagingSlot 'Microsoft.Web/sites/slots@2023-01-01' = {
  parent: webApp
  name: 'staging'
  location: location
  tags: tags
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|8.0'
      alwaysOn: true
      healthCheckPath: '/health'
      appSettings: [
        { name: 'ASPNETCORE_ENVIRONMENT', value: 'Staging' }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
        { name: 'KeyVault__Uri', value: vaultUri }
        { name: 'Storage__AccountUri', value: storage.properties.primaryEndpoints.blob }
        { name: 'Storage__ContainerName', value: 'documents' }
        { name: 'ConnectionStrings__Default', value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=ConnectionStrings--Default)' }
        { name: 'AzureAd__ClientSecret', value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=AzureAd--ClientSecret)' }
      ]
    }
  }
}

resource hostnameBinding 'Microsoft.Web/sites/hostNameBindings@2023-01-01' = if (!empty(customHostname)) {
  parent: webApp
  name: customHostname
  properties: {
    siteName: webApp.name
    hostNameType: 'Verified'
    sslState: 'Disabled'
  }
}

// ---- Data -------------------------------------------------------------------------------------
resource sqlServer 'Microsoft.Sql/servers@2022-05-01-preview' = {
  name: sqlServerName
  location: location
  tags: tags
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

resource sqlAllowAzure 'Microsoft.Sql/servers/firewallRules@2022-05-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource sqlDb 'Microsoft.Sql/servers/databases@2022-05-01-preview' = {
  parent: sqlServer
  name: sqlDbName
  location: location
  tags: tags
  sku: {
    name: sqlSkuName
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    zoneRedundant: false
  }
}

// ---- Document storage (blob versioning provides immutable version history) --------------------
resource storage 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageName
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    supportsHttpsTrafficOnly: true
    allowSharedKeyAccess: false // managed identity only
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-01-01' = {
  parent: storage
  name: 'default'
  properties: {
    isVersioningEnabled: true
    deleteRetentionPolicy: {
      enabled: true
      days: 30
    }
  }
}

resource documentsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  parent: blobService
  name: 'documents'
  properties: {
    publicAccess: 'None'
  }
}

// Storage Blob Data Contributor for the web app and its slot.
var blobDataContributorRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')

resource webAppBlobRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, webApp.id, blobDataContributorRoleId)
  scope: storage
  properties: {
    roleDefinitionId: blobDataContributorRoleId
    principalId: webApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource slotBlobRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, stagingSlot.id, blobDataContributorRoleId)
  scope: storage
  properties: {
    roleDefinitionId: blobDataContributorRoleId
    principalId: stagingSlot.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// ---- Key Vault --------------------------------------------------------------------------------
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  tags: tags
  properties: {
    tenantId: subscription().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: false
    enableSoftDelete: true
    enablePurgeProtection: true
    accessPolicies: concat([
      {
        tenantId: subscription().tenantId
        objectId: webApp.identity.principalId
        permissions: { secrets: ['get', 'list'] }
      }
      {
        tenantId: subscription().tenantId
        objectId: stagingSlot.identity.principalId
        permissions: { secrets: ['get', 'list'] }
      }
    ], empty(keyVaultAdminObjectId) ? [] : [
      {
        tenantId: subscription().tenantId
        objectId: keyVaultAdminObjectId
        permissions: { secrets: ['get', 'list', 'set', 'delete', 'recover'] }
      }
    ])
  }
}

resource sqlConnectionSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'ConnectionStrings--Default'
  properties: {
    value: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=${sqlDbName};Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False;'
  }
}

// ---- Outputs ----------------------------------------------------------------------------------
output webAppHostname string = webApp.properties.defaultHostName
output stagingSlotHostname string = stagingSlot.properties.defaultHostName
output webAppPrincipalId string = webApp.identity.principalId
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output storageAccountName string = storage.name
output keyVaultUri string = keyVault.properties.vaultUri
output appInsightsConnectionString string = appInsights.properties.ConnectionString
