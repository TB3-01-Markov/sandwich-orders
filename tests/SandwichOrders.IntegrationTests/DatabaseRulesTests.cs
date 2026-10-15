using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SandwichOrders.Core;
using SandwichOrders.Core.Menu;
using SandwichOrders.Core.Orders;
using SandwichOrders.Web.Data;

namespace SandwichOrders.IntegrationTests;

// Rules that only the database can guarantee when two requests arrive at the same moment, tested against a real
// SQLite file. Two DbContexts stand for two web requests: both read first, then both try to save.
public class DatabaseRulesTests : IntegrationTestBase
{
    private static readonly DateOnly Day = new(2026, 1, 14);

    // Proves which database these tests really ran on: SQL Server when CI sets TEST_SQLSERVER, otherwise SQLite.
    [Fact]
    public void TheTestsUseTheDatabaseTheyAreMeantFor()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var expectSqlServer = Environment.GetEnvironmentVariable("TEST_SQLSERVER") is not null;

        Assert.Equal(expectSqlServer, db.Database.IsSqlServer());
        Assert.Equal(!expectSqlServer, db.Database.IsSqlite());
        if (expectSqlServer)
        {
            Assert.Empty(db.Database.GetPendingMigrations()); // the schema really came from the migrations
            Assert.Contains(db.Database.GetAppliedMigrations(), name => name.EndsWith("_InitialCreate"));
        }
    }

    [Fact]
    public void ColumnSizes_MatchTheLimitsTheFormsCheck()
    {
        using var scope = Factory.Services.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<AppDbContext>().Model;

        int? MaxLength<T>(string property) => model.FindEntityType(typeof(T))!.FindProperty(property)!.GetMaxLength();

        Assert.Equal(MenuLimits.MaxNameLength, MaxLength<Sandwich>(nameof(Sandwich.NameEn)));
        Assert.Equal(MenuLimits.MaxNameLength, MaxLength<Sandwich>(nameof(Sandwich.NameNl)));
        Assert.Equal(MenuLimits.MaxDescriptionLength, MaxLength<Sandwich>(nameof(Sandwich.DescriptionEn)));
        Assert.Equal(MenuLimits.MaxDescriptionLength, MaxLength<Sandwich>(nameof(Sandwich.DescriptionNl)));
        Assert.Equal(MenuLimits.MaxNameLength, MaxLength<Ingredient>(nameof(Ingredient.NameEn)));
        Assert.Equal(MenuLimits.MaxNameLength, MaxLength<Ingredient>(nameof(Ingredient.NameNl)));
    }

    [Fact]
    public async Task ASecondActiveOrderForTheSameDay_IsRefusedByTheDatabase()
    {
        await SaveOrderAsync(OrderFor("guest-1"));

        var exception = await Assert.ThrowsAsync<OrderingException>(() => SaveOrderAsync(OrderFor("guest-1")));

        Assert.Equal(OrderingError.AlreadyOrdered, exception.Error);
        Assert.Equal(1, await CountOrdersAsync());
    }

    [Fact]
    public async Task CancelledOrders_DoNotBlockANewOrderForTheSameDay()
    {
        await SaveOrderAsync(OrderFor("guest-1", OrderStatus.Cancelled));
        await SaveOrderAsync(OrderFor("guest-1", OrderStatus.Cancelled));

        await SaveOrderAsync(OrderFor("guest-1"));

        Assert.Equal(3, await CountOrdersAsync());
    }

    [Fact]
    public async Task OtherCustomersAndOtherDays_AreNotBlocked()
    {
        await SaveOrderAsync(OrderFor("guest-1"));
        await SaveOrderAsync(OrderFor("guest-2"));

        var nextDay = OrderFor("guest-1");
        nextDay.OrderDate = Day.AddDays(1);
        await SaveOrderAsync(nextDay);

        Assert.Equal(3, await CountOrdersAsync());
    }

    [Fact]
    public async Task TwoOrdersForTheLastPortionAtTheSameMoment_OnlyOneSucceeds()
    {
        var sandwichId = await AddSandwichWithCountedIngredientAsync(portionsInStock: 1);

        using var first = Factory.Services.CreateScope();
        using var second = Factory.Services.CreateScope();
        var firstDb = first.ServiceProvider.GetRequiredService<AppDbContext>();
        var secondDb = second.ServiceProvider.GetRequiredService<AppDbContext>();

        // Both requests read the sandwich while one portion is left, so both pass the stock check.
        var firstSandwich = (await new EfMenuRepository(firstDb).GetAsync(sandwichId))!;
        var secondSandwich = (await new EfMenuRepository(secondDb).GetAsync(sandwichId))!;
        firstSandwich.TakeFromStock(1);
        secondSandwich.TakeFromStock(1);

        var firstOrders = new EfOrderRepository(firstDb);
        firstOrders.Add(OrderFor("guest-1", sandwich: firstSandwich));
        await firstOrders.SaveChangesAsync();

        var secondOrders = new EfOrderRepository(secondDb);
        secondOrders.Add(OrderFor("guest-2", sandwich: secondSandwich));
        var exception = await Assert.ThrowsAsync<OrderingException>(() => secondOrders.SaveChangesAsync());

        Assert.Equal(OrderingError.StockChanged, exception.Error);
        Assert.Equal(1, await CountOrdersAsync());
        using var check = Factory.Services.CreateScope();
        var stock = await check.ServiceProvider.GetRequiredService<AppDbContext>().Ingredients
            .Where(i => i.NameEn == "Counted cheese").Select(i => i.StockPortions).SingleAsync();
        Assert.Equal(0, stock);
    }

    [Fact]
    public async Task AnIngredientAddedTwiceAtTheSameMoment_GivesAMessageNotAnErrorPage()
    {
        // MenuService refuses a duplicate name; this simulates a second employee who passed that check at the same time.
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repository = new EfMenuRepository(db);
        repository.Add(new Ingredient { NameEn = "Ham", NameNl = "Ham" }); // "Ham" exists already (IntegrationTestBase)

        var exception = await Assert.ThrowsAsync<MenuException>(() => repository.SaveChangesAsync());

        Assert.Equal(MenuError.IngredientExists, exception.Error);
    }

    [Fact]
    public async Task ASandwichNumberUsedTwiceAtTheSameMoment_GivesAMessageNotAnErrorPage()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repository = new EfMenuRepository(db);
        repository.Add(new Sandwich { Number = 1, NameEn = "Other", NameNl = "Ander", Price = 3m }); // number 1 = ham

        var exception = await Assert.ThrowsAsync<MenuException>(() => repository.SaveChangesAsync());

        Assert.Equal(MenuError.NumberTaken, exception.Error);
    }

    [Fact]
    public async Task AtTheWeekend_TheMenuSaysSo_AndOrdersAreRefused()
    {
        using var customer = CreateClient();
        SetBrusselsClockToUtc("2026-01-17T08:00:00Z"); // Saturday 09:00 in Brussels

        var menu = await customer.GetStringAsync("/");
        Assert.Contains("closed at the weekend", menu);

        var response = await OrderHamAsync(customer, guestName: "Ada Lovelace");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("The canteen is closed today", await customer.GetStringAsync("/"));
        Assert.Equal(0, await CountOrdersAsync());
    }

    // ---- helpers ------------------------------------------------------------------------------------------------

    private static Order OrderFor(string customerId, OrderStatus status = OrderStatus.Placed, Sandwich? sandwich = null)
    {
        var order = new Order
        {
            CustomerId = customerId,
            CustomerName = customerId,
            OrderDate = Day,
            CreatedAtUtc = new DateTime(2026, 1, 14, 8, 0, 0, DateTimeKind.Utc),
            Status = status
        };
        if (sandwich is not null)
        {
            order.Lines.Add(new OrderLine { Sandwich = sandwich, SandwichId = sandwich.Id, Bread = Bread.White, UnitPrice = sandwich.Price });
        }

        return order;
    }

    private async Task SaveOrderAsync(Order order)
    {
        using var scope = Factory.Services.CreateScope();
        var repository = new EfOrderRepository(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        repository.Add(order);
        await repository.SaveChangesAsync();
    }

    private async Task<int> CountOrdersAsync()
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Orders.CountAsync();
    }

    private async Task<int> AddSandwichWithCountedIngredientAsync(int portionsInStock)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cheese = new Ingredient { NameEn = "Counted cheese", NameNl = "Getelde kaas" };
        cheese.SetStock(portionsInStock);
        var sandwich = new Sandwich
        {
            Number = 50,
            NameEn = "Cheese sandwich",
            NameNl = "Broodje kaas",
            Price = 3.80m,
            SandwichIngredients = { new SandwichIngredient { Ingredient = cheese } }
        };
        db.Sandwiches.Add(sandwich);
        await db.SaveChangesAsync();
        return sandwich.Id;
    }
}
