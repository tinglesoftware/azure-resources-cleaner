using AspNetCore.Authentication.Basic;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Hosting;
using System.CommandLine.NamingConventionBinder;
using System.CommandLine.Parsing;
using Tingle.AzureCleaner;

var builder = WebApplication.CreateBuilder(args);

if (builder.Configuration.GetValue<bool>("AS_WEB_APP"))
{
    builder.Services.AddApplicationInsightsTelemetry()
                    .AddSerilog(sb => sb.ConfigureSensitiveDataMasking(o => o.ExcludeProperties.AddRange(["ProjectUrl", "RemoteUrl", "ResourceId"])))
                    .AddCleaner(builder.Configuration.GetSection("Cleaner"))
                    .AddEventBus(builder.Configuration)
                    .AddHealthChecks();

    builder.Services.AddAuthentication().AddBasic<BasicUserValidationService>(options => options.Realm = "AzureCleaner");
    builder.Services.AddAuthorization(options => options.FallbackPolicy = options.DefaultPolicy); // By default, all incoming requests will be authorized according to the default policy.

    var app = builder.Build();

    app.UseRouting();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapHealthChecks("/health").AllowAnonymous();
    app.MapHealthChecks("/liveness", new HealthCheckOptions { Predicate = _ => false, }).AllowAnonymous();
    app.MapWebhooksAzure();

    await app.RunAsync();
    return 0;
}
else
{
    var root = new RootCommand("Cleanup tool for Azure resources based on Azure DevOps PRs")
    {
        new Option<int>(["-p", "--pr", "--pull-request", "--pull-request-id"], "Identifier of the pull request.") { IsRequired = true, },
        new Option<string[]>(["-s", "--subscription"], "Name or ID of subscriptions allowed. If none are provided, all subscriptions are checked."),
        new Option<string?>(["--remote", "--remote-url"], "Remote URL of the Azure DevOps repository."),
        new Option<string?>(["--project", "--project-url"], "Project URL. Overrides the remote URL when provided."),
    };
    root.Handler = CommandHandler.Create(async (IHost host, int pullRequestId, string[] subscription, string? remoteUrl, string? projectUrl, CancellationToken cancellationToken) =>
    {
        var cleaner = host.Services.GetRequiredService<AzureCleaner>();
        await cleaner.HandleAsync(prId: pullRequestId,
                                  subscriptionIdsOrNames: subscription,
                                  remoteUrl: remoteUrl,
                                  rawProjectUrl: projectUrl,
                                  cancellationToken: cancellationToken);
    });

    var clb = new CommandLineBuilder(root)
        .UseHost(_ => Host.CreateDefaultBuilder(args), host =>
        {
            host.ConfigureAppConfiguration((context, builder) =>
            {
                builder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Logging:LogLevel:Default"] = "Information",
                    ["Logging:LogLevel:Microsoft"] = "Warning",
                    ["Logging:LogLevel:Microsoft.Hosting.Lifetime"] = "Warning",

                    ["Logging:LogLevel:Tingle.AzureCleaner"] = "Trace",

                    ["Logging:Console:FormatterName"] = "Tingle",
                    ["Logging:Console:FormatterOptions:SingleLine"] = "True",
                    ["Logging:Console:FormatterOptions:IncludeCategory"] = "False",
                    ["Logging:Console:FormatterOptions:IncludeEventId"] = "False",
                    ["Logging:Console:FormatterOptions:IncludeScopes"] = "False",
                    ["Logging:Console:FormatterOptions:TimestampFormat"] = "HH:mm:ss ",
                });
            });

            host.ConfigureLogging((context, builder) =>
            {
                builder.AddConsoleFormatter<TingleConsoleFormatter, TingleConsoleOptions>();
            });

            host.ConfigureServices(services =>
            {
                services.AddCleaner(builder.Configuration.GetSection("Cleaner"));
            });
        })
        .UseDefaults();

    // Parse the incoming args and invoke the handler
    var parser = clb.Build();
    return await parser.InvokeAsync(args);
}
