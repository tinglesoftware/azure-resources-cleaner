using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tingle.AzureCleaner.Purgers;

namespace Tingle.AzureCleaner.Tests.Purgers;

public class DevOpsPurgerTests(ITestOutputHelper outputHelper)
{
    [Fact]
    public void TryFindAzdoProject_Works()
    {
        var projects = DevOpsPurgeContextOptions.MakeProjects([
            "https://dev.azure.com/fabrikam/DefaultCollection;123456789",
            "https://dev.azure.com/fabrikam/cea8cb01-dd13-4588-b27a-55fa170e4e94;987654321",
        ]);
        var purger = CreatePurger();

        // not found
        Assert.False(purger.TryFindAzdoProject(projects, "https://dev.azure.com/fabrikam/6ce954b1-ce1f-45d1-b94d-e6bf2464ba2c", out _, out _));

        // proper one
        Assert.True(purger.TryFindAzdoProject(projects, "https://dev.azure.com/fabrikam/DefaultCollection", out var url, out var token));
        Assert.Equal("https://dev.azure.com/fabrikam/DefaultCollection", url);
        Assert.Equal("123456789", token);

        // with project id
        Assert.True(purger.TryFindAzdoProject(projects, "https://dev.azure.com/fabrikam/_apis/projects/cea8cb01-dd13-4588-b27a-55fa170e4e94", out url, out token));
        Assert.Equal("https://dev.azure.com/fabrikam/cea8cb01-dd13-4588-b27a-55fa170e4e94", url);
        Assert.Equal("987654321", token);

        // with remote URL 1
        Assert.True(purger.TryFindAzdoProject(projects, "https://dev.azure.com/fabrikam/DefaultCollection/_git/Fabrikam", out url, out token));
        Assert.Equal("https://dev.azure.com/fabrikam/DefaultCollection", url);
        Assert.Equal("123456789", token);

        // with remote URL 2
        Assert.True(purger.TryFindAzdoProject(projects, "https://tingle@dev.azure.com/fabrikam/DefaultCollection/_git/Fabrikam", out url, out token));
        Assert.Equal("https://dev.azure.com/fabrikam/DefaultCollection", url);
        Assert.Equal("123456789", token);
    }

    private ModifiedDevOpsPurger CreatePurger()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Logging.AddXUnit(outputHelper);

        builder.Services.AddMemoryCache();
        builder.Services.AddScoped<DevOpsPurger, ModifiedDevOpsPurger>();

        var app = builder.Build();
        var scope = app.Services.CreateScope();
        var provider = scope.ServiceProvider;
        return Assert.IsType<ModifiedDevOpsPurger>(provider.GetRequiredService<DevOpsPurger>());
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
