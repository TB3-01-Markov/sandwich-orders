using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;

namespace SandwichOrders.Web.Infrastructure;

// Limits how often one address may send the forms that change something without being staff: placing an order,
// signing in and registering. Without it a script could clear its guest cookie and order again and again until the
// stock is empty, or try passwords at full speed.
//
// Trade-off: all trainees in the building share one public IP address, so the limit is per minute and generous
// (RateLimiting:PermitsPerMinute, default 30). Behind a reverse proxy the proxy's address would be seen instead;
// that needs forwarded headers and is not set up (the app is not deployed yet).
public static class RateLimits
{
    public const string Forms = "forms";
    public const int DefaultPermitsPerMinute = 30;

    public static IServiceCollection AddFormRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(_ => { });

        // Configured lazily from IConfiguration, so tests can change the limit (same reason as the database setup).
        services.AddOptions<RateLimiterOptions>().Configure<IConfiguration>((options, configuration) =>
        {
            var permits = Math.Max(1, configuration.GetValue("RateLimiting:PermitsPerMinute", DefaultPermitsPerMinute));

            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(Forms, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permits,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));

            options.OnRejected = async (context, ct) =>
            {
                var L = context.HttpContext.RequestServices.GetRequiredService<IStringLocalizer<SharedResource>>();
                context.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
                await context.HttpContext.Response.WriteAsync(
                    L["Too many requests. Please wait a minute and try again."].Value, ct);
            };
        });

        return services;
    }
}
