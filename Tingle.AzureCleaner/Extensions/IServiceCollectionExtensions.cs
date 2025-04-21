using Azure.Identity;
using Tingle.AzureCleaner;
using Tingle.AzureCleaner.Purgers;

namespace Microsoft.Extensions.DependencyInjection;

internal enum EventBusTransportKind { InMemory, ServiceBus, QueueStorage, }

internal static class IServiceCollectionExtensions
{
    public static IServiceCollection AddCleaner(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache();
        services.Configure<AzureCleanerOptions>(configuration);
        services.AddScoped<AzureCleaner>();
        services.AddScoped<AzureResourcesPurger>();
        services.AddScoped<DevOpsPurger>();

        return services;
    }

    public static IServiceCollection AddEventBus(this IServiceCollection services, IConfiguration configuration, Action<EventBusBuilder>? setupAction = null)
    {
        var selectedTransport = configuration.GetValue<EventBusTransportKind?>("EventBus:SelectedTransport");
        services.AddSlimEventBus(builder =>
        {
            // Setup consumers
            builder.AddConsumer<AzdoCleanupEvent, ProcessAzdoCleanupEventConsumer>();

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
