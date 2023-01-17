using AspNetCore.Authentication.Basic;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Tingle.AzdoCleaner;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplicationInsightsTelemetry();
builder.Services.AddAuthentication()
                .AddBasic<BasicUserValidationService>(options => options.Realm = "AzDoCleaner");

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

app.MapAzdoNotifications();

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
        services.Configure<PullRequestUpdatedHandlerOptions>(configuration);
        services.AddSingleton<PullRequestUpdatedHandler>();

        return services;
    }

    public static RouteHandlerBuilder MapAzdoNotifications(this IEndpointRouteBuilder builder)
    {
        return builder.MapPost(
            pattern: "/service-hooks/pull-request-updated",
            handler: (PullRequestUpdatedHandler handler, [FromBody] PullRequestUpdatedEvent @event) => handler.HandleAsync(@event));
    }
}
