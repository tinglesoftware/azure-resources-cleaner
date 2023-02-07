@description('Location for all resources.')
param location string = resourceGroup().location

@description('Name of all resources.')
param name string = 'azdo-cleaner'

@description('Registry of the docker image. E.g. "contoso.azurecr.io". Leave empty unless you have a private registry mirroring the image from docker hub')
param dockerImageRegistry string = ''

@description('Registry and repository of the docker image. Ideally, you do not need to edit this value.')
param dockerImageRepository string = 'tingle/azure-devops-cleaner'

@description('Tag of the docker image.')
param dockerImageTag string = '#{GITVERSION_NUGETVERSIONV2}#'

@secure()
@description('Notifications password.')
param notificationsPassword string

@description('URL of the project. For example "https://dev.azure.com/fabrikam/DefaultCollection"')
param azureDevOpsProjectUrl string

@secure()
@description('Token for accessing the project.')
param azureDevOpsProjectToken string

@minValue(0)
@maxValue(2)
@description('The minimum number of replicas')
param minReplicas int = 0

@minValue(1)
@maxValue(5)
@description('The maximum number of replicas')
param maxReplicas int = 1

var hasDockerImageRegistry = (dockerImageRegistry != null && !empty(dockerImageRegistry))

/* Managed Identity */
resource managedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2018-11-30' = {
  name: name
  location: location
}

/* Container App Environment */
resource appEnvironment 'Microsoft.App/managedEnvironments@2022-03-01' = {
  name: name
  location: location
  properties: {}
}

/* Application Insights */
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: name
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
  }
}

/* Container App */
resource app 'Microsoft.App/containerApps@2022-03-01' = {
  name: name
  location: location
  properties: {
    managedEnvironmentId: appEnvironment.id
    configuration: {
      ingress: {
        external: true
        targetPort: 80
        traffic: [
          {
            latestRevision: true
            weight: 100
          }
        ]
      }
      registries: hasDockerImageRegistry ? [
        {
          identity: managedIdentity.id
          server: dockerImageRegistry
        }
      ] : []
      secrets: [
        { name: 'connection-strings-application-insights', value: appInsights.properties.ConnectionString }
        { name: 'notifications-password', value: notificationsPassword }
        { name: 'project-and-token-0', value: '${azureDevOpsProjectUrl};${azureDevOpsProjectToken}' }
      ]
    }
    template: {
      containers: [
        {
          image: '${'${hasDockerImageRegistry ? '${dockerImageRegistry}/' : ''}'}${dockerImageRepository}:${dockerImageTag}'
          name: 'azdo-cleaner'
          env: [
            { name: 'AZURE_CLIENT_ID', value: managedIdentity.properties.clientId } // Specifies the User-Assigned Managed Identity to use. Without this, the app attempt to use the system assigned one.
            { name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED', value: 'true' }
            { name: 'ApplicationInsights__ConnectionString', secretRef: 'connection-strings-application-insights' }
            { name: 'Authentication__ServiceHooks__Credentials__vsts', secretRef: 'notifications-password' }
            { name: 'Handler__Projects__0', secretRef: 'project-and-token-0' }
            { name: 'Handler__AzureWebsites', value: 'false' }
          ]
          resources: { // these are the least resources we can provision
            cpu: json('0.25')
            memory: '0.5Gi'
          }
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
      }
    }
  }
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${managedIdentity.id}': { /*ttk bug*/}
    }
  }
}

output id string = app.id
output fqdn string = app.properties.configuration.ingress.fqdn
output notificationUrl string = 'https://${app.properties.configuration.ingress.fqdn}/webhooks/azure'
