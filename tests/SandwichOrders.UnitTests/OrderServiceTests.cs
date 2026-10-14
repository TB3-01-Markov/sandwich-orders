using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SandwichOrders.Core;
using SandwichOrders.Core.Menu;
using SandwichOrders.Core.Orders;

namespace SandwichOrders.UnitTests;

// Tests for US-04, US-05 and US-07. The storage is replaced by NSubstitute and the clock by FakeTimeProvider.
// Belgium is UTC+1 in winter and UTC+2 in summer, so 09:30 in Brussels is 08:30Z in January and 07:30Z in July.
// NOTE: FakeTimeProvider cannot go back in time. Every test starts at 2026-01-14 08:00Z (09:00 Brussels),
// so a time set inside a test must not be earlier than that.
public class OrderServiceTests
{
    private const string CustomerId = "customer-1";

    private readonly IOrderRepository orders = Substitute.For<IOrderRepository>();
    private readonly IMenuRepository menu = Substitute.For<IMenuRepository>();
    private readonly IOrderingSettings settings = Substitute.For<IOrderingSettings>();
    private readonly FakeTimeProvider clock = new();
    private readonly OrderService service;

    public OrderServiceTests()
    {
        settings.GetDeadlineAsync(Arg.Any<CancellationToken>()).Returns(new TimeOnly(9, 30));
        menu.GetAsync(1, Arg.Any<CancellationToken>()).Returns(HamSandwich());
        SetUtc("2026-01-14T08:00:00Z");

        service = new OrderService(orders, menu, settings, clock);
    }

    // ---- US-04: placing an order -------------------------------------------------------------------------

    [Theory]
    [InlineData("2026-01-14T08:29:59Z")] // 09:29:59 in Brussels (winter)
    [InlineData("2026-07-14T07:29:59Z")] // 09:29:59 in Brussels (summer)
    public async Task Place_JustBeforeTheDeadline_IsAccepted(string utc)
    {
        SetUtc(utc);

        var order = await service.PlaceAsync(CustomerId, "Ada Lovelace", 1, Bread.White);

        Assert.Equal(OrderStatus.Placed, order.Status);
    }

    [Theory]
    [InlineData("2026-01-14T08:30:00Z")] // exactly 09:30 (winter)
    [InlineData("2026-07-14T07:30:00Z")] // exactly 09:30 (summer)
    [InlineData("2026-01-14T12:00:00Z")] // the afternoon
    public async Task Place_AtOrAfterTheDeadline_IsRefused(string utc)
    {
        SetUtc(utc);

        var exception = await Assert.ThrowsAsync<OrderingException>(
            () => service.PlaceAsync(CustomerId, "Ada Lovelace", 1, Bread.White));

        Assert.Equal(OrderingError.DeadlinePassed, exception.Error);
        orders.DidNotReceive().Add(Arg.Any<Order>());
    }

    [Fact]
    public async Task Place_UsesTheDeadlineSetByTheCanteenEmployee()
    {
        settings.GetDeadlineAsync(Arg.Any<CancellationToken>()).Returns(new TimeOnly(10, 0));

        SetUtc("2026-01-14T08:45:00Z"); // 09:45, after the old deadline but before the new one
        await service.PlaceAsync(CustomerId, "Ada Lovelace", 1, Bread.White);

        SetUtc("2026-01-14T09:00:00Z"); // 10:00 exactly
        var exception = await Assert.ThrowsAsync<OrderingException>(
            () => service.PlaceAsync("customer-2", "Grace Hopper", 1, Bread.White));
        Assert.Equal(OrderingError.DeadlinePassed, exception.Error);
    }

    [Fact]
    public async Task Place_WhenTheCustomerAlreadyHasAnOrderToday_IsRefused()
    {
        orders.GetActiveForCustomerAsync(CustomerId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new Order { CustomerId = CustomerId });

        var exception = await Assert.ThrowsAsync<OrderingException>(
            () => service.PlaceAsync(CustomerId, "Ada Lovelace", 1, Bread.White));

        Assert.Equal(OrderingError.AlreadyOrdered, exception.Error);
        orders.DidNotReceive().Add(Arg.Any<Order>());
    }

    [Theory]
    [InlineData("switched-off")]
    [InlineData("removed-from-menu")]
    [InlineData("ingredient-out-of-stock")]
    [InlineData("does-not-exist")]
    public async Task Place_ASandwichThatCannotBeOrdered_IsRefused(string reason)
    {
        Sandwich? sandwich = HamSandwich(ingredientInStock: reason != "ingredient-out-of-stock");
        if (reason == "switched-off") sandwich.IsTemporarilyUnavailable = true;
        if (reason == "removed-from-menu") sandwich.IsActive = false;
        if (reason == "does-not-exist") sandwich = null;
        menu.GetAsync(1, Arg.Any<CancellationToken>()).Returns(sandwich);

        var exception = await Assert.ThrowsAsync<OrderingException>(
            () => service.PlaceAsync(CustomerId, "Ada Lovelace", 1, Bread.White));

        Assert.Equal(OrderingError.SandwichUnavailable, exception.Error);
        orders.DidNotReceive().Add(Arg.Any<Order>());
    }

    [Fact]
    public async Task Place_SavesTheOrderWithNameBreadPriceAndBelgianDate()
    {
        var order = await service.PlaceAsync(CustomerId, "Ada Lovelace", 1, Bread.WholeGrain);

        Assert.Equal(new DateOnly(2026, 1, 14), order.OrderDate);
        orders.Received(1).Add(Arg.Is<Order>(o =>
            o.CustomerId == CustomerId
            && o.CustomerName == "Ada Lovelace"
            && o.Status == OrderStatus.Placed
            && o.Lines.Count == 1
            && o.Lines.Single().SandwichId == 1
            && o.Lines.Single().Bread == Bread.WholeGrain
            && o.Lines.Single().Quantity == 1
            && o.Lines.Single().UnitPrice == 4.20m));
        _ = orders.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Place_AfterMidnightInBrussels_UsesTheBelgianDateNotTheUtcDate()
    {
        SetUtc("2026-01-14T23:30:00Z"); // 00:30 on 15 January in Brussels

        var order = await service.PlaceAsync(CustomerId, "Ada Lovelace", 1, Bread.White);

        Assert.Equal(new DateOnly(2026, 1, 15), order.OrderDate);
    }

    [Fact]
    public async Task Place_ALaterPriceChangeDoesNotChangeTheOrder()
    {
        var sandwich = HamSandwich();
        menu.GetAsync(1, Arg.Any<CancellationToken>()).Returns(sandwich);

        var order = await service.PlaceAsync(CustomerId, "Ada Lovelace", 1, Bread.White);
        sandwich.Price = 9.99m;

        Assert.Equal(4.20m, order.Lines.Single().UnitPrice);
    }

    // ---- US-05: cancelling -------------------------------------------------------------------------------

    [Fact]
    public async Task Cancel_BeforeTheDeadline_CancelsTheOrder()
    {
        var order = ExistingOrder();

        await service.CancelAsync(CustomerId);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        _ = orders.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancel_AtTheDeadline_IsRefusedAndTheOrderStaysActive()
    {
        var order = ExistingOrder();
        SetUtc("2026-01-14T08:30:00Z");

        var exception = await Assert.ThrowsAsync<OrderingException>(() => service.CancelAsync(CustomerId));

        Assert.Equal(OrderingError.DeadlinePassed, exception.Error);
        Assert.Equal(OrderStatus.Placed, order.Status);
        _ = orders.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancel_WithoutAnOrder_ReportsThatThereIsNone()
    {
        var exception = await Assert.ThrowsAsync<OrderingException>(() => service.CancelAsync(CustomerId));

        Assert.Equal(OrderingError.OrderNotFound, exception.Error);
    }

    // ---- US-07: what the canteen has to prepare ----------------------------------------------------------

    [Fact]
    public async Task Overview_CountsEverySandwichPerBreadType_AndTheTotals()
    {
        var ham = HamSandwich();
        var turkey = new Sandwich { Id = 2, Number = 2, NameEn = "Turkey", NameNl = "Kalkoen", Price = 5.00m };
        IReadOnlyList<Order> todays =
        [
            OrderOf("Ada", Line(ham, Bread.White)),
            OrderOf("Grace", Line(ham, Bread.White)),
            OrderOf("Linus", Line(ham, Bread.WholeGrain)),
            OrderOf("Alan", Line(turkey, Bread.White))
        ];
        orders.GetActiveForDateAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(todays);

        var overview = await service.GetPreparationOverviewAsync();

        Assert.Collection(
            overview.Rows,
            row => AssertRow(row, 1, Bread.White, 2),
            row => AssertRow(row, 1, Bread.WholeGrain, 1),
            row => AssertRow(row, 2, Bread.White, 1));
        Assert.Equal(4, overview.TotalSandwiches);
        Assert.Equal(3 * 4.20m + 5.00m, overview.TotalAmount);
        Assert.Equal(new DateOnly(2026, 1, 14), overview.Date);
    }

    [Fact]
    public async Task Overview_WithoutOrders_IsEmpty()
    {
        IReadOnlyList<Order> none = [];
        orders.GetActiveForDateAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(none);

        var overview = await service.GetPreparationOverviewAsync();

        Assert.Empty(overview.Rows);
        Assert.Equal(0, overview.TotalSandwiches);
        Assert.Equal(0m, overview.TotalAmount);
    }

    // ---- helpers -----------------------------------------------------------------------------------------

    private void SetUtc(string iso) =>
        clock.SetUtcNow(DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal));

    private Order ExistingOrder()
    {
        var order = OrderOf("Ada Lovelace", Line(HamSandwich(), Bread.White));
        orders.GetActiveForCustomerAsync(CustomerId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(order);
        return order;
    }

    private static Order OrderOf(string customerName, OrderLine line)
    {
        var order = new Order { CustomerId = customerName, CustomerName = customerName };
        order.Lines.Add(line);
        return order;
    }

    private static OrderLine Line(Sandwich sandwich, Bread bread) => new()
    {
        SandwichId = sandwich.Id,
        Sandwich = sandwich,
        Bread = bread,
        Quantity = 1,
        UnitPrice = sandwich.Price
    };

    private static void AssertRow(PreparationRow row, int number, Bread bread, int quantity)
    {
        Assert.Equal(number, row.Sandwich.Number);
        Assert.Equal(bread, row.Bread);
        Assert.Equal(quantity, row.Quantity);
    }

    private static Sandwich HamSandwich(bool ingredientInStock = true) => new()
    {
        Id = 1,
        Number = 1,
        NameEn = "Ham sandwich",
        NameNl = "Broodje ham",
        Price = 4.20m,
        SandwichIngredients =
        {
            new SandwichIngredient
            {
                Ingredient = new Ingredient { NameEn = "Ham", NameNl = "Ham", IsAvailable = ingredientInStock }
            }
        }
    };
}
