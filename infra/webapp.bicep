@description('Azure region for the resources.')
param location string = resourceGroup().location

@description('Name of the App Service (globally unique; becomes <name>.azurewebsites.net).')
param webAppName string

@description('Name of the App Service plan.')
param appServicePlanName string

@description('Name of the existing SQL logical server (without the .database.windows.net suffix).')
param sqlServerName string

@description('Name of the SQL database.')
param sqlDatabaseName string = 'stockaggregator'

@description('Dotnet runtime version for the web app.')
param dotnetRuntimeVersion string = '10.0'

// Entra connection string — no secret. The app attaches an access token via its
// managed identity (see StockAggregatorApp/Data/SqlConnectionFactory.cs).
var sqlConnectionString = 'Server=tcp:${sqlServerName}.database.windows.net,1433;Initial Catalog=${sqlDatabaseName};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: appServicePlanName
  location: location
  sku: {
    name: 'F1'
    tier: 'Free'
  }
  kind: 'linux'
  properties: {
    reserved: true // Linux
  }
}

resource webApp 'Microsoft.Web/sites@2023-12-01' = {
  name: webAppName
  location: location
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|${dotnetRuntimeVersion}'
      alwaysOn: false // not supported on the Free (F1) tier
      ftpsState: 'FtpsOnly'
      minTlsVersion: '1.2'
      appSettings: [
        {
          name: 'SqlConnectionString'
          value: sqlConnectionString
        }
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
      ]
    }
  }
}

output webAppName string = webApp.name
output webAppUrl string = 'https://${webApp.properties.defaultHostName}'
output webAppPrincipalId string = webApp.identity.principalId
