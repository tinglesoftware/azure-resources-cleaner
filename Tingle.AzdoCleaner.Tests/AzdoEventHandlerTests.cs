using Azure.Core;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Xunit;

namespace Tingle.AzdoCleaner.Tests;

public class AzdoEventHandlerTests
{
    [Fact]
    public void TryFindProject_Works()
    {
        var cache = new MemoryCache(Options.Create(new MemoryCacheOptions()));
        var optionsAccessor = Options.Create(new AzureDevOpsEventHandlerOptions
        {
            Projects = new List<string>
            {
                "https://dev.azure.com/fabrikam/DefaultCollection;123456789",
                "https://dev.azure.com/fabrikam/cea8cb01-dd13-4588-b27a-55fa170e4e94;987654321",
            }
        });
        var logger = new LoggerFactory().CreateLogger<AzdoEventHandler>();
        var handler = new AzdoEventHandler(cache, optionsAccessor, logger);

        // not found
        Assert.False(handler.TryFindProject("https://dev.azure.com/fabrikam/6ce954b1-ce1f-45d1-b94d-e6bf2464ba2c", out _, out _));

        // proper one
        Assert.True(handler.TryFindProject("https://dev.azure.com/fabrikam/DefaultCollection", out var url, out var token));
        Assert.Equal("https://dev.azure.com/fabrikam/DefaultCollection", url);
        Assert.Equal("123456789", token);

        // with project id
        Assert.True(handler.TryFindProject("https://dev.azure.com/fabrikam/_apis/projects/cea8cb01-dd13-4588-b27a-55fa170e4e94", out url, out token));
        Assert.Equal("https://dev.azure.com/fabrikam/cea8cb01-dd13-4588-b27a-55fa170e4e94", url);
        Assert.Equal("987654321", token);

        // with remote URL 1
        Assert.True(handler.TryFindProject("https://dev.azure.com/fabrikam/DefaultCollection/_git/Fabrikam", out url, out token));
        Assert.Equal("https://dev.azure.com/fabrikam/DefaultCollection", url);
        Assert.Equal("123456789", token);

        // with remote URL 2
        Assert.True(handler.TryFindProject("https://tingle@dev.azure.com/fabrikam/DefaultCollection/_git/Fabrikam", out url, out token));
        Assert.Equal("https://dev.azure.com/fabrikam/DefaultCollection", url);
        Assert.Equal("123456789", token);
    }

    [Fact]
    public void MakePossibleNames_Works()
    {
        Assert.Equal(new[] { "review-app-23765", "ra-23765", "ra23765", },
            AzdoEventHandler.MakePossibleNames(new[] { 23765, }));
        Assert.Equal(new[] { "review-app-23765", "ra-23765", "ra23765", "review-app-50", "ra-50", "ra50", },
            AzdoEventHandler.MakePossibleNames(new[] { 23765, 50, }));
    }

    [Fact]
    public void NameMatchesExpectedFormat_Works()
    {
        var possibleNames = AzdoEventHandler.MakePossibleNames(new[] { 23765, });

        // works for all in exact format
        var modified = possibleNames;
        Assert.All(modified, pn => AzdoEventHandler.NameMatchesExpectedFormat(possibleNames, pn));

        // works when prefixed
        modified = possibleNames.Select(pn => $"bla:{pn}").ToList();
        Assert.All(modified, pn => AzdoEventHandler.NameMatchesExpectedFormat(possibleNames, pn));

        // works when suffixed
        modified = possibleNames.Select(pn => $"{pn}:bla").ToList();
        Assert.All(modified, pn => AzdoEventHandler.NameMatchesExpectedFormat(possibleNames, pn));

        // works for AppServicePlan
        var resourceId = new ResourceIdentifier($"/subscriptions/{Guid.Empty}/resourceGroups/FABRIKAM/providers/Microsoft.Web/serverfarms/fabrikam-sites-ra23765");
        Assert.True(AzdoEventHandler.NameMatchesExpectedFormat(possibleNames, resourceId));

        // works for ManagedEnvironment
        resourceId = new ResourceIdentifier($"/subscriptions/{Guid.Empty}/resourceGroups/FABRIKAM/providers/Microsoft.App/managedEnvironments/fabrikam-sites-ra-23765");
        Assert.True(AzdoEventHandler.NameMatchesExpectedFormat(possibleNames, resourceId));

        // works for Azure SQL Database
        resourceId = new ResourceIdentifier($"/subscriptions/{Guid.Empty}/resourceGroups/FABRIKAM/providers/Microsoft.Sql/servers/fabrikam/databases/fabrikam-sites-ra-23765");
        Assert.True(AzdoEventHandler.NameMatchesExpectedFormat(possibleNames, resourceId));

        // skips Azure SQL Database master database
        resourceId = new ResourceIdentifier($"/subscriptions/{Guid.Empty}/resourceGroups/FABRIKAM/providers/Microsoft.Sql/servers/fabrikam/databases/master");
        Assert.False(AzdoEventHandler.NameMatchesExpectedFormat(possibleNames, resourceId));
    }

    [Fact]
    public async Task HandleAsync_Works()
    {
        var cache = new MemoryCache(Options.Create(new MemoryCacheOptions()));
        var optionsAccessor = Options.Create(new AzureDevOpsEventHandlerOptions
        {
            Projects = new List<string>
            {
                "https://dev.azure.com/fabrikam/DefaultCollection;123456789",
            }
        });
        var logger = new LoggerFactory().CreateLogger<AzdoEventHandler>();
        var handler = new ModifiedAzdoEventHandler(cache, optionsAccessor, logger);

        var stream = TestSamples.AzureDevOps.GetPullRequestUpdated();
        var payload = await JsonSerializer.DeserializeAsync<AzdoEvent>(stream);
        await handler.HandleAsync(payload!);
        var (url, token, prIds) = Assert.Single(handler.Calls);
        Assert.Equal("https://dev.azure.com/fabrikam/DefaultCollection", url);
        Assert.Equal("123456789", token);
        Assert.Equal(new[] { 1 }, prIds);
    }

    class ModifiedAzdoEventHandler : AzdoEventHandler
    {
        public ModifiedAzdoEventHandler(IMemoryCache cache, IOptions<AzureDevOpsEventHandlerOptions> options, ILogger<AzdoEventHandler> logger)
            : base(cache, options, logger) { }

        public List<(AzdoProjectUrl url, string? token, IEnumerable<int> prIds)> Calls { get; } = new();

        protected override Task DeleteReviewAppResourcesAsync(AzdoProjectUrl url, string? token, IEnumerable<int> prIds, CancellationToken cancellationToken = default)
        {
            Calls.Add((url, token, prIds));
            return Task.CompletedTask;
        }
    }
}
