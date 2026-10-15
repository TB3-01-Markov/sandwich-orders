using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SandwichOrders.Core;
using SandwichOrders.Core.Menu;
using SandwichOrders.Core.Orders;

namespace SandwichOrders.UnitTests;

public class SandwichAvailabilityTests
{
    [Fact]
    public void ASandwichOnTheMenuWithEveryIngredientInStock_IsAvailable()
    {
        Assert.True(Build().IsAvailable);
    }

    [Fact]
    public void ASandwichSwitchedOffByTheCanteen_IsNotAvailable()
    {
        Assert.False(Build(switchedOff: true).IsAvailable);
    }

    [Fact]
    public void ASandwichRemovedFromTheMenu_IsNotAvailable()
    {
        Assert.False(Build(active: false).IsAvailable);
    }

    [Fact]
    public void ASandwichWithAnIngredientOutOfStock_IsNotAvailable()
    {
        Assert.False(Build(ingredientInStock: false).IsAvailable);
    }

    private static Sandwich Build(bool active = true, bool switchedOff = false, bool ingredientInStock = true) => new()
    {
        IsActive = active,
        IsTemporarilyUnavailable = switchedOff,
        SandwichIngredients =
        {
            new SandwichIngredient { Ingredient = new Ingredient { IsAvailable = true } },
            new SandwichIngredient { Ingredient = new Ingredient { IsAvailable = ingredientInStock } }
        }
    };
}

public class LocalizationTests
{
    [Theory]
    [InlineData("nl", "Broodje ham")]
    [InlineData("nl-BE", "Broodje ham")]
    [InlineData("en", "Ham sandwich")]
    [InlineData("fr", "Ham sandwich")]
    public void ASandwichNameFollowsTheLanguage(string culture, string expected)
    {
        var sandwich = new Sandwich { NameEn = "Ham sandwich", NameNl = "Broodje ham" };

        Assert.Equal(expected, sandwich.NameFor(culture));
    }

    [Fact]
    public void WithoutADutchName_TheEnglishNameIsShownInstead()
    {
        var sandwich = new Sandwich { NameEn = "Ham sandwich", NameNl = "" };

        Assert.Equal("Ham sandwich", sandwich.NameFor("nl"));
    }
}

public class OrderTests
{
    [Fact]
    public void TheTotalIsThePriceTimesTheQuantityOfEveryLine()
    {
        var order = new Order();
        order.Lines.Add(new OrderLine { UnitPrice = 4.20m, Quantity = 2 });
        order.Lines.Add(new OrderLine { UnitPrice = 3.50m, Quantity = 1 });

        Assert.Equal(11.90m, order.Total);
    }

    [Fact]
    public void ACancelledOrderIsMarkedCancelled()
    {
        var order = new Order();

        order.Cancel();

        Assert.True(order.IsCancelled);
    }

    [Fact]
    public void ACollectedOrderCannotBeCancelled()
    {
        var order = new Order { Status = OrderStatus.Collected };

        var exception = Assert.Throws<OrderingException>(order.Cancel);
        Assert.Equal(OrderingError.InvalidStatusChange, exception.Error);
    }
}

public class OrderingRulesTests
{
    [Theory]
    [InlineData(9, 29, 59, true)]
    [InlineData(9, 30, 0, false)]
    [InlineData(0, 0, 0, true)]
    [InlineData(23, 59, 59, false)]
    public void OrderingIsOpenStrictlyBeforeTheDeadline(int hour, int minute, int second, bool expectedOpen)
    {
        Assert.Equal(expectedOpen, OrderingRules.IsOpen(new TimeOnly(hour, minute, second), new TimeOnly(9, 30)));
    }

    [Fact]
    public void TheDefaultDeadlineIsHalfPastNine()
    {
        Assert.Equal(new TimeOnly(9, 30), OrderingRules.DefaultDeadline);
    }
}

public class BrusselsTimeTests
{
    [Theory]
    [InlineData("2026-01-14T08:00:00Z", 9)]  // winter: UTC+1
    [InlineData("2026-07-14T07:00:00Z", 9)]  // summer: UTC+2
    public void ConvertsUtcToTheWallClockInBelgium(string utc, int expectedHour)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(expectedHour, BrusselsTime.Now(clock).Hour);
    }

    [Fact]
    public void FollowsTheSwitchToSummerTime()
    {
        // In 2026 the clocks go forward at 01:00Z on 29 March: 00:59Z is 01:59 CET, 01:00Z is already 03:00 CEST.
        var before = new FakeTimeProvider(DateTimeOffset.Parse("2026-03-29T00:59:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var after = new FakeTimeProvider(DateTimeOffset.Parse("2026-03-29T01:00:00Z", System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(new TimeOnly(1, 59), TimeOnly.FromDateTime(BrusselsTime.Now(before)));
        Assert.Equal(new TimeOnly(3, 0), TimeOnly.FromDateTime(BrusselsTime.Now(after)));
    }
}

public class MenuServiceTests
{
    private readonly IMenuRepository menu = Substitute.For<IMenuRepository>();
    private readonly MenuService service;

    public MenuServiceTests()
    {
        service = new MenuService(menu);
    }

    [Fact]
    public async Task SwitchingASandwichOff_IsSaved()
    {
        var sandwich = new Sandwich { Id = 7 };
        menu.GetAsync(7, Arg.Any<CancellationToken>()).Returns(sandwich);

        await service.SetTemporarilyUnavailableAsync(7, true);

        Assert.True(sandwich.IsTemporarilyUnavailable);
        _ = menu.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SwitchingAnUnknownSandwichOff_ReportsThatItDoesNotExist()
    {
        var exception = await Assert.ThrowsAsync<OrderingException>(
            () => service.SetTemporarilyUnavailableAsync(99, true));

        Assert.Equal(OrderingError.SandwichNotFound, exception.Error);
    }

    [Fact]
    public async Task MarkingAnIngredientOutOfStock_IsSaved()
    {
        var ingredient = new Ingredient { Id = 3, IsAvailable = true };
        menu.GetIngredientAsync(3, Arg.Any<CancellationToken>()).Returns(ingredient);

        await service.SetIngredientAvailableAsync(3, false);

        Assert.False(ingredient.IsAvailable);
        _ = menu.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkingAnUnknownIngredient_ReportsThatItDoesNotExist()
    {
        var exception = await Assert.ThrowsAsync<OrderingException>(
            () => service.SetIngredientAvailableAsync(99, false));

        Assert.Equal(OrderingError.IngredientNotFound, exception.Error);
    }

    [Fact]
    public void BrownBreadSurcharge_IsEightyCent_WhiteBreadHasNone()
    {
        Assert.Equal(0.80m, OrderingRules.SurchargeFor(Bread.WholeGrain));
        Assert.Equal(0m, OrderingRules.SurchargeFor(Bread.White));
    }
}
