using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SandwichOrders.Core;
using SandwichOrders.Web.Data;

namespace SandwichOrders.IntegrationTests;

// What happens on the hosting when a new version with a new migration is deployed: the database already has
// orders, and they must still be there afterwards. Runs in CI against SQL Server (TEST_SQLSERVER); the migrations
// are SQL Server migrations, so with SQLite there is nothing to check.
public class MigrationUpgradeTests
{
    private static readonly string? SqlServer = Environment.GetEnvironmentVariable("TEST_SQLSERVER");

    [Fact]
    public async Task DataWrittenWithTheFirstSchema_SurvivesAllLaterMigrations()
    {
        if (SqlServer is null)
        {
            return;
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer($"{SqlServer.TrimEnd(';')};Database=SandwichOrdersUpgrade_{Guid.NewGuid():N}")
            .Options;
        await using var db = new AppDbContext(options);
        try
        {
            var migrations = db.Database.GetMigrations().ToList();
            Assert.True(migrations.Count >= 2, "Needs at least two migrations to test an upgrade.");

            // 1. The database as the first version created it.
            await db.GetService<IMigrator>().MigrateAsync(migrations[0]);

            // 2. Data in it, written with plain SQL that only uses the columns of that first version, so this test keeps
            //    working when later migrations add columns.
            await db.Database.ExecuteSqlRawAsync("""
                DECLARE @ingredient int, @sandwich int, @order int;
                INSERT INTO Ingredients (NameEn, NameNl, IsAvailable, StockPortions, StockVersion) VALUES (N'Ham', N'Hesp', 1, 5, 0);
                SET @ingredient = SCOPE_IDENTITY();
                INSERT INTO Sandwiches (Number, NameEn, NameNl, DescriptionEn, DescriptionNl, Price, ImagePath, Kind,
                                        StockCount, StockVersion, IsActive, IsTemporarilyUnavailable)
                VALUES (1, N'Ham sandwich', N'Broodje hesp', N'', N'', 4.20, NULL, N'Prepared', NULL, 0, 1, 0);
                SET @sandwich = SCOPE_IDENTITY();
                INSERT INTO SandwichIngredients (SandwichId, IngredientId, Portions) VALUES (@sandwich, @ingredient, 1);
                INSERT INTO Orders (CustomerId, CustomerName, CustomerPhone, OrderDate, CreatedAtUtc, Status)
                VALUES (N'guest-1', N'Ada Lovelace', NULL, '2026-01-14', '2026-01-14T08:00:00', 2);
                SET @order = SCOPE_IDENTITY();
                INSERT INTO OrderLines (OrderId, SandwichId, Bread, Quantity, UnitPrice) VALUES (@order, @sandwich, 1, 1, 5.00);
                """);

            // 3. Deploy the newest version: all remaining migrations.
            await db.Database.MigrateAsync();

            // 4. Everything is still there and readable with the newest model.
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            var order = await db.Orders.Include(o => o.Lines).ThenInclude(l => l.Sandwich).SingleAsync();
            Assert.Equal("Ada Lovelace", order.CustomerName);
            Assert.Equal(OrderStatus.Collected, order.Status);
            Assert.Equal(new DateOnly(2026, 1, 14), order.OrderDate);
            var line = Assert.Single(order.Lines);
            Assert.Equal(Bread.WholeGrain, line.Bread);
            Assert.Equal(5.00m, line.UnitPrice);
            Assert.Equal("Ham sandwich", line.Sandwich.NameEn);
            Assert.Equal(5, (await db.Ingredients.SingleAsync()).StockPortions);
            Assert.Equal(0, await db.DataProtectionKeys.CountAsync()); // a table from a later migration exists now
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
