using Azure.Identity;
using Tingle.AzureCleaner;

namespace Microsoft.Extensions.DependencyInjection;

internal enum EventBusTransportKind { InMemory, ServiceBus, QueueStorage, }

internal static class IServiceCollectionExtensions
{
    public static IServiceCollection AddCleaner(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache();
        services.Configure<AzureCleanerOptions>(configuration);
        services.AddSingleton<AzureCleaner>();

        return services;
    }

    public static IServiceCollection AddEventBus(this IServiceCollection services, IConfiguration configuration, Action<EventBusBuilder>? setupAction = null)
    {
        var selectedTransport = configuration.GetValue<EventBusTransportKind?>("EventBus:SelectedTransport");
        services.AddEventBus(builder =>
        {
            // Setup consumers
            builder.AddConsumer<ProcessAzdoCleanupEventConsumer>();

            // Setup transports
            var credential = new DefaultAzureCredential();
            if (selectedTransport is EventBusTransportKind.ServiceBus)
            {
                builder.AddAzureServiceBusTransport(
                    options => ((AzureServiceBusTransportCredentials)options.Credentials).TokenCredential = credential);
            }
            else if (selectedTransport is EventBusTransportKind.QueueStorage)
            {
                builder.AddAzureQueueStorageTransport(
                    options => ((AzureQueueStorageTransportCredentials)options.Credentials).TokenCredential = credential);
            }
            else if (selectedTransport is EventBusTransportKind.InMemory)
            {
                builder.AddInMemoryTransport();
            }

            setupAction?.Invoke(builder);
        });
        return services;
    }
}
