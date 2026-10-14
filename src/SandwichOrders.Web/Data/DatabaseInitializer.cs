namespace SandwichOrders.Web.Data;

// Runs once while the host starts: creates the database, the roles and (optionally) the demo data.
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

                await db.Database.EnsureCreatedAsync(cancellationToken);
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
}
