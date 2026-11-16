@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('SQL Server administrator login.')
param sqlAdminLogin string = 'settleadmin'

@secure()
@description('SQL Server administrator password.')
param sqlAdminPassword string

@description('Public IP allowed through the SQL Server firewall for one-time schema setup.')
param clientIpAddress string

var suffix = uniqueString(resourceGroup().id)
var storageAccountName = 'strpasettle${suffix}'
var keyVaultName = 'kv-rpa-settle-${suffix}'
var sqlServerName = 'sql-rpa-settle-${suffix}'
var sqlDatabaseName = 'SettlementDb'
var functionAppName = 'func-rpa-settle-${suffix}'
var functionPlanName = 'plan-func-rpa-settle'
var portalAppName = 'app-rpa-settle-${suffix}'
var portalPlanName = 'plan-portal-rpa-settle'
var appInsightsName = 'appi-rpa-settle'
var settlementsContainerName = 'settlements-raw'
var settlementsQueueName = 'settlements-to-process'

resource storage 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageAccountName
  location: location
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-01-01' = {
  parent: storage
  name: 'default'
}

resource settlementsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  parent: blobService
  name: settlementsContainerName
}

resource queueService 'Microsoft.Storage/storageAccounts/queueServices@2023-01-01' = {
  parent: storage
  name: 'default'
}

resource settlementsQueue 'Microsoft.Storage/storageAccounts/queueServices/queues@2023-01-01' = {
  parent: queueService
  name: settlementsQueueName
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    Request_Source: 'rest'
  }
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  properties: {
    tenantId: subscription().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
  }
}

resource sqlServer 'Microsoft.Sql/servers@2023-05-01-preview' = {
  name: sqlServerName
  location: location
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    minimalTlsVersion: '1.2'
  }
}

resource sqlFirewallAzure 'Microsoft.Sql/servers/firewallRules@2023-05-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource sqlFirewallClient 'Microsoft.Sql/servers/firewallRules@2023-05-01-preview' = {
  parent: sqlServer
  name: 'AllowClientSetupIp'
  properties: {
    startIpAddress: clientIpAddress
    endIpAddress: clientIpAddress
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-05-01-preview' = {
  parent: sqlServer
  name: sqlDatabaseName
  location: location
  sku: {
    name: 'GP_S_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 1
  }
  properties: {
    autoPauseDelay: 60
    minCapacity: json('0.5')
    useFreeLimit: true
    freeLimitExhaustionBehavior: 'AutoPause'
  }
}

var sqlConnectionString = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${sqlDatabaseName};User ID=${sqlAdminLogin};Password=${sqlAdminPassword};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'

resource settlementDbSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'SettlementDb'
  properties: {
    value: sqlConnectionString
  }
}

resource functionPlan 'Microsoft.Web/serverfarms@2023-01-01' = {
  name: functionPlanName
  location: location
  sku: { name: 'Y1', tier: 'Dynamic' }
}

resource functionApp 'Microsoft.Web/sites@2023-01-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp'
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: functionPlan.id
    httpsOnly: true
    siteConfig: {
      netFrameworkVersion: 'v9.0'
      use32BitWorkerProcess: false
      appSettings: [
        { name: 'AzureWebJobsStorage', value: 'DefaultEndpointsProtocol=https;AccountName=${storage.name};AccountKey=${storage.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}' }
        { name: 'FUNCTIONS_EXTENSION_VERSION', value: '~4' }
        { name: 'FUNCTIONS_WORKER_RUNTIME', value: 'dotnet-isolated' }
        { name: 'WEBSITE_RUN_FROM_PACKAGE', value: '1' }
        { name: 'SettlementsContainerName', value: settlementsContainerName }
        { name: 'SettlementsQueueName', value: settlementsQueueName }
        { name: 'PortalBaseUrl', value: 'https://${portalAppName}.azurewebsites.net' }
        { name: 'SettlementDb', value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=SettlementDb)' }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
      ]
    }
  }
}

resource portalPlan 'Microsoft.Web/serverfarms@2023-01-01' = {
  name: portalPlanName
  location: location
  sku: { name: 'F1', tier: 'Free' }
}

resource portalApp 'Microsoft.Web/sites@2023-01-01' = {
  name: portalAppName
  location: location
  kind: 'app'
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: portalPlan.id
    httpsOnly: true
    siteConfig: {
      netFrameworkVersion: 'v9.0'
      alwaysOn: false
      appSettings: [
        { name: 'ConnectionStrings__SettlementDb', value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=SettlementDb)' }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
      ]
    }
  }
}

var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'

resource functionKvAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, functionApp.id, keyVaultSecretsUserRoleId)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource portalKvAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, portalApp.id, keyVaultSecretsUserRoleId)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalId: portalApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output functionAppName string = functionApp.name
output portalAppUrl string = 'https://${portalApp.name}.azurewebsites.net'
output functionAppUrl string = 'https://${functionApp.name}.azurewebsites.net'
output storageAccountName string = storage.name
output keyVaultName string = keyVault.name
