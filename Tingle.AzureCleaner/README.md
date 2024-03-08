# Azure Resources Cleaner

A convenience tool for cleaning up resources based on the terminal status of pull requests. This is particularly useful in removing the `reviewApp` resources in environments, that created automatically by Azure Pipelines. In addition, it will also clean up resources deployed to Azure.

> Review/preview applications and resources are generally created in PR-based workflows to allow team members review/preview changes before they are merged. They can also be useful in preventing bugs such as application startup errors from being merged. This pattern is generally considered a good practice and is used widely. You should consider making use of it if you aren't already.

For more read the [repo documentation](https://github.com/tinglesoftware/azure-resources-cleaner).
