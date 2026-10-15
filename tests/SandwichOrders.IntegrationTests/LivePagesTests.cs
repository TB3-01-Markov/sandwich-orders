using Microsoft.Extensions.DependencyInjection;
namespace SandwichOrders.IntegrationTests;

// The pages give their JavaScript what it needs (the scripts themselves are checked in a browser by tools/ui-check).
public class LivePagesTests : IntegrationTestBase
{
    [Fact]
    public async Task TheMenuGivesTheCountdownTheServersSecondsLeft()
    {
        using var visitor = CreateClient();

        var html = await visitor.GetStringAsync("/");

        Assert.Contains("data-seconds-left=\"1800\"", html); // 09:00 -> 09:30
        Assert.Contains("data-is-open=\"true\"", html);
        Assert.Contains("live-menu.js", html);
    }

    [Fact]
    public async Task ASoldOutSandwichKeepsAHiddenOrderForm_SoItCanComeBackWithoutReloading()
    {
        using var visitor = CreateClient();
        await SetHamStockAsync(0);

        var html = await visitor.GetStringAsync("/");

        Assert.Contains("order-form d-none", html);
        Assert.Contains("Sold out", html);
    }

    [Fact]
    public async Task MyOrderIsWatchedUntilItIsCollected()
    {
        using var guest = CreateClient();
        await OrderHamAsync(guest, guestName: "Ada Lovelace");

        var html = await guest.GetStringAsync("/Orders/Today");

        Assert.Contains("id=\"live-order\"", html);
        Assert.Contains("data-status=\"Placed\"", html);
    }

    [Fact]
    public async Task OnlyTodaysStaffPageIsLive()
    {
        using var staff = await CreateStaffClientAsync();

        Assert.Contains("id=\"staff-live\"", await staff.GetStringAsync("/Staff/Today"));
        SetBrusselsClockToUtc("2026-01-15T08:00:00Z");
        Assert.DoesNotContain("id=\"staff-live\"", await staff.GetStringAsync("/Staff/Day?date=2026-01-14"));
    }

    private async Task SetHamStockAsync(int portions)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SandwichOrders.Web.Data.AppDbContext>();
        (await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(db.Ingredients, i => i.NameEn == "Ham")).SetStock(portions);
        await db.SaveChangesAsync();
    }
}
