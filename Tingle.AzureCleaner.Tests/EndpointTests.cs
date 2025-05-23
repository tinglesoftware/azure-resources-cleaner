﻿using AspNetCore.Authentication.Basic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text;
using Tingle.AzureCleaner.Purgers;
using Tingle.EventBus;
using Tingle.EventBus.Transports.InMemory;

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
            Assert.Equal([1], evt_ctx.Event.Ids);
            Assert.Equal("https://dev.azure.com/fabrikam/DefaultCollection/_git/Fabrikam", evt_ctx.Event.Url);
            Assert.Empty(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        });
    }

    private async Task TestAsync(Func<InMemoryTestHarness, HttpClient, Task> logic)
    {
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
                services.AddScoped<AzureResourcesPurger, ModifiedAzureResourcesPurger>();
                services.AddScoped<DevOpsPurger, ModifiedDevOpsPurger>();
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

    class ModifiedAzureResourcesPurger(ILoggerFactory loggerFactory) : AzureResourcesPurger(loggerFactory)
    {
        public override Task PurgeAsync(PurgeContext<AzureResourcesPurgeContextOptions> context, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
    class ModifiedDevOpsPurger(IMemoryCache cache, ILogger<DevOpsPurger> logger) : DevOpsPurger(cache, logger)
    {
        public override Task PurgeAsync(PurgeContext<DevOpsPurgeContextOptions> context, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
