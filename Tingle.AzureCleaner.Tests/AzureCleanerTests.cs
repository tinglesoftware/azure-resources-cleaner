using Azure.Core;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Tingle.AzureCleaner.Tests;

public class AzureCleanerTests
{
    [Fact]
    public void TryFindAzdoProject_Works()
    {
        var cache = new MemoryCache(Options.Create(new MemoryCacheOptions()));
        var optionsAccessor = Options.Create(new AzureCleanerOptions
        {
            AzdoProjects =
            [
                "https://dev.azure.com/fabrikam/DefaultCollection;123456789",
                "https://dev.azure.com/fabrikam/cea8cb01-dd13-4588-b27a-55fa170e4e94;987654321",
            ],
        });
        var logger = new LoggerFactory().CreateLogger<AzureCleaner>();
        var cleaner = new AzureCleaner(cache, optionsAccessor, logger);

        // not found
        Assert.False(cleaner.TryFindAzdoProject("https://dev.azure.com/fabrikam/6ce954b1-ce1f-45d1-b94d-e6bf2464ba2c", out _, out _));

        // proper one
        Assert.True(cleaner.TryFindAzdoProject("https://dev.azure.com/fabrikam/DefaultCollection", out var url, out var token));
        Assert.Equal("https://dev.azure.com/fabrikam/DefaultCollection", url);
        Assert.Equal("123456789", token);

        // with project id
        Assert.True(cleaner.TryFindAzdoProject("https://dev.azure.com/fabrikam/_apis/projects/cea8cb01-dd13-4588-b27a-55fa170e4e94", out url, out token));
        Assert.Equal("https://dev.azure.com/fabrikam/cea8cb01-dd13-4588-b27a-55fa170e4e94", url);
        Assert.Equal("987654321", token);

        // with remote URL 1
        Assert.True(cleaner.TryFindAzdoProject("https://dev.azure.com/fabrikam/DefaultCollection/_git/Fabrikam", out url, out token));
        Assert.Equal("https://dev.azure.com/fabrikam/DefaultCollection", url);
        Assert.Equal("123456789", token);

        // with remote URL 2
        Assert.True(cleaner.TryFindAzdoProject("https://tingle@dev.azure.com/fabrikam/DefaultCollection/_git/Fabrikam", out url, out token));
        Assert.Equal("https://dev.azure.com/fabrikam/DefaultCollection", url);
        Assert.Equal("123456789", token);
    }

    [Fact]
    public void MakePossibleNames_Works()
    {
        Assert.Equal(["review-app-23765", "ra-23765", "ra23765"],
            AzureCleaner.MakePossibleNames([23765]));
        Assert.Equal(["review-app-23765", "ra-23765", "ra23765", "review-app-50", "ra-50", "ra50"],
            AzureCleaner.MakePossibleNames([23765, 50]));
    }

    [Fact]
    public void NameMatchesExpectedFormat_Works()
    {
        var possibleNames = AzureCleaner.MakePossibleNames([23765]);

        // works for all in exact format
        var modified = possibleNames;
        Assert.All(modified, pn => AzureCleaner.NameMatchesExpectedFormat(possibleNames, pn));

        // works when prefixed
        modified = possibleNames.Select(pn => $"bla:{pn}").ToList();
        Assert.All(modified, pn => AzureCleaner.NameMatchesExpectedFormat(possibleNames, pn));

        // works when suffixed
        modified = possibleNames.Select(pn => $"{pn}:bla").ToList();
        Assert.All(modified, pn => AzureCleaner.NameMatchesExpectedFormat(possibleNames, pn));

        // works for AppServicePlan
        var resourceId = new ResourceIdentifier($"/subscriptions/{Guid.Empty}/resourceGroups/FABRIKAM/providers/Microsoft.Web/serverfarms/fabrikam-sites-ra23765");
        Assert.True(AzureCleaner.NameMatchesExpectedFormat(possibleNames, resourceId));

        // works for ManagedEnvironment
        resourceId = new ResourceIdentifier($"/subscriptions/{Guid.Empty}/resourceGroups/FABRIKAM/providers/Microsoft.App/managedEnvironments/fabrikam-sites-ra-23765");
        Assert.True(AzureCleaner.NameMatchesExpectedFormat(possibleNames, resourceId));

        // works for Azure SQL Database
        resourceId = new ResourceIdentifier($"/subscriptions/{Guid.Empty}/resourceGroups/FABRIKAM/providers/Microsoft.Sql/servers/fabrikam/databases/fabrikam-sites-ra-23765");
        Assert.True(AzureCleaner.NameMatchesExpectedFormat(possibleNames, resourceId));

        // skips Azure SQL Database master database
        resourceId = new ResourceIdentifier($"/subscriptions/{Guid.Empty}/resourceGroups/FABRIKAM/providers/Microsoft.Sql/servers/fabrikam/databases/master");
        Assert.False(AzureCleaner.NameMatchesExpectedFormat(possibleNames, resourceId));
    }

    [Fact]
    public async Task HandleAsync_Works()
    {
        var cache = new MemoryCache(Options.Create(new MemoryCacheOptions()));
        var optionsAccessor = Options.Create(new AzureCleanerOptions
        {
            AzdoProjects = ["https://dev.azure.com/fabrikam/DefaultCollection;123456789"],
        });
        var logger = new LoggerFactory().CreateLogger<AzureCleaner>();
        var cleaner = new ModifiedAzureCleaner(cache, optionsAccessor, logger);

        await cleaner.HandleAsync(prId: 1,
                                  remoteUrl: "https://dev.azure.com/fabrikam/DefaultCollection/_git/Fabrikam",
                                  rawProjectUrl: "https://dev.azure.com/fabrikam/DefaultCollection/_apis/projects/6ce954b1-ce1f-45d1-b94d-e6bf2464ba2c",
                                  cancellationToken: TestContext.Current.CancellationToken);
        var (possibleNames1, dryRun1) = Assert.Single(cleaner.DeleteAzureResourcesAsyncCalls);
        Assert.Equal(["review-app-1", "ra-1", "ra1"], possibleNames1);
        Assert.False(dryRun1);
        var (url, token, possibleNames2, dryRun2) = Assert.Single(cleaner.DeleteReviewAppsEnvironmentsAsyncCalls);
        Assert.Equal("https://dev.azure.com/fabrikam/DefaultCollection", url);
        Assert.Equal("123456789", token);
        Assert.Equal(["review-app-1", "ra-1", "ra1"], possibleNames2);
        Assert.False(dryRun2);
    }

    class ModifiedAzureCleaner(IMemoryCache cache, IOptions<AzureCleanerOptions> options, ILogger<AzureCleaner> logger) : AzureCleaner(cache, options, logger)
    {
        public List<(IReadOnlyCollection<string> possibleNames, bool dryRun)> DeleteAzureResourcesAsyncCalls { get; } = [];
        public List<(AzdoProjectUrl url, string? token, IReadOnlyCollection<string> possibleNames, bool dryRun)> DeleteReviewAppsEnvironmentsAsyncCalls { get; } = [];

        protected override Task DeleteAzureResourcesAsync(IReadOnlyCollection<string> possibleNames, IReadOnlyCollection<string> subscriptionIdsOrNames, bool dryRun, CancellationToken cancellationToken = default)
        {
            DeleteAzureResourcesAsyncCalls.Add((possibleNames, dryRun));
            return Task.CompletedTask;
        }

        protected override Task DeleteReviewAppsEnvironmentsAsync(AzdoProjectUrl url, string token, IReadOnlyCollection<string> possibleNames, bool dryRun, CancellationToken cancellationToken)
        {
            DeleteReviewAppsEnvironmentsAsyncCalls.Add((url, token, possibleNames, dryRun));
            return Task.CompletedTask;
        }
    }
}
