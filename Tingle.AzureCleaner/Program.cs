using AspNetCore.Authentication.Basic;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using System.CommandLine;
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

        ["Logging:Console:FormatterName"] = "cli",
        ["Logging:Console:FormatterOptions:SingleLine"] = "True",
        ["Logging:Console:FormatterOptions:IncludeCategory"] = "False",
        ["Logging:Console:FormatterOptions:IncludeEventId"] = "False",
        ["Logging:Console:FormatterOptions:TimestampFormat"] = "yyyy-MM-dd HH:mm:ss ",
    });

    // configure logging
    builder.Logging.AddCliConsole();

    // register services
    builder.Services.AddCleaner(builder.Configuration.GetSection("Cleaner"));

    // build and start the host
    using var host = builder.Build();
    await host.StartAsync();

    // prepare options
    var pullRequestIdOption = new Option<int>(name: "--pull-request", aliases: ["-p", "--pr", "--pull-request-id"]) { Description = "Identifier of the pull request.", Required = true, };
    var subscriptionsOption = new Option<string[]>(name: "--subscription", aliases: ["-s"]) { Description = "Name or ID of subscriptions allowed. If none are provided, all subscriptions are checked.", };
    var urlOption = new Option<string?>(name: "--url", aliases: ["-u"])
    {
        Description = "Remote URL of the Azure DevOps repository or the project URL."
                    + " Example: https://dev.azure.com/fabrikam/DefaultCollection/_git/Fabrikam"
                    + " or https://dev.azure.com/fabrikam/DefaultCollection",
    };
    var tokenOption = new Option<string>(name: "--token", aliases: ["-t"]) { Description = "Token for accessing Azure DevOps", };
    var dryRunOption = new Option<bool>(name: "--dry-run") { Description = "Performs a trial run without making any changes. Outputs the actions that would be taken.", };

    // prepare the root command
    var root = new RootCommand("Cleanup tool for Azure resources based on Azure DevOps PRs")
    {
        pullRequestIdOption,
        subscriptionsOption,
        urlOption,
        tokenOption,
        dryRunOption,
    };
    root.Action = System.CommandLine.NamingConventionBinder.CommandHandler.Create(
        async (ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            using var scope = host.Services.CreateScope();
            var provider = scope.ServiceProvider;

            var pullRequestId = parseResult.GetValue(pullRequestIdOption);
            var subscriptions = parseResult.GetValue(subscriptionsOption);
            var url = parseResult.GetValue(urlOption);
            var token = parseResult.GetValue(tokenOption);
            var dryRun = parseResult.GetValue(dryRunOption);

            // prepare projects
            var projects = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(url))
            {
                if (string.IsNullOrWhiteSpace(token))
                {
                    var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(Tingle.AzureCleaner));
                    logger.LogError("When then url is supplied the token must also be provided");
                    return -1;
                }
                projects[url] = token;
            }

            var cleaner = provider.GetRequiredService<AzureCleaner>();
            await cleaner.HandleAsync(ids: [pullRequestId],
                                      subscriptions: subscriptions,
                                      projects: projects,
                                      url: url,
                                      dryRun: dryRun,
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
