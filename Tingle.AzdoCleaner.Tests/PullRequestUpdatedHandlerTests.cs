using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Xunit;

namespace Tingle.AzdoCleaner.Tests;

public class PullRequestUpdatedHandlerTests
{
    [Fact]
    public void TryFindProject_Works()
    {
        var cache = new MemoryCache(Options.Create(new MemoryCacheOptions()));
        var optionsAccessor = Options.Create(new PullRequestUpdatedHandlerOptions
        {
            Projects = new List<string>
            {
                "https://dev.azure.com/fabrikam/DefaultCollection;123456789",
                "https://dev.azure.com/fabrikam/cea8cb01-dd13-4588-b27a-55fa170e4e94;987654321",
            }
        });
        var logger = new LoggerFactory().CreateLogger<PullRequestUpdatedHandler>();
        var handler = new PullRequestUpdatedHandler(cache, optionsAccessor, logger);

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
    public async Task HandleAsync_Works()
    {
        var cache = new MemoryCache(Options.Create(new MemoryCacheOptions()));
        var optionsAccessor = Options.Create(new PullRequestUpdatedHandlerOptions
        {
            Projects = new List<string>
            {
                "https://dev.azure.com/fabrikam/DefaultCollection;123456789",
            }
        });
        var logger = new LoggerFactory().CreateLogger<PullRequestUpdatedHandler>();
        var handler = new ModifiedPullRequestUpdatedHandler(cache, optionsAccessor, logger);

        var stream = TestSamples.AzureDevOps.GetPullRequestUpdated();
        var payload = await JsonSerializer.DeserializeAsync<PullRequestUpdatedEvent>(stream);
        await handler.HandleAsync(payload!);
        var (url, token, prIds) = Assert.Single(handler.Calls);
        Assert.Equal("https://dev.azure.com/fabrikam/DefaultCollection", url);
        Assert.Equal("123456789", token);
        Assert.Equal(new[] { 1 }, prIds);
    }

    class ModifiedPullRequestUpdatedHandler : PullRequestUpdatedHandler
    {
        public ModifiedPullRequestUpdatedHandler(IMemoryCache cache, IOptions<PullRequestUpdatedHandlerOptions> options, ILogger<PullRequestUpdatedHandler> logger)
            : base(cache, options, logger) { }

        public List<(AzdoProjectUrl url, string? token, IEnumerable<int> prIds)> Calls { get; } = new();

        protected override Task DeleteReviewAppResourcesAsync(AzdoProjectUrl url, string? token, IEnumerable<int> prIds, CancellationToken cancellationToken = default)
        {
            Calls.Add((url, token, prIds));
            return Task.CompletedTask;
        }
    }
}
