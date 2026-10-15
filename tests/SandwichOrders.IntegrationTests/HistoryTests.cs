using System.Net;

namespace SandwichOrders.IntegrationTests;

// "My previous orders" for customers and guests, and the canteen's history per month and per day.
// The test clock starts on Wednesday 14 January 2026, 09:00 in Brussels.
public class HistoryTests : IntegrationTestBase
{
    private const string NextMorning = "2026-01-15T08:00:00Z"; // Thursday 09:00 in Brussels

    [Fact]
    public async Task ACustomerSeesEarlierOrders_ButNotTodaysOrder()
    {
        using var customer = await CreateSignedInCustomerAsync();
        await OrderHamAsync(customer, "WholeGrain");
        SetBrusselsClockToUtc(NextMorning);
        await OrderHamAsync(customer, "White");

        var html = WebUtility.HtmlDecode(await customer.GetStringAsync("/Orders/History"));

        Assert.Contains("14/01/2026", html);
        Assert.Contains("Ham sandwich", html);
        Assert.Contains("Whole-grain bread", html);
        Assert.Contains("5.00", html); // 4.20 + 0.80 brown bread
        Assert.DoesNotContain("15/01/2026", html); // today's order is on "My order", not in the history
        Assert.DoesNotContain("kept in this browser only", html); // no guest hint for an account
    }

    [Fact]
    public async Task AGuestSeesOnlyTheOrdersOfThisBrowser()
    {
        using var ada = CreateClient();
        using var grace = CreateClient();
        await OrderHamAsync(ada, "White", guestName: "Ada Lovelace");
        await OrderHamAsync(grace, "WholeGrain", guestName: "Grace Hopper");
        SetBrusselsClockToUtc(NextMorning);

        var html = WebUtility.HtmlDecode(await ada.GetStringAsync("/Orders/History"));

        Assert.Contains("White bread", html);
        Assert.DoesNotContain("Whole-grain bread", html);
        Assert.Contains("kept in this browser only", html);
    }

    [Fact]
    public async Task AVisitorWithoutOrders_SeesAnEmptyHistory()
    {
        using var visitor = CreateClient();

        var response = await visitor.GetAsync("/Orders/History");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("You have no previous orders", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TheCanteenSeesEachDayOfTheMonthWithItsTotal()
    {
        using var ada = CreateClient();
        using var grace = CreateClient();
        await OrderHamAsync(ada, "White", guestName: "Ada Lovelace");        // 4.20
        await OrderHamAsync(grace, "WholeGrain", guestName: "Grace Hopper"); // 5.00
        SetBrusselsClockToUtc(NextMorning);
        using var staff = await CreateStaffClientAsync();

        var html = WebUtility.HtmlDecode(await staff.GetStringAsync("/Staff/History?year=2026&month=1"));

        Assert.Contains("Wed 14/01", html);
        Assert.Contains("9.20", html);
        Assert.Contains("January 2026", html);
    }

    [Fact]
    public async Task TheCanteenCanOpenAnEarlierDay_ReadOnly()
    {
        using var ada = CreateClient();
        await OrderHamAsync(ada, "White", guestName: "Ada Lovelace");
        SetBrusselsClockToUtc(NextMorning);
        using var staff = await CreateStaffClientAsync();

        var html = WebUtility.HtmlDecode(await staff.GetStringAsync("/Staff/Day?date=2026-01-14"));

        Assert.Contains("Orders on 14/01/2026", html);
        Assert.Contains("Ada Lovelace", html);
        Assert.DoesNotContain("/Staff/SetOrderStatus", html); // no status buttons for an earlier day
        Assert.DoesNotContain("/Staff/CancelOrder", html);
    }

    [Fact]
    public async Task OpeningTodayFromTheHistory_GoesToTodaysOrders()
    {
        using var staff = await CreateStaffClientAsync();

        var response = await staff.GetAsync("/Staff/Day?date=2026-01-14");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.EndsWith("/Staff/Today", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task AnImpossibleMonth_ShowsTheCurrentMonth()
    {
        using var staff = await CreateStaffClientAsync();

        var response = await staff.GetAsync("/Staff/History?year=1&month=13");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("January 2026", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ACustomerCannotSeeTheCanteensHistory()
    {
        using var customer = await CreateSignedInCustomerAsync();

        var response = await customer.GetAsync("/Staff/History");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/AccessDenied", response.Headers.Location!.ToString());
    }
}
