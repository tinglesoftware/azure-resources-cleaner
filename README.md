# Azure Resources Cleaner

[![NuGet](https://img.shields.io/nuget/v/azure-resources-cleaner.svg)](https://www.nuget.org/packages/azure-resources-cleaner/)
[![GitHub Workflow Status](https://github.com/tinglesoftware/azure-resources-cleaner/actions/workflows/build.yml/badge.svg)](https://github.com/tinglesoftware/azure-resources-cleaner/actions/workflows/build.yml)
[![Release](https://img.shields.io/github/release/tinglesoftware/azure-resources-cleaner.svg?style=flat-square)](https://github.com/tinglesoftware/azure-resources-cleaner/releases/latest)
[![license](https://img.shields.io/github/license/tinglesoftware/azure-resources-cleaner.svg?style=flat-square)](LICENSE)

This repository houses a convenience tool for cleaning up resources based on the terminal status of pull requests. This is particularly useful in removing the `reviewApp` resources in environments, that created automatically by Azure Pipelines. In addition, it will also clean up resources deployed to Azure.

> Review/preview applications and resources are generally created in PR-based workflows to allow team members review/preview changes before they are merged. They can also be useful in preventing bugs such as application startup errors from being merged. This pattern is generally considered a good practice and is used widely. You should consider making use of it if you aren't already.

## Documentation

- [Setup](#setup)
  - [CLI tool](#cli-tool)
    - [Locally](#cli-tool-locally)
    - [GitHub Actions](#cli-tool-with-github-actions)
  - [Deployment to Azure](#deployment-to-azure)
  - [Azure DevOps Service Hooks and Subscriptions](#azure-devops-service-hooks-and-subscriptions)
- [What is supported](#what-is-supported)
  - [Naming format](#naming-format)
  - [Preview environments on Azure DevOps](#preview-environments-on-azure-devops)
  - [Preview deployments on Azure](#preview-deployments-on-azure)
- [Keeping updated](#keeping-updated)

## Setup

### CLI tool

The easiest way to run the tool is via CLI using .NET tools. This can help you when working locally or when working outside Azure Pipelines.

#### CLI tool locally

As a global tool:

```bash
dotnet tool install --global azure-resources-cleaner
azrc -h
azrc --pr <pull-request-number-here>
```

As a local tool:

```bash
dotnet new tool-manifest
dotnet tool install azure-resources-cleaner
dotnet azrc -h
dotnet azrc --pr <pull-request-number-here>
```

#### CLI tool with GitHub Actions

This tool can be used to clean up review resources using GitHub Actions. See the workflow below:

```yaml
name: Remove Review Resources

on:
  pull_request:
    types: [closed]
    branches: [main]
    paths-ignore:
    - README.md
  workflow_dispatch:
    inputs:
      pr:
        description: 'Pull request number'
        required: true
        type: number

env:
  AZURE_SUBSCRIPTION_ID: ${{ secrets.AZURE_SUBSCRIPTION_ID }}

jobs:
  remove:
    if: ${{ github.actor != 'dependabot[bot]' }}
    runs-on: ubuntu-latest
    name: 🗑️ Remove

    steps:
    - name: Setup .NET SDK
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: '9.x'

    - name: Azure Login
      uses: azure/login@v2
      with:
        creds: ${{ secrets.AZURE_CREDENTIALS }}

    - name: Remove review resources
      run: |
        dotnet tool install --global azure-resources-cleaner && \
        azrc \
        --pr ${{ inputs.pr || github.event.pull_request.number }} \
        --subscription ${{ env.AZURE_SUBSCRIPTION_ID }}
```

### Deployment to Azure

[![Deploy to Azure](https://aka.ms/deploytoazurebutton)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Ftinglesoftware%2Fazure-resources-cleaner%2Fmain%2Fmain.json)
[![Deploy to Azure US Gov](https://aka.ms/deploytoazuregovbutton)](https://portal.azure.us/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Ftinglesoftware%2Fazure-resources-cleaner%2Fmain%2Fmain.json)
[![Visualize](https://raw.githubusercontent.com/Azure/azure-quickstart-templates/master/1-CONTRIBUTION-GUIDE/images/visualizebutton.svg?sanitize=true)](http://armviz.io/#/?load=https%3A%2F%2Fraw.githubusercontent.com%2Ftinglesoftware%2Fazure-resources-cleaner%2Fmain%2Fmain.json)

The easiest means of deployment is to use the relevant button above. You can also use the [`main.json`](/main.json) or [`main.bicep`](/main.bicep) files. You will need an Azure subscription and a resource group to deploy to any of the Azure hosts.

|Parameter Name|Remarks|Required|Default|
|--|--|--|--|
|`notificationsPassword`|The password used to authenticate incoming requests from Azure DevOps|Yes|**none**|
|`azureDevOpsProjectUrl`|The URL of the Azure DevOps project or collection. For example `https://dev.azure.com/fabrikam/DefaultCollection`. This URL must be accessible from the network that the deployment is done in. You can modify the deployment to be done in a private network, but you are on your own there.|Yes|**none**|
|`azureDevOpsProjectToken`|Personal Access Token (PAT) for accessing the Azure DevOps project. It must have `Environment (Read & Manage)` permissions.|Yes|**none**|
|location|Location to deploy the resources.|No|&lt;resource-group-location&gt;|
|name|The name of all resources.|No|`azure-cleaner`|
|`dockerImageTag`|The image tag to use when pulling the docker container. A tag also defines the version. You should avoid using `latest`. Example: `0.1.0`|No|&lt;version-downloaded&gt;|

> The template includes a User Assigned Managed Identity, which is used when performing Azure Resource Manager operations such as deletions. After deployment, you should assign `Contributor` permissions to it where you want it to operate such as a subscription or a resource group. See [official docs](https://learn.microsoft.com/en-us/azure/role-based-access-control/role-assignments-portal-managed-identity#user-assigned-managed-identity) for how to assign permissions.<br/><br/> You can also do the role assignment on a management group. The tool scans for subscriptions that it has access to before listing the resources of a given type, so you need not change anything in the deployment after altering permissions.

### Azure DevOps Service Hooks and Subscriptions

To enable automatic cleanup after the status of a pull request changes, a subscription needs to be setup on Azure DevOps. Follow the [official documentation](https://learn.microsoft.com/en-us/azure/devops/service-hooks/services/webhooks?view=azure-devops) on how to set up one. The tool receives notifications via HTTP authenticated via basic authentication.

Steps to follow:

1. Create/Add subscription and select `Web Hooks` service type. Click Next.
2. Select `Pull request updated` for event type and `Status changed` for Change while leaving the rest as is. Click Next.
3. Populate the URL provided after deployment above, set the username to `vsts`, and the password to the value used in `notificationsPassword` above. Click Test to test functionality and if works, click Next.

Unfortunately, the Azure CLI does not offer support for creating the subscription. Otherwise, it'd have been much easier setup.

If you use the [REST API](https://learn.microsoft.com/en-us/rest/api/azure/devops/hooks/subscriptions/create?view=azure-devops-rest-7.0) here's a sample:

```json
{
  "publisherId": "tfs",
  "eventType": "git.pullrequest.updated",
  "resourceVersion": "1.0",
  "consumerId": "webHooks",
  "consumerActionId": "httpRequest",
  "publisherInputs": {
    "notificationType": "StatusUpdateNotification",
    "projectId": "<identifier-of-azure-project>"
  },
  "consumerInputs": {
    "detailedMessagesToSend": "none",
    "messagesToSend": "none",
    "url": "<notification-url-here>",
    "basicAuthUsername": "vsts",
    "basicAuthPassword": "<notifications-password-here>"
  }
}
```

> When using Azure Container Apps, the URL should have the format:<br/>`https://azure-cleaner.{envrionment-unique-dentifier}.{region}.azurecontainerapps.io/webhooks/azure`<br/>For example: `https://azure-cleaner.blackplant-123456a7.westeurope.azurecontainerapps.io/webhooks/azure`

## What is supported?

### Naming format

This tool looks for resources or sub-resources named in a number of formats:

- `review-app-{pull-request-identifier}`
- `ra-{pull-request-identifier}`
- `ra{pull-request-identifier}`

For example: `ra-2215`, `ra2215`, and `review-app-2215` will all be handled. Make sure you name your preview environments accordingly. If you wish to contribute more reasonable patterns, check [here](https://github.com/tinglesoftware/azure-resources-cleaner/blob/7e21f338f78f6af634d8aa35d39542455c55415b/Tingle.AzureCleaner/AzureCleaner.cs#L100)

### Preview environments on Azure DevOps

When a pipeline's deployment job uses the `reviewApp` keyword, a dynamic resource is created in the environment. If these are not removed, subsequent pipelines become slow because Azure loads the whole environment to find the relevant environment. An example pipeline would look like:

```yml
jobs:
- deployment:
  environment:
     name: smart-hotel-dev
     resourceName: ra-$(System.PullRequest.PullRequestId) # watch out on the naming format here
  pool:
    name: 'ubuntu-latest'
  strategy:
    runOnce:
      pre-deploy:
        steps:
        - reviewApp: MasterNamespace
```

Once the pull request is merged or abandoned, the `reviewApp` remains deployed. This tool cleans up after you.

### Preview deployments on Azure

Preview environments normally tend to deploy resources on Azure. This tool deletes these resources to ensure that you do not continue paying for them. You should remain within budget!
A couple of compute types are supported.
|Type|What is supported|
|--|--|
|Azure Resource Groups|Resource groups with names ending in the possible [formats](#naming-format). Useful in scenarios where everything is deployed in one group such a Virtual Machine with an IP Address, Disk, and Network Security Group.|
|Azure Kubernetes Service (AKS)|Namespaces with names ending in the possible [formats](#naming-format). Stopped clusters are ignored.|
|Azure Websites|Apps/websites, and slots with names ending in the possible [formats](#naming-format)|
|Azure Static WebApps|Apps and builds/environments with names ending in the possible [formats](#naming-format)|
|Azure Container Apps|Container Apps, Jobs, and environments with names ending in the possible [formats](#naming-format)|
|Azure Container Instances|Container Groups with names ending in the possible [formats](#naming-format)|
|Azure CosmosDB|Azure CosmosDB accounts, MongoDB databases/collection, Cassandra Keyspaces/tables, Gremlin databases/graph, and SQL databases/containers with names ending in the possible [formats](#naming-format)|
|Azure MySQL|Azure MySQL servers (Single and Flexible) and databases with names ending in the possible [formats](#naming-format)|
|Azure PostgreSQL|Azure PostgreSQL servers (Single and Flexible) and databases with names ending in the possible [formats](#naming-format)|
|Azure SQL|Azure SQL servers, elastic pools, and databases with names ending in the possible [formats](#naming-format)|
|Azure SQL Managed Instances|Azure SQL Managed Instances, instance pools, and databases with names ending in the possible [formats](#naming-format)|
|Azure User Assigned Managed Identities|Identities and federated credentials with names ending in the possible [formats](#naming-format)|

## Keeping updated

If you wish to keep your deployment updated, you can create a private repository with this one as a git submodule, configure Dependabot to update it then add a new workflow that deploys to your preferred host using a manual trigger (or one of your choice).

You can also choose to watch the repository to be notified when a new release is published.

### Issues &amp; Comments

Please leave all comments, bugs, requests, and issues on the Issues page. We'll respond to your request ASAP!
