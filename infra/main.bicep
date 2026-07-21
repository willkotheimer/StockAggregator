@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Name of the Azure Function App.')
param functionAppName string

@description('Name of the storage account used by the Functions runtime.')
param storageAccountName string

@description('Name of the App Service plan.')
param appServicePlanName string

@description('Name of the SQL logical server.')
param sqlServerName string

@description('Name of the SQL database.')
param sqlDatabaseName string = 'StockAggregator'

@description('SQL administrator username.')
param sqlAdminLogin string

@secure()
@description('SQL administrator password.')
param sqlAdminPassword string

@description('Microsoft Entra admin login for the SQL server (a user UPN or a group name).')
param sqlAadAdminLogin string

@description('Object ID (SID) of the Entra admin user or group.')
param sqlAadAdminObjectId string

@description('Entra admin principal type: User, Group, or Application.')
@allowed([
  'User'
  'Group'
  'Application'
])
param sqlAadAdminPrincipalType string = 'User'

@description('Stock symbols to fetch, as a comma-separated list.')
param stockSymbols string = 'AAPL,MSFT,NVDA'

@description('Yahoo Finance chart endpoint (single-symbol; the app appends /{symbol}).')
param yahooChartBaseUrl string = 'https://query1.finance.yahoo.com/v8/finance/chart'

@description('Time zone used for timer triggers.')
param websiteTimeZone string = 'Central Standard Time'

@description('Dotnet runtime version for the Functions app.')
param dotnetRuntimeVersion string = '10.0'

var storageApiVersion = '2023-05-01'
var storageConnectionString = 'DefaultEndpointsProtocol=https;AccountName=${storage.name};AccountKey=${listKeys(storage.id, storageApiVersion).keys[0].value};EndpointSuffix=${environment().suffixes.storage}'
// Entra (Microsoft Entra ID) authentication — no secret in the string. The app
// fills in the auth mode at runtime (managed identity in Azure), so no
// Authentication keyword is needed here.
var sqlConnectionString = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=${sqlDatabase.name};Persist Security Info=False;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: appServicePlanName
  location: location
  sku: {
    name: 'Y1'
    tier: 'Dynamic'
  }
  kind: 'functionapp'
  properties: {}
}

resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNET-ISOLATED|${dotnetRuntimeVersion}'
      appSettings: [
        {
          name: 'AzureWebJobsStorage'
          value: storageConnectionString
        }
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'dotnet-isolated'
        }
        {
          name: 'SqlConnectionString'
          value: sqlConnectionString
        }
        {
          name: 'StockSymbols'
          value: stockSymbols
        }
        {
          name: 'YahooChartBaseUrl'
          value: yahooChartBaseUrl
        }
        {
          name: 'WEBSITE_TIME_ZONE'
          value: websiteTimeZone
        }
      ]
      ftpsState: 'FtpsOnly'
      minTlsVersion: '1.2'
    }
  }
}

resource sqlServer 'Microsoft.Sql/servers@2022-05-01-preview' = {
  name: sqlServerName
  location: location
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    version: '12.0'
    publicNetworkAccess: 'Enabled'
    minimalTlsVersion: '1.2'
    // Entra admin. SQL auth stays enabled (azureADOnlyAuthentication: false) so
    // the SQL admin remains a break-glass login. Set to true to require Entra.
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: sqlAadAdminPrincipalType
      login: sqlAadAdminLogin
      sid: sqlAadAdminObjectId
      tenantId: subscription().tenantId
      azureADOnlyAuthentication: false
    }
  }
}

resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2022-05-01-preview' = {
  name: 'AllowAzureServices'
  parent: sqlServer
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2022-05-01-preview' = {
  name: '${sqlServer.name}/${sqlDatabaseName}'
  location: location
  sku: {
    name: 'Basic'
    tier: 'Basic'
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    maxSizeBytes: 2147483648
  }
}

output functionAppName string = functionApp.name
output functionAppUrl string = 'https://${functionApp.properties.defaultHostName}'
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output sqlDatabaseName string = sqlDatabase.name
output functionAppPrincipalId string = functionApp.identity.principalId
