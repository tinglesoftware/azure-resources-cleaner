using AspNetCore.Authentication.Basic;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using System.CommandLine;
using System.CommandLine.Invocation;
using Tingle.AzureCleaner;

var isWebApp = Host.CreateApplicationBuilder().Configuration.GetValue<bool>("AS_WEB_APP");

if (isWebApp)
{
    var builder = WebApplication.CreateBuilder(args);
    builder.AddOpenTelemetry(true);

    builder.Services.AddCleaner(builder.Configuration.GetSection("Cleaner"))
                    .AddEventBus(builder.Configuration)
                    .AddHealthChecks();

    builder.Services.AddAuthentication().AddBasic<BasicUserValidationService>(options => options.Realm = "AzureCleaner");
    builder.Services.AddAuthorization(options => options.FallbackPolicy = options.DefaultPolicy); // By default, all incoming requests will be authorized according to the default policy.

    // build the app
    var app = builder.Build();

    // configure the app
    app.UseRouting();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapHealthChecks("/health").AllowAnonymous();
    app.MapHealthChecks("/liveness", new HealthCheckOptions { Predicate = _ => false, }).AllowAnonymous();
    app.MapWebhooksAzure();

    // run the application until terminated
    await app.RunAsync();
    return 0;
}
else
{
    // create the builder
    var builder = Host.CreateApplicationBuilder();
    builder.AddOpenTelemetry(false);

    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Logging:LogLevel:Default"] = "Information",
        ["Logging:LogLevel:Microsoft"] = "Warning",
        ["Logging:LogLevel:Microsoft.Hosting.Lifetime"] = "Warning",
        ["Logging:LogLevel:OpenTelemetry:Default"] = "Warning", // only export warnings to OpenTelemetry

        ["Logging:LogLevel:Tingle.AzureCleaner"] = "Trace",

        ["Logging:Console:FormatterName"] = "Tingle",
        ["Logging:Console:FormatterOptions:SingleLine"] = "True",
        ["Logging:Console:FormatterOptions:IncludeCategory"] = "False",
        ["Logging:Console:FormatterOptions:IncludeEventId"] = "False",
        ["Logging:Console:FormatterOptions:IncludeScopes"] = "False",
        ["Logging:Console:FormatterOptions:TimestampFormat"] = "HH:mm:ss ",
    });

    builder.Logging.AddConsoleFormatter<TingleConsoleFormatter, TingleConsoleOptions>();
    builder.Services.AddCleaner(builder.Configuration.GetSection("Cleaner"));

    // build and start the host
    using var host = builder.Build();
    await host.StartAsync();

    // prepare options
    var pullRequestIdOption = new Option<int>(name: "--pull-request", aliases: ["-p", "--pr", "--pull-request-id"]) { Description = "Identifier of the pull request.", Required = true, };
    var subscriptionOption = new Option<string[]>(name: "--subscription", aliases: ["-s"]) { Description = "Name or ID of subscriptions allowed. If none are provided, all subscriptions are checked.", };
    var remoteUrlOption = new Option<string?>(name: "--remote-url", aliases: ["--remote"]) { Description = "Remote URL of the Azure DevOps repository.", };
    var projectUrlOption = new Option<string?>(name: "--project-url", aliases: ["--project"]) { Description = "Project URL. Overrides the remote URL when provided.", };

    var root = new RootCommand("Cleanup tool for Azure resources based on Azure DevOps PRs")
    {
        pullRequestIdOption,
        subscriptionOption,
        remoteUrlOption,
        projectUrlOption,
    };
    root.Action = System.CommandLine.NamingConventionBinder.CommandHandler.Create(
        async (ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            var pullRequestId = parseResult.GetValue(pullRequestIdOption);
            var subscription = parseResult.GetValue(subscriptionOption);
            var remoteUrl = parseResult.GetValue(remoteUrlOption);
            var projectUrl = parseResult.GetValue(projectUrlOption);

            var cleaner = host.Services.GetRequiredService<AzureCleaner>();
            await cleaner.HandleAsync(prId: pullRequestId,
                                      subscriptionIdsOrNames: subscription,
                                      remoteUrl: remoteUrl,
                                      rawProjectUrl: projectUrl,
                                      cancellationToken: cancellationToken);
            return 0;
        });

    var configuration = new CommandLineConfiguration(root);

    // execute the command
    try
    {
        return await configuration.InvokeAsync(args);
    }
    finally
    {
        // stop the host, this will stop and dispose the services which flushes OpenTelemetry data
        await host.StopAsync();
    }
}
