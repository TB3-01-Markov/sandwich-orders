using Microsoft.EntityFrameworkCore;

namespace SandwichOrders.Web.Data;

// Runs once while the host starts: creates or migrates the database, the roles and (optionally) the demo data.
// Running it as a hosted service means the database is ready before the first request and before tests start.
public class DatabaseInitializer(
    IServiceProvider services,
    IConfiguration configuration,
    ILogger<DatabaseInitializer> logger) : IHostedService
{
    private const int MaxAttempts = 10;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // SQL Server in Docker needs a few seconds before it accepts connections, so we retry.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var scope = services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                // SQL Server (Docker, hosting): the migrations in Data/Migrations create and update the schema, so an
                // existing database keeps its data when the model changes.
                // SQLite (development on the laptop, most tests): created straight from the model. An existing file is
                // never changed by EnsureCreated; in Development (Database:RecreateOutdatedSqlite) a file that is older
                // than the model is deleted and created again with the demo data.
                logger.LogInformation("Database provider: {Provider}", db.Database.ProviderName);
                if (db.Database.IsSqlServer())
                {
                    await db.Database.MigrateAsync(cancellationToken);
                }
                else
                {
                    await db.Database.EnsureCreatedAsync(cancellationToken);
                    await RecreateOutdatedSqliteAsync(db, cancellationToken);
                }
                await DbSeeder.EnsureRolesAsync(scope.ServiceProvider);

                if (configuration.GetValue<bool>("Seed:Enabled"))
                {
                    await DbSeeder.SeedDemoDataAsync(scope.ServiceProvider, configuration);
                }

                return;
            }
            catch (Exception exception) when (attempt < MaxAttempts && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Database not ready (attempt {Attempt} of {Max}). Retrying in 3 seconds.", attempt, MaxAttempts);
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // Only when switched on (appsettings.Development.json): it deletes the file, so never for real data.
    private async Task RecreateOutdatedSqliteAsync(AppDbContext db, CancellationToken ct)
    {
        if (!configuration.GetValue<bool>("Database:RecreateOutdatedSqlite"))
        {
            return;
        }

        var missing = await SqliteSchema.FindMissingAsync(db, ct);
        if (missing.Count == 0)
        {
            return;
        }

        logger.LogWarning(
            "The SQLite file is older than the model (missing: {Missing}). It is deleted and created again with the demo data.",
            string.Join(", ", missing));
        await db.Database.EnsureDeletedAsync(ct);
        await db.Database.EnsureCreatedAsync(ct);
    }
}
