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
using Xunit.Abstractions;

namespace Tingle.AzdoCleaner.Tests;

public class EndpointTests
{
    private readonly ITestOutputHelper outputHelper;

    public EndpointTests(ITestOutputHelper outputHelper)
    {
        this.outputHelper = outputHelper ?? throw new ArgumentNullException(nameof(outputHelper));
    }

    [Fact]
    public async Task EndpointReturns_Unauthorized()
    {
        await TestAsync(async (harness, client) =>
        {
            // without Authorization header
            var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/azure");
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Empty(await response.Content.ReadAsStringAsync());
            Assert.Empty(await harness.PublishedAsync()); // Ensure no event was published

            // with wrong value for Authorization header
            request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/azure");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes("vsts:burp-bump5")));
            response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Empty(await response.Content.ReadAsStringAsync());
            Assert.Empty(await harness.PublishedAsync()); // Ensure no event was published
        });
    }

    [Fact]
    public async Task EndpointReturns_BadRequest_NoBody()
    {
        await TestAsync(async (harness, client) =>
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/azure");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes("vsts:burp-bump")));
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(await response.Content.ReadAsStringAsync());
            Assert.Empty(await harness.PublishedAsync()); // Ensure no event was published
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
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("\"type\":\"https://tools.ietf.org/html/rfc7231#section-6.5.1\"", body);
            Assert.Contains("\"title\":\"One or more validation errors occurred.\"", body);
            Assert.Contains("\"status\":400", body);
            Assert.Contains("\"SubscriptionId\":[\"The SubscriptionId field is required.\"]", body);
            Assert.Contains("\"EventType\":[\"The EventType field is required.\"]", body);
            Assert.Contains("\"Resource\":[\"The Resource field is required.\"]", body);
            Assert.Empty(await harness.PublishedAsync()); // Ensure no event was published
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
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
            Assert.Empty(await response.Content.ReadAsStringAsync());
            Assert.Empty(await harness.PublishedAsync()); // Ensure no event was published
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
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // Ensure event was published
            var evt_ctx = Assert.IsType<EventContext<AzdoCleanupEvent>>(Assert.Single(await harness.PublishedAsync()));
            Assert.Equal(1, evt_ctx.Event.PullRequestId);
            Assert.Equal("https://dev.azure.com/fabrikam/DefaultCollection/_git/Fabrikam", evt_ctx.Event.RemoteUrl);
            Assert.Equal("https://dev.azure.com/fabrikam/DefaultCollection/_apis/projects/6ce954b1-ce1f-45d1-b94d-e6bf2464ba2c", evt_ctx.Event.RawProjectUrl);
            Assert.Empty(await response.Content.ReadAsStringAsync());
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
                    ["Handler:Projects:0"] = "https://dev.azure.com/fabrikam/DefaultCollection;123456789",
                    ["Authentication:ServiceHooks:Credentials:vsts"] = "burp-bump",
                });
            })
            .ConfigureServices((context, services) =>
            {
                var configuration = context.Configuration;
                services.AddRouting();
                services.AddNotificationsHandler(configuration.GetSection("Handler"));
                services.AddSingleton<AzdoEventHandler, ModifiedAzdoEventHandler>();
                services.AddEventBus(builder => builder.AddInMemoryTransport().AddInMemoryTestHarness());

                services.AddAuthentication()
                        .AddBasic<BasicUserValidationService>(options => options.Realm = "AzdoCleaner");

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
