using AspNetCore.Authentication.Basic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text;
using Tingle.EventBus;
using Tingle.EventBus.Transports.InMemory;
using Xunit;

namespace Tingle.AzureCleaner.Tests;

public class EndpointTests(ITestOutputHelper outputHelper)
{
    [Fact]
    public async Task EndpointReturns_Unauthorized()
    {
        await TestAsync(async (harness, client) =>
        {
            // without Authorization header
            var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/azure");
            var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Empty(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Empty(await harness.PublishedAsync(cancellationToken: TestContext.Current.CancellationToken)); // Ensure no event was published

            // with wrong value for Authorization header
            request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/azure");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes("vsts:burp-bump5")));
            response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Empty(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Empty(await harness.PublishedAsync(cancellationToken: TestContext.Current.CancellationToken)); // Ensure no event was published
        });
    }

    [Fact]
    public async Task EndpointReturns_BadRequest_NoBody()
    {
        await TestAsync(async (harness, client) =>
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/azure");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes("vsts:burp-bump")));
            var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Empty(await harness.PublishedAsync(cancellationToken: TestContext.Current.CancellationToken)); // Ensure no event was published
        });
    }

    [Fact]
    public async Task EndpointReturns_BadRequest_MissingValues()
    {
        await TestAsync(async (harness, client) =>
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/azure");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes("vsts:burp-bump")));
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Contains("\"type\":\"https://tools.ietf.org/html/rfc9110#section-15.5.1\"", body);
            Assert.Contains("\"title\":\"One or more validation errors occurred.\"", body);
            Assert.Contains("\"status\":400", body);
            Assert.Contains("\"SubscriptionId\":[\"The SubscriptionId field is required.\"]", body);
            Assert.Contains("\"EventType\":[\"The EventType field is required.\"]", body);
            Assert.Contains("\"Resource\":[\"The Resource field is required.\"]", body);
            Assert.Empty(await harness.PublishedAsync(cancellationToken: TestContext.Current.CancellationToken)); // Ensure no event was published
        });
    }

    [Fact]
    public async Task EndpointReturns_UnsupportedMediaType()
    {
        await TestAsync(async (harness, client) =>
        {
            var stream = TestSamples.AzureDevOps.GetPullRequestUpdated();
            var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/azure");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes("vsts:burp-bump")));
            request.Content = new StreamContent(stream);
            var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
            Assert.Empty(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Empty(await harness.PublishedAsync(cancellationToken: TestContext.Current.CancellationToken)); // Ensure no event was published
        });
    }

    [Fact]
    public async Task EndpointReturns_OK()
    {
        await TestAsync(async (harness, client) =>
        {
            var stream = TestSamples.AzureDevOps.GetPullRequestUpdated();
            var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/azure");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes("vsts:burp-bump")));
            request.Content = new StreamContent(stream);
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json", "utf-8");
            var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // Ensure event was published
            var evt_ctx = Assert.IsType<EventContext<AzdoCleanupEvent>>(Assert.Single(await harness.PublishedAsync(cancellationToken: TestContext.Current.CancellationToken)));
            Assert.Equal(1, evt_ctx.Event.PullRequestId);
            Assert.Equal("https://dev.azure.com/fabrikam/DefaultCollection/_git/Fabrikam", evt_ctx.Event.RemoteUrl);
            Assert.Equal("https://dev.azure.com/fabrikam/DefaultCollection/_apis/projects/6ce954b1-ce1f-45d1-b94d-e6bf2464ba2c", evt_ctx.Event.RawProjectUrl);
            Assert.Empty(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        });
    }

    private async Task TestAsync(Func<InMemoryTestHarness, HttpClient, Task> logic)
    {
        // Arrange
        var builder = new WebHostBuilder()
            .ConfigureLogging(builder => builder.AddXUnit(outputHelper))
            .ConfigureAppConfiguration(builder =>
            {
                builder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Cleaner:AzdoProjects:0"] = "https://dev.azure.com/fabrikam/DefaultCollection;123456789",
                    ["Authentication:ServiceHooks:Credentials:vsts"] = "burp-bump",
                });
            })
            .ConfigureServices((context, services) =>
            {
                var configuration = context.Configuration;
                services.AddRouting();
                services.AddCleaner(configuration.GetSection("Cleaner"));
                services.AddSingleton<AzureCleaner, ModifiedAzureCleaner>();
                services.AddEventBus(builder => builder.AddInMemoryTransport().AddInMemoryTestHarness());

                services.AddAuthentication()
                        .AddBasic<BasicUserValidationService>(options => options.Realm = "AzureCleaner");

                services.AddAuthorization(options => options.FallbackPolicy = options.DefaultPolicy);
            })
            .Configure(app =>
            {
                app.UseRouting();

                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapWebhooksAzure();
                });
            });
        using var server = new TestServer(builder);

        using var scope = server.Services.CreateScope();
        var provider = scope.ServiceProvider;

        var client = server.CreateClient();

        var harness = provider.GetRequiredService<InMemoryTestHarness>();
        await harness.StartAsync();

        try
        {
            await logic(harness, client);

            // Ensure there were no publish failures
            Assert.Empty(await harness.FailedAsync());
        }
        finally
        {
            await harness.StopAsync();
        }
    }

    class ModifiedAzureCleaner(IMemoryCache cache, IOptions<AzureCleanerOptions> options, ILogger<AzureCleaner> logger) : AzureCleaner(cache, options, logger)
    {
        protected override Task DeleteAzureResourcesAsync(IReadOnlyCollection<string> possibleNames, IReadOnlyCollection<string> subscriptionIdsOrNames, bool dryRun, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        protected override Task DeleteReviewAppsEnvironmentsAsync(AzdoProjectUrl url, string token, IReadOnlyCollection<string> possibleNames, bool dryRun, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
