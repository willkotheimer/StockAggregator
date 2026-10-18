// Azure Communication Services Email for the midday digest.
//
// Kept apart from main.bicep on purpose: redeploying main.bicep rewrites the Function
// App's whole appSettings list, which would wipe settings set outside it (the live
// StockSymbols list among them). This file only adds the email resources and the role
// that lets the Function App's managed identity send. Deploy with:
//
//   az deployment group create -g stockaggreagator-rg -f infra/email.bicep \
//     -p functionAppPrincipalId=$(az functionapp identity show -n stockaggregator-func -g stockaggreagator-rg --query principalId -o tsv)
//
// then copy the two outputs into the Function App settings CommunicationServicesEndpoint
// and DigestSenderAddress.

@description('Name of the Email Communication Service.')
param emailServiceName string = 'stockaggregator-email'

@description('Name of the Communication Services resource the app sends through.')
param communicationServiceName string = 'stockaggregator-acs'

@description('Where message data is stored at rest.')
param dataLocation string = 'United States'

@description('Principal ID of the Function App\'s system-assigned managed identity.')
param functionAppPrincipalId string

// Built-in "Communication and Email Service Owner". It is broader than sending needs,
// but there is no send-only built-in role; scoping it to this one resource keeps it contained.
var emailSenderRoleId = '09976791-48a7-449e-bb21-39d1a415f350'

resource emailService 'Microsoft.Communication/emailServices@2023-04-01' = {
  name: emailServiceName
  location: 'global'
  properties: {
    dataLocation: dataLocation
  }
}

// The free Azure-managed sending domain (DoNotReply@<id>.azurecomm.net). Fine for
// personal use; a custom domain is the step to take before sending to subscribers.
resource managedDomain 'Microsoft.Communication/emailServices/domains@2023-04-01' = {
  parent: emailService
  name: 'AzureManagedDomain'
  location: 'global'
  properties: {
    domainManagement: 'AzureManaged'
    userEngagementTracking: 'Disabled'
  }
}

resource communicationService 'Microsoft.Communication/communicationServices@2023-04-01' = {
  name: communicationServiceName
  location: 'global'
  properties: {
    dataLocation: dataLocation
    linkedDomains: [
      managedDomain.id
    ]
  }
}

resource functionCanSend 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(communicationService.id, functionAppPrincipalId, emailSenderRoleId)
  scope: communicationService
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', emailSenderRoleId)
    principalId: functionAppPrincipalId
    principalType: 'ServicePrincipal'
  }
}

output communicationServicesEndpoint string = 'https://${communicationService.properties.hostName}'
output digestSenderAddress string = 'DoNotReply@${managedDomain.properties.mailFromSenderDomain}'
