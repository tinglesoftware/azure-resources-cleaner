@description('Location for all resources.')
param location string = resourceGroup().location

@description('Name of all resources.')
param name string = 'azure-cleaner'

@description('Tag of the docker image.')
param dockerImageTag string = '#{DOCKER_IMAGE_TAG}#'

@secure()
@description('Notifications password.')
param notificationsPassword string

@description('URL of the project. For example "https://dev.azure.com/fabrikam/DefaultCollection"')
param azureDevOpsProjectUrl string

@secure()
@description('Token for accessing the project.')
param azureDevOpsProjectToken string

@allowed([
  'ServiceBus'
  'QueueStorage'
])
@description('Merge strategy to use when setting auto complete on created pull requests.')
param eventBusTransport string = 'ServiceBus'

// Example: /subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/Fabrikam/providers/Microsoft.OperationalInsights/workspaces/fabrikam
@description('Resource identifier of the LogAnalytics Workspace to use. If none is provided, a new one is created.')
param logAnalyticsWorkspaceId string = ''

@description('Resource identifier of the ContainerApp Environment to deploy to. If none is provided, a new one is created.')
param appEnvironmentId string = ''

var hasProvidedLogAnalyticsWorkspace = (logAnalyticsWorkspaceId != null && !empty(logAnalyticsWorkspaceId))
var hasProvidedAppEnvironment = (appEnvironmentId != null && !empty(appEnvironmentId))
// avoid conflicts across multiple deployments for resources that generate FQDN based on the name
// original uniqueString(resourceGroup().id) produces a 13 character string but some resources have a 24 character limit
var collisionSuffix = substring(uniqueString(resourceGroup().id), 0, 24 - length(name) - 1) // e.g. zecnx476et (9 characters)

/* Managed Identity */
resource managedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: name
  location: location
}

/* Key Vault */
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: '${name}-${collisionSuffix}'
  location: location
  properties: {
    tenantId: subscription().tenantId
    sku: { name: 'standard', family: 'A' }
    enabledForDeployment: true
    enabledForDiskEncryption: true
    enabledForTemplateDeployment: true
    accessPolicies: []
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
  }
}

/* Service Bus namespace and Storage Account */
// One cannot provide their own to reduce complexity in this file
// Further, these are free tiers and should not be a cost concern
resource serviceBusNamespace 'Microsoft.ServiceBus/namespaces@2021-11-01' = if (eventBusTransport == 'ServiceBus') {
  name: '${name}-${collisionSuffix}'
  location: location
  properties: { disableLocalAuth: false, zoneRedundant: false }
  sku: { name: 'Basic' }

  resource authorizationRule 'AuthorizationRules' existing = { name: 'RootManageSharedAccessKey' }
}
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = if (eventBusTransport == 'QueueStorage') {
  name: '${name}${collisionSuffix}'
  location: location
  kind: 'StorageV2'
  sku: { name: 'Standard_LRS' }
  properties: {
    accessTier: 'Hot'
    supportsHttpsTrafficOnly: true
    networkAcls: { bypass: 'AzureServices', defaultAction: 'Allow' }
  }
}

/* LogAnalytics */
resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2022-10-01' = if (!hasProvidedLogAnalyticsWorkspace) {
  name: name
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
    workspaceCapping: { dailyQuotaGb: json('0.167') } // low so as not to pass the 5GB limit per subscription
  }
}
resource providedLogAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2022-10-01' existing = if (hasProvidedLogAnalyticsWorkspace) {
  // Inspired by https://github.com/Azure/bicep/issues/1722#issuecomment-952118402
  // Example: /subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/Fabrikam/providers/Microsoft.OperationalInsights/workspaces/fabrikam
  // 0 -> '', 1 -> 'subscriptions', 2 -> '00000000-0000-0000-0000-000000000000', 3 -> 'resourceGroups'
  // 4 -> 'Fabrikam', 5 -> 'providers', 6 -> 'Microsoft.OperationalInsights' 7 -> 'workspaces'
  // 8 -> 'fabrikam'
  name: split(logAnalyticsWorkspaceId, '/')[8]
  scope: resourceGroup(split(logAnalyticsWorkspaceId, '/')[2], split(logAnalyticsWorkspaceId, '/')[4])
}

/* Container App Environment */
resource appEnvironment 'Microsoft.App/managedEnvironments@2023-05-01' = if (!hasProvidedAppEnvironment) {
  name: name
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: hasProvidedLogAnalyticsWorkspace ? providedLogAnalyticsWorkspace.properties.customerId : logAnalyticsWorkspace.properties.customerId
        sharedKey: hasProvidedLogAnalyticsWorkspace ? providedLogAnalyticsWorkspace.listKeys().primarySharedKey : logAnalyticsWorkspace.listKeys().primarySharedKey
      }
    }
  }
}

/* Application Insights */
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: name
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: hasProvidedLogAnalyticsWorkspace ? providedLogAnalyticsWorkspace.id : logAnalyticsWorkspace.id
  }
}

/* KeyVault secrets */
resource projectAndToken0Secret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'project-and-token-0'
  properties: { contentType: 'text/plain', value: '${azureDevOpsProjectUrl};${azureDevOpsProjectToken}' }
}
resource notificationsPasswordSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'notifications-password'
  properties: { contentType: 'text/plain', value: notificationsPassword }
}

/* Container App */
resource app 'Microsoft.App/containerApps@2023-05-01' = {
  name: name
  location: location
  properties: {
    managedEnvironmentId: hasProvidedAppEnvironment ? appEnvironmentId : appEnvironment.id
    configuration: {
      ingress: { external: true, targetPort: 8080, traffic: [ { latestRevision: true, weight: 100 } ] }
      secrets: concat(
        [
          { name: 'connection-strings-application-insights', value: appInsights.properties.ConnectionString }
          { name: 'project-and-token-0', keyVaultUrl: projectAndToken0Secret.properties.secretUri, identity: managedIdentity.id }
          { name: 'notifications-password', keyVaultUrl: notificationsPasswordSecret.properties.secretUri, identity: managedIdentity.id }
        ],
        eventBusTransport == 'ServiceBus' ? [
          {
            name: 'connection-strings-asb-scaler'
            value: serviceBusNamespace::authorizationRule.listKeys().primaryConnectionString
          }
        ] : [],
        eventBusTransport == 'QueueStorage' ? [
          {
            name: 'connection-strings-storage-scaler'
            //'DefaultEndpointsProtocol=https;AccountName=<name>;EndpointSuffix=<suffix>;AccountKey=<key>'
            value: join([
                'DefaultEndpointsProtocol=https'
                'AccountName=${storageAccount.name}'
                'AccountKey=${storageAccount.listKeys().keys[0].value}'
                'EndpointSuffix=${environment().suffixes.storage}'
              ], ';')
          }
        ] : [])
    }
    template: {
      containers: [
        {
          image: 'ghcr.io/tinglesoftware/azure-resources-cleaner:${dockerImageTag}'
          name: 'azure-cleaner'
          env: [
            { name: 'AZURE_CLIENT_ID', value: managedIdentity.properties.clientId } // Specifies the User-Assigned Managed Identity to use. Without this, the app attempt to use the system assigned one.
            { name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED', value: 'true' }

            { name: 'GITHUB_SHA', value: '#{GITHUB_SHA}#' }
            { name: 'GITHUB_REF_NAME', value: '#{GITHUB_REF_NAME}#' }

            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', secretRef: 'connection-strings-application-insights' }
            { name: 'Authentication__ServiceHooks__Credentials__vsts', secretRef: 'notifications-password' }

            { name: 'Cleaner__AzdoProjects__0', secretRef: 'project-and-token-0' }

            { name: 'EventBus__SelectedTransport', value: eventBusTransport }
            {
              name: 'EventBus__Transports__azure-service-bus__FullyQualifiedNamespace'
              // manipulating https://{your-namespace}.servicebus.windows.net:443/
              value: eventBusTransport == 'ServiceBus' ? split(split(serviceBusNamespace.properties.serviceBusEndpoint, '/')[2], ':')[0] : ''
            }
            {
              name: 'EventBus__Transports__azure-queue-storage__ServiceUrl'
              value: eventBusTransport == 'QueueStorage' ? (storageAccount.properties.primaryEndpoints.queue) : ''
            }
          ]
          resources: { cpu: json('0.25'), memory: '0.5Gi' } // these are the least resources we can provision
          probes: [
            { type: 'Liveness', httpGet: { port: 8080, path: '/liveness' } }
            { type: 'Readiness', httpGet: { port: 8080, path: '/health' } }
          ]
        }
      ]
    }
  }
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${managedIdentity.id}': {/*ttk bug*/ }
    }
  }
}

/* Role Assignments */
resource keyVaultAdministratorRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(managedIdentity.id, 'KeyVaultAdministrator')
  scope: resourceGroup()
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '00482a5a-887f-4fb3-b363-3b7fe8e74483')
    principalId: managedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}
resource serviceBusDataOwnerRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (eventBusTransport == 'ServiceBus') {
  name: guid(managedIdentity.id, 'AzureServiceBusDataOwner')
  scope: resourceGroup()
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '090c5cfd-751d-490a-894a-3ce6f1109419')
    principalId: managedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}
resource storageQueueDataContributorRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (eventBusTransport == 'QueueStorage') {
  name: guid(managedIdentity.id, 'StorageQueueDataContributor')
  scope: resourceGroup()
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '974c5e8b-45b9-4653-ba55-5f855dd0fb88')
    principalId: managedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

output id string = app.id
output fqdn string = app.properties.configuration.ingress.fqdn
output notificationUrl string = 'https://${app.properties.configuration.ingress.fqdn}/webhooks/azure'
