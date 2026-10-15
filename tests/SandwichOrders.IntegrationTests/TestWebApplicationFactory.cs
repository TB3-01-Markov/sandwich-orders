using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace SandwichOrders.IntegrationTests;

// Starts the real application in memory with its own temporary SQLite file and a clock the test controls.
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string databasePath =
        Path.Combine(Path.GetTempPath(), $"sandwich-orders-tests-{Guid.NewGuid():N}.db");

    private readonly string uploadsPath =
        Path.Combine(Path.GetTempPath(), $"sandwich-orders-uploads-{Guid.NewGuid():N}");

    public string UploadsPath => uploadsPath;

    // Starts at 09:00 in Brussels (08:00Z in winter), half an hour before the default deadline.
    public FakeTimeProvider Time { get; } = new(DateTimeOffset.Parse("2026-01-14T08:00:00Z"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Sqlite",
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={databasePath}",
                ["Uploads:Path"] = uploadsPath,
                ["Seed:Enabled"] = "false",
                // All test requests come from the same (empty) address; only RateLimitingTests uses a low limit.
                ["RateLimiting:PermitsPerMinute"] = "1000",
                // EF Core logs every SQL command at Information level, which buries test failures in the CI log.
                ["Logging:LogLevel:Default"] = "Warning",
                ["Logging:LogLevel:Microsoft.EntityFrameworkCore"] = "Warning",
                // The in-memory test server cannot apply [RequestSizeLimit]; the warning is expected and harmless there.
                ["Logging:LogLevel:Microsoft.AspNetCore.Routing.EndpointRoutingMiddleware"] = "Error",
                ["Logging:LogLevel:Microsoft.AspNetCore.Mvc.Filters.RequestSizeLimitFilter"] = "Error"
            }));

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            SqliteConnection.ClearAllPools();
            try
            {
                File.Delete(databasePath);
                if (Directory.Exists(uploadsPath))
                {
                    Directory.Delete(uploadsPath, recursive: true);
                }
            }
            catch (IOException)
            {
                // A leftover file in the temp folder is harmless.
            }
        }
    }
}
