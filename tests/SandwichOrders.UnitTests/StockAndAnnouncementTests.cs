using SandwichOrders.Core;
using SandwichOrders.Core.Announcements;
using SandwichOrders.Core.Menu;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace SandwichOrders.UnitTests;

// How many sandwiches can still be made, and announcements that inform first and block during their period.
public class StockAndAnnouncementTests
{
    [Fact]
    public void ThePreparedCount_IsDecidedByTheScarcestCountedIngredient()
    {
        var cheese = Counted("Cheese", 7);
        var ham = Counted("Ham", 10);
        var lettuce = new Ingredient { NameEn = "Lettuce" }; // not counted
        var sandwich = Prepared((cheese, 2), (ham, 1), (lettuce, 1));

        Assert.Equal(3, sandwich.AvailableCount); // 7 / 2 = 3, 10 / 1 = 10
        Assert.True(sandwich.IsAvailable);
    }

    [Fact]
    public void ASandwichWithoutCountedIngredients_HasNoLimit()
    {
        var sandwich = Prepared((new Ingredient { NameEn = "Lettuce" }, 1));

        Assert.Null(sandwich.AvailableCount);
        Assert.True(sandwich.IsAvailable);
    }

    [Fact]
    public void WhenTheLastPortionIsGone_TheSandwichIsUnavailable()
    {
        var sandwich = Prepared((Counted("Cheese", 1), 2));

        Assert.Equal(0, sandwich.AvailableCount);
        Assert.False(sandwich.IsAvailable);
    }

    [Fact]
    public void ReturningStock_UndoesTaking()
    {
        var cheese = Counted("Cheese", 5);
        var sandwich = Prepared((cheese, 2));

        sandwich.TakeFromStock(2);
        Assert.Equal(1, cheese.StockPortions);

        sandwich.ReturnToStock(2);
        Assert.Equal(5, cheese.StockPortions);
    }

    [Fact]
    public void ASwitchedOffIngredient_DoesNotBlockASuppliedSandwich()
    {
        var wrap = new Sandwich { Kind = SandwichKind.Supplied };
        wrap.SandwichIngredients.Add(new SandwichIngredient { Ingredient = new Ingredient { IsAvailable = false } });
        wrap.SetStock(2);

        Assert.True(wrap.IsAvailable);
        Assert.Equal(2, wrap.AvailableCount);
    }

    [Fact]
    public void ChangingStock_ChangesTheConcurrencyVersion()
    {
        var cheese = Counted("Cheese", 5);
        var before = cheese.StockVersion;

        Prepared((cheese, 1)).TakeFromStock(1);

        Assert.NotEqual(before, cheese.StockVersion);
    }

    // ---- Announcements: shown on 1 July, closed from 5 to 15 August ------------------------------------------

    private static Announcement Holidays() => new()
    {
        MessageNl = "Van 5 tot 15 augustus is de kantine gesloten",
        MessageEn = "",
        PeriodMessageEn = "Closed until 16 August",
        ShowFrom = new DateOnly(2026, 7, 1),
        PeriodStart = new DateOnly(2026, 8, 5),
        PeriodEnd = new DateOnly(2026, 8, 15),
        BlocksOrdering = true
    };

    [Theory]
    [InlineData("2026-06-30", AnnouncementPhase.Hidden, false)]
    [InlineData("2026-07-01", AnnouncementPhase.Upcoming, false)]
    [InlineData("2026-08-04", AnnouncementPhase.Upcoming, false)]
    [InlineData("2026-08-05", AnnouncementPhase.Active, true)]
    [InlineData("2026-08-15", AnnouncementPhase.Active, true)]
    [InlineData("2026-08-16", AnnouncementPhase.Expired, false)]
    public void AnAnnouncement_InformsFirst_ThenBlocks_ThenDisappears(string day, AnnouncementPhase phase, bool blocks)
    {
        var date = DateOnly.Parse(day, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(phase, Holidays().PhaseOn(date));
        Assert.Equal(blocks, Holidays().BlocksOrderingOn(date));
    }

    [Fact]
    public void ANonBlockingAnnouncement_NeverBlocks()
    {
        var notice = Holidays();
        notice.BlocksOrdering = false;

        Assert.False(notice.BlocksOrderingOn(new DateOnly(2026, 8, 10)));
    }

    [Fact]
    public void ThePeriodText_IsUsedDuringThePeriod_OtherwiseTheNormalText()
    {
        var notice = Holidays();

        Assert.Equal("Van 5 tot 15 augustus is de kantine gesloten", notice.MessageFor("en", new DateOnly(2026, 7, 20)));
        Assert.Equal("Closed until 16 August", notice.MessageFor("en", new DateOnly(2026, 8, 10)));

        notice.PeriodMessageEn = null;
        Assert.Equal("Van 5 tot 15 augustus is de kantine gesloten", notice.MessageFor("nl", new DateOnly(2026, 8, 10)));
    }

    [Fact]
    public async Task Save_WithoutText_IsRefused()
    {
        var service = new AnnouncementService(Substitute.For<IAnnouncementRepository>(), new FakeTimeProvider());

        var exception = await Assert.ThrowsAsync<AnnouncementException>(() => service.SaveAsync(Input(" ", null)));

        Assert.Equal(AnnouncementError.MessageRequired, exception.Error);
    }

    [Fact]
    public async Task Save_WithDatesOutOfOrder_IsRefused()
    {
        var service = new AnnouncementService(Substitute.For<IAnnouncementRepository>(), new FakeTimeProvider());

        var exception = await Assert.ThrowsAsync<AnnouncementException>(() => service.SaveAsync(
            Input("Closed", null) with { PeriodStart = new DateOnly(2026, 8, 20) }));

        Assert.Equal(AnnouncementError.InvalidDates, exception.Error);
    }

    [Fact]
    public async Task Save_StoresTheAnnouncement()
    {
        var repository = Substitute.For<IAnnouncementRepository>();
        var service = new AnnouncementService(repository, new FakeTimeProvider());

        var saved = await service.SaveAsync(Input("  Closed  ", "Gesloten"));

        Assert.Equal("Closed", saved.MessageEn);
        repository.Received(1).Add(saved);
        await repository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private static AnnouncementInput Input(string? en, string? nl) => new(
        null, en, nl, null, null,
        new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 5), new DateOnly(2026, 8, 15), true);

    private static Ingredient Counted(string name, int portions)
    {
        var ingredient = new Ingredient { NameEn = name, NameNl = name };
        ingredient.SetStock(portions);
        return ingredient;
    }

    private static Sandwich Prepared(params (Ingredient Ingredient, int Portions)[] parts)
    {
        var sandwich = new Sandwich { NameEn = "Test", NameNl = "Test", Price = 4m };
        foreach (var (ingredient, portions) in parts)
        {
            sandwich.SandwichIngredients.Add(new SandwichIngredient { Ingredient = ingredient, Portions = portions });
        }

        return sandwich;
    }
}
