using Azure.ResourceManager.CosmosDB;
using Azure.ResourceManager.CosmosDB.Models;
using Azure.ResourceManager.Resources;

namespace Tingle.AzureCleaner.Purgers.AzureResources;

public class CosmosDBPurger(ILoggerFactory loggerFactory) : AbstractAzureResourcesPurger(loggerFactory)
{
    // TODO: split this a function for each kind!
    public override async Task PurgeAsync(PurgeContext<SubscriptionResource> context, CancellationToken cancellationToken = default)
    {
        var accounts = context.Resource.GetCosmosDBAccountsAsync(cancellationToken);
        await foreach (var account in accounts)
        {
            // delete matching accounts
            var name = account.Data.Name;
            if (context.NameMatches(name))
            {
                if (context.DryRun)
                {
                    Logger.LogInformation("Deleting CosmosDB account '{AccountName}' at '{ResourceId}' (dry run)", name, account.Data.Id);
                }
                else
                {
                    Logger.LogInformation("Deleting CosmosDB account '{AccountName}' at '{ResourceId}'", name, account.Data.Id);
                    await account.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken);
                }
                continue; // nothing more for the account
            }

            // delete matching MongoDB databases and collections
            if (account.Data.Kind == CosmosDBAccountKind.MongoDB)
            {
                // delete matching databases
                var databases = account.GetMongoDBDatabases().GetAllAsync(cancellationToken);
                await foreach (var database in databases)
                {
                    var databaseName = database.Data.Name;
                    if (context.NameMatches(databaseName))
                    {
                        if (context.DryRun)
                        {
                            Logger.LogInformation("Deleting CosmosDB for MongoDB database '{DatabaseName}' at '{ResourceId}' (dry run)", databaseName, database.Data.Id);
                        }
                        else
                        {
                            Logger.LogInformation("Deleting CosmosDB for MongoDB database '{DatabaseName}' at '{ResourceId}'", databaseName, database.Data.Id);
                            await database.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken: cancellationToken);
                        }
                        continue; // nothing more for the database
                    }

                    // delete matching collections
                    var collections = database.GetMongoDBCollections().GetAllAsync(cancellationToken);
                    await foreach (var collection in collections)
                    {
                        var collectionName = collection.Data.Name;
                        if (context.NameMatches(collectionName))
                        {
                            if (context.DryRun)
                            {
                                Logger.LogInformation("Deleting CosmosDB for MongoDB database collection '{CollectionName}' at '{ResourceId}' (dry run)", collectionName, database.Data.Id);
                            }
                            else
                            {
                                Logger.LogInformation("Deleting CosmosDB for MongoDB database collection '{CollectionName}' at '{ResourceId}'", collectionName, database.Data.Id);
                                await collection.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken: cancellationToken);
                            }
                        }
                    }
                }
                continue; // nothing more for the account
            }

            // delete matching Cassandra Keyspaces and tables
            if (account.Data.Capabilities.Any(c => c.Name == "EnableCassandra"))
            {
                var keyspaces = account.GetCassandraKeyspaces().GetAllAsync(cancellationToken);
                await foreach (var keyspace in keyspaces)
                {
                    // delete matching keyspaces
                    var keyspaceName = keyspace.Data.Name;
                    if (context.NameMatches(keyspaceName))
                    {
                        if (context.DryRun)
                        {
                            Logger.LogInformation("Deleting CosmosDB for Cassandra Keyspace '{KeyspaceName}' at '{ResourceId}' (dry run)", keyspaceName, keyspace.Data.Id);
                        }
                        else
                        {
                            Logger.LogInformation("Deleting CosmosDB for Cassandra Keyspace '{KeyspaceName}' at '{ResourceId}'", keyspaceName, keyspace.Data.Id);
                            await keyspace.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken: cancellationToken);
                        }
                        continue; // nothing more for the keyspace
                    }

                    // delete matching tables
                    var tables = keyspace.GetCassandraTables().GetAllAsync(cancellationToken);
                    await foreach (var table in tables)
                    {
                        var tableName = table.Data.Name;
                        if (context.NameMatches(tableName))
                        {
                            if (context.DryRun)
                            {
                                Logger.LogInformation("Deleting CosmosDB for Cassandra Keyspace Table '{TableName}' at '{ResourceId}' (dry run)", tableName, table.Data.Id);
                            }
                            else
                            {
                                Logger.LogInformation("Deleting CosmosDB for Cassandra Keyspace Table '{TableName}' at '{ResourceId}'", tableName, table.Data.Id);
                                await table.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken: cancellationToken);
                            }
                        }
                    }
                }
            }

            // delete matching Tables
            if (account.Data.Capabilities.Any(c => c.Name == "EnableTable"))
            {
                var tables = account.GetCosmosDBTables().GetAllAsync(cancellationToken);
                await foreach (var table in tables)
                {
                    var tableName = table.Data.Name;
                    if (context.NameMatches(tableName))
                    {
                        if (context.DryRun)
                        {
                            Logger.LogInformation("Deleting CosmosDB Table '{TableName}' at '{ResourceId}' (dry run)", tableName, table.Data.Id);
                        }
                        else
                        {
                            Logger.LogInformation("Deleting CosmosDB Table '{TableName}' at '{ResourceId}'", tableName, table.Data.Id);
                            await table.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken: cancellationToken);
                        }
                    }
                }
            }

            // delete matching Gremlin databases and graphs
            if (account.Data.Capabilities.Any(c => c.Name == "EnableGremlin"))
            {
                var databases = account.GetGremlinDatabases().GetAllAsync(cancellationToken);
                await foreach (var database in databases)
                {
                    // delete matching databases
                    var databaseName = database.Data.Name;
                    if (context.NameMatches(databaseName))
                    {
                        if (context.DryRun)
                        {
                            Logger.LogInformation("Deleting CosmosDB for Germlin database '{DatabaseName}' at '{ResourceId}' (dry run)", databaseName, database.Data.Id);
                        }
                        else
                        {
                            Logger.LogInformation("Deleting CosmosDB for Germlin database '{DatabaseName}' at '{ResourceId}'", databaseName, database.Data.Id);
                            await database.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken: cancellationToken);
                        }
                        continue; // nothing more for the database
                    }

                    // delete matching graphs
                    var graphs = database.GetGremlinGraphs().GetAllAsync(cancellationToken);
                    await foreach (var graph in graphs)
                    {
                        var graphName = graph.Data.Name;
                        if (context.NameMatches(graphName))
                        {
                            if (context.DryRun)
                            {
                                Logger.LogInformation("Deleting CosmosDB for Germlin database graph '{GraphName}' at '{ResourceId}' (dry run)", graphName, graph.Data.Id);
                            }
                            else
                            {
                                Logger.LogInformation("Deleting CosmosDB for Germlin database graph '{GraphName}' at '{ResourceId}'", graphName, graph.Data.Id);
                                await graph.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken: cancellationToken);
                            }
                        }
                    }
                }
            }

            // delete matching SQL databases and containers
            if (account.Data.Kind == CosmosDBAccountKind.GlobalDocumentDB)
            {
                if (account.Data.Capabilities.All(c => c.Name != "EnableCassandra" && c.Name != "EnableTable" && c.Name != "EnableGremlin"))
                {
                    var databases = account.GetCosmosDBSqlDatabases().GetAllAsync(cancellationToken);
                    await foreach (var database in databases)
                    {
                        // delete matching databases
                        var databaseName = database.Data.Name;
                        if (context.NameMatches(databaseName))
                        {
                            if (context.DryRun)
                            {
                                Logger.LogInformation("Deleting CosmosDB for SQL database '{DatabaseName}' at '{ResourceId}' (dry run)", databaseName, database.Data.Id);
                            }
                            else
                            {
                                Logger.LogInformation("Deleting CosmosDB for SQL database '{DatabaseName}' at '{ResourceId}'", databaseName, database.Data.Id);
                                await database.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken: cancellationToken);
                            }
                            continue; // nothing more for the database
                        }

                        // delete matching containers
                        var containers = database.GetCosmosDBSqlContainers().GetAllAsync(cancellationToken);
                        await foreach (var container in containers)
                        {
                            var containerName = container.Data.Name;
                            if (context.NameMatches(containerName))
                            {
                                if (context.DryRun)
                                {
                                    Logger.LogInformation("Deleting CosmosDB for SQL database container '{ContainerName}' at '{ResourceId}' (dry run)", containerName, container.Data.Id);
                                }
                                else
                                {
                                    Logger.LogInformation("Deleting CosmosDB for SQL database container '{ContainerName}' at '{ResourceId}'", containerName, container.Data.Id);
                                    await container.DeleteAsync(Azure.WaitUntil.Completed, cancellationToken: cancellationToken);
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
