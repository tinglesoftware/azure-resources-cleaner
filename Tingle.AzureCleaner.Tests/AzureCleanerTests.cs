using Azure.Core;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tingle.AzureCleaner.Purgers;

namespace Tingle.AzureCleaner.Tests;

public class AzureCleanerTests(ITestOutputHelper outputHelper)
{
    [Fact]
    public async Task HandleAsync_Works()
    {
        var (cleaner, arp, dop) = CreateCleaner(["https://dev.azure.com/fabrikam/DefaultCollection;123456789"]);

        await cleaner.HandleAsync(ids: [1],
                                  url: "https://dev.azure.com/fabrikam/DefaultCollection/_git/Fabrikam",
                                  cancellationToken: TestContext.Current.CancellationToken);

        var ctx1 = Assert.Single(arp.PurgeAsyncCalls);
        var ctx2 = Assert.Single(dop.PurgeAsyncCalls);

        Assert.Equal(["review-app-1", "ra-1", "ra1"], ctx1.PossibleNames);
        Assert.Equal(ctx1.PossibleNames, ctx2.PossibleNames);
        Assert.False(ctx1.DryRun);
        Assert.Equal(ctx1.DryRun, ctx2.DryRun);
    }

    private (AzureCleaner, ModifiedAzureResourcesPurger, ModifiedDevOpsPurger) CreateCleaner(IList<string> projects)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Configuration.AddInMemoryCollection(
            projects.Select((p, i) => new KeyValuePair<string, string?>($"Cleaner:AzdoProjects:{i}", p)));

        builder.Logging.AddXUnit(outputHelper);

        builder.Services.AddCleaner(builder.Configuration.GetSection("Cleaner"));
        builder.Services.AddScoped<AzureResourcesPurger, ModifiedAzureResourcesPurger>();
        builder.Services.AddScoped<DevOpsPurger, ModifiedDevOpsPurger>();

        var app = builder.Build();
        var scope = app.Services.CreateScope();
        var provider = scope.ServiceProvider;
        var cleaner = provider.GetRequiredService<AzureCleaner>();
        var arp = Assert.IsType<ModifiedAzureResourcesPurger>(provider.GetRequiredService<AzureResourcesPurger>());
        var dop = Assert.IsType<ModifiedDevOpsPurger>(provider.GetRequiredService<DevOpsPurger>());
        return (cleaner, arp, dop);
    }

    class ModifiedAzureResourcesPurger(ILoggerFactory loggerFactory) : AzureResourcesPurger(loggerFactory)
    {
        public List<PurgeContext<AzureResourcesPurgeContextOptions>> PurgeAsyncCalls { get; } = [];
        public override Task PurgeAsync(PurgeContext<AzureResourcesPurgeContextOptions> context, CancellationToken cancellationToken = default)
        {
            PurgeAsyncCalls.Add(context);
            return Task.CompletedTask;
        }
    }
    class ModifiedDevOpsPurger(IMemoryCache cache, ILogger<DevOpsPurger> logger) : DevOpsPurger(cache, logger)
    {
        public List<PurgeContext<DevOpsPurgeContextOptions>> PurgeAsyncCalls { get; } = [];
        public override Task PurgeAsync(PurgeContext<DevOpsPurgeContextOptions> context, CancellationToken cancellationToken = default)
        {
            PurgeAsyncCalls.Add(context);
            return Task.CompletedTask;
        }
    }
}
