using AspNetCore.Authentication.Basic;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using MiniValidation;
using Tingle.AzdoCleaner;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplicationInsightsTelemetry();
builder.Services.AddAuthentication()
                .AddBasic<BasicUserValidationService>(options => options.Realm = "AzdoCleaner");

builder.Services.AddAuthorization(options =>
{
    // By default, all incoming requests will be authorized according to the default policy.
    options.FallbackPolicy = options.DefaultPolicy;
});

builder.AddNotificationsHandler();

// Add health checks
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapHealthChecks("/liveness", new HealthCheckOptions { Predicate = _ => false, }).AllowAnonymous();

app.MapWebhooksAzure();

await app.RunAsync();

internal static class ApplicationExtensions
{
    public static WebApplicationBuilder AddNotificationsHandler(this WebApplicationBuilder builder)
    {
        builder.Services.AddNotificationsHandler(builder.Configuration.GetSection("Handler"));
        return builder;
    }

    public static IServiceCollection AddNotificationsHandler(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache();
        services.Configure<AzureDevOpsEventHandlerOptions>(configuration);
        services.AddSingleton<AzdoEventHandler>();

        return services;
    }

    public static IEndpointConventionBuilder MapWebhooksAzure(this IEndpointRouteBuilder builder)
    {
        return builder.MapPost("/webhooks/azure", (AzdoEventHandler handler, [FromBody] AzdoEvent model) =>
        {
            if (!MiniValidator.TryValidate(model, out var errors)) return Results.ValidationProblem(errors);

            /*
             * This is not awaited because the process may take longer that the caller's request timeout (suspected to be 100 seconds).
             * Azure DevOps seeps to disabled [Enabled (restricted)] the service hook when one (may be two) request is timed out which
             * can make one wonder why new PR completions are not getting handled.
             */
            _ = handler.HandleAsync(model);
            return Results.Ok();
        });
    }
}
