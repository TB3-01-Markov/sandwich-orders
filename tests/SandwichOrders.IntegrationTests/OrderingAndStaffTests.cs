using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SandwichOrders.Web.Models;

namespace SandwichOrders.IntegrationTests;

// US-04 to US-08 and US-15 through the real web application.
public class OrderingAndStaffTests : IntegrationTestBase
{
    // ---- US-04: ordering ---------------------------------------------------------------------------------

    [Fact]
    public async Task AVisitorWithoutAnAccount_CanOrderByGivingAName()
    {
        using var visitor = CreateClient();

        var response = await OrderHamAsync(visitor, "WholeGrain", "Jan Peeters");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.EndsWith("/Orders/Today", response.Headers.Location!.ToString());

        var html = await visitor.GetStringAsync("/Orders/Today");
        Assert.Contains("Jan Peeters", html);
        Assert.Contains("Ham sandwich", html);
        Assert.Contains("Whole-grain bread", html);
    }

    [Fact]
    public async Task AGuestOrderWithoutAName_IsRefused()
    {
        using var visitor = CreateClient();

        var response = await OrderHamAsync(visitor, "White", "   ");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.EndsWith("/", response.Headers.Location!.ToString());
        Assert.Contains("Please enter your name", await visitor.GetStringAsync("/"));
        Assert.Contains("You have no order for today", await visitor.GetStringAsync("/Orders/Today"));
    }

    [Fact]
    public async Task AGuestCanOnlyOrderOncePerDay_AndCanCancelHisOwnOrder()
    {
        using var visitor = CreateClient();
        await OrderHamAsync(visitor, "White", "Jan Peeters");

        await OrderHamAsync(visitor, "White", "Jan Peeters");
        Assert.Contains("already have an order", await visitor.GetStringAsync("/"));

        await PostFormAsync(visitor, "/Orders/Cancel", new Dictionary<string, string>());
        Assert.Contains("You have no order for today", await visitor.GetStringAsync("/Orders/Today"));
    }

    [Fact]
    public async Task AGuestDoesNotSeeTheOrderOfAnotherGuest()
    {
        using var first = CreateClient();
        using var second = CreateClient();
        await OrderHamAsync(first, "White", "Jan Peeters");

        var html = await second.GetStringAsync("/Orders/Today");

        Assert.DoesNotContain("Jan Peeters", html);
        Assert.Contains("You have no order for today", html);
    }

    [Fact]
    public async Task AGuestCookieCannotBeUsedToReachARegisteredCustomersOrder()
    {
        using var ada = await CreateSignedInCustomerAsync();
        await OrderHamAsync(ada);
        string adaId;
        using (var scope = Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            adaId = (await users.FindByEmailAsync("ada@example.test"))!.Id;
        }

        using var forger = Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/Orders/Today");
        request.Headers.Add("Cookie", $"guest_id={adaId}");
        var response = await forger.SendAsync(request);

        var html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Ada Lovelace", html);
        Assert.Contains("You have no order for today", html);
    }

    [Fact]
    public async Task AGuestOrderAppearsOnTheCanteensList()
    {
        using var visitor = CreateClient();
        await OrderHamAsync(visitor, "White", "Jan Peeters");
        using var staff = await CreateStaffClientAsync();

        var html = await staff.GetStringAsync("/Staff/Today");

        Assert.Contains("Jan Peeters", html);
    }

    [Fact]
    public async Task WhenOrderingIsClosed_TheMenuExplainsItAndOffersNoOrderButton()
    {
        SetBrusselsClockToUtc("2026-01-14T09:00:00Z"); // 10:00 in Brussels, after the default deadline
        using var visitor = CreateClient();

        var html = await visitor.GetStringAsync("/");

        Assert.Contains("Ordering is closed", html);
        Assert.DoesNotContain("name=\"sandwichId\"", html);
    }

    [Fact]
    public async Task ACustomerCanOrderASandwich_AndSeesItWithPriceAndBread()
    {
        using var customer = await CreateSignedInCustomerAsync();

        var response = await OrderHamAsync(customer, "WholeGrain");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.EndsWith("/Orders/Today", response.Headers.Location!.ToString());

        var html = await customer.GetStringAsync("/Orders/Today");
        Assert.Contains("Ada Lovelace", html);
        Assert.Contains("Ham sandwich", html);
        Assert.Contains("Whole-grain bread", html);
        Assert.Contains("4.20", html);
    }

    [Fact]
    public async Task OrderingAtTheDeadline_IsRefusedWithAMessage()
    {
        using var customer = await CreateSignedInCustomerAsync();
        SetBrusselsClockToUtc("2026-01-14T08:30:00Z"); // exactly 09:30 in Brussels

        var response = await OrderHamAsync(customer);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var menu = await customer.GetStringAsync("/");
        Assert.Contains("deadline has passed", menu);
        Assert.Contains("You have no order for today", await customer.GetStringAsync("/Orders/Today"));
    }

    [Fact]
    public async Task ASecondOrderOnTheSameDay_IsRefused()
    {
        using var customer = await CreateSignedInCustomerAsync();
        await OrderHamAsync(customer);

        await OrderHamAsync(customer);

        Assert.Contains("already have an order", await customer.GetStringAsync("/"));
    }

    // ---- US-05: cancelling -------------------------------------------------------------------------------

    [Fact]
    public async Task ACustomerCanCancelBeforeTheDeadline_AndThenOrderAgain()
    {
        using var customer = await CreateSignedInCustomerAsync();
        await OrderHamAsync(customer);

        var cancel = await PostFormAsync(customer, "/Orders/Cancel", new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.Redirect, cancel.StatusCode);
        Assert.Contains("You have no order for today", await customer.GetStringAsync("/Orders/Today"));

        var again = await OrderHamAsync(customer);
        Assert.EndsWith("/Orders/Today", again.Headers.Location!.ToString());
    }

    [Fact]
    public async Task CancellingAfterTheDeadline_IsRefused_AndTheOrderStays()
    {
        using var customer = await CreateSignedInCustomerAsync();
        await OrderHamAsync(customer);
        SetBrusselsClockToUtc("2026-01-14T08:31:00Z");

        await PostFormAsync(customer, "/Orders/Cancel", new Dictionary<string, string>());

        var html = await customer.GetStringAsync("/Orders/Today");
        Assert.Contains("Ham sandwich", html);
        Assert.Contains("deadline has passed", html);
    }

    // ---- US-06 and US-07: the canteen's view -------------------------------------------------------------

    [Fact]
    public async Task TheCanteenSeesWhoOrderedWhatAndHowManyToPrepare()
    {
        using var ada = await CreateSignedInCustomerAsync("ada@example.test", "Ada Lovelace");
        using var grace = await CreateSignedInCustomerAsync("grace@example.test", "Grace Hopper");
        await OrderHamAsync(ada, "White");
        await OrderHamAsync(grace, "WholeGrain");
        using var staff = await CreateStaffClientAsync();

        var html = await staff.GetStringAsync("/Staff/Today");

        Assert.Contains("Ada Lovelace", html);
        Assert.Contains("Grace Hopper", html);
        Assert.Contains("White bread", html);
        Assert.Contains("Whole-grain bread", html);
        Assert.Contains("8.40", html); // two sandwiches at 4.20
    }

    [Fact]
    public async Task ACancelledOrderIsNotOnTheCanteensList()
    {
        using var customer = await CreateSignedInCustomerAsync();
        await OrderHamAsync(customer);
        await PostFormAsync(customer, "/Orders/Cancel", new Dictionary<string, string>());
        using var staff = await CreateStaffClientAsync();

        var html = await staff.GetStringAsync("/Staff/Today");

        Assert.DoesNotContain("Ada Lovelace", html);
        Assert.Contains("No orders yet", html);
    }

    [Fact]
    public async Task OrdersFromAnotherDayAreNotOnTodaysList()
    {
        using var customer = await CreateSignedInCustomerAsync();
        await OrderHamAsync(customer);
        SetBrusselsClockToUtc("2026-01-15T08:00:00Z"); // the next morning
        using var staff = await CreateStaffClientAsync();

        var html = await staff.GetStringAsync("/Staff/Today");

        Assert.DoesNotContain("Ada Lovelace", html);
    }

    [Fact]
    public async Task ACustomerCannotOpenTheCanteenPages()
    {
        using var customer = await CreateSignedInCustomerAsync();

        var response = await customer.GetAsync("/Staff/Today");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/AccessDenied", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task AVisitorCannotOpenTheCanteenPages()
    {
        using var visitor = CreateClient();

        var response = await visitor.GetAsync("/Staff/Today");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location!.ToString());
    }

    // ---- US-08: switching a sandwich off -----------------------------------------------------------------

    [Fact]
    public async Task ASandwichSwitchedOffByTheCanteen_IsShownAsUnavailable_AndCannotBeOrdered()
    {
        using var staff = await CreateStaffClientAsync();
        var switchOff = await PostFormAsync(staff, "/Staff/SetSandwichUnavailable", new Dictionary<string, string>
        {
            ["id"] = HamSandwichId.ToString(),
            ["unavailable"] = "true"
        });
        Assert.Equal(HttpStatusCode.Redirect, switchOff.StatusCode);

        using var customer = await CreateSignedInCustomerAsync();
        Assert.Contains("Unavailable", await customer.GetStringAsync("/"));

        await OrderHamAsync(customer);
        Assert.Contains("not available", await customer.GetStringAsync("/"));
    }

    [Fact]
    public async Task SwitchingTheSandwichBackOn_MakesItOrderableAgain()
    {
        using var staff = await CreateStaffClientAsync();
        var form = new Dictionary<string, string> { ["id"] = HamSandwichId.ToString(), ["unavailable"] = "true" };
        await PostFormAsync(staff, "/Staff/SetSandwichUnavailable", form);
        await PostFormAsync(staff, "/Staff/SetSandwichUnavailable",
            new Dictionary<string, string> { ["id"] = HamSandwichId.ToString(), ["unavailable"] = "false" });
        using var customer = await CreateSignedInCustomerAsync();

        var response = await OrderHamAsync(customer);

        Assert.EndsWith("/Orders/Today", response.Headers.Location!.ToString());
    }

    // ---- US-15: the deadline -----------------------------------------------------------------------------

    [Fact]
    public async Task TheDeadlineSetByTheCanteen_IsShownToCustomers_AndUsedForOrdering()
    {
        using var staff = await CreateStaffClientAsync();
        var save = await PostFormAsync(staff, "/Staff/Deadline", new Dictionary<string, string> { ["Deadline"] = "10:00" });
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);

        using var customer = await CreateSignedInCustomerAsync();
        Assert.Contains("10:00", await customer.GetStringAsync("/"));

        SetBrusselsClockToUtc("2026-01-14T08:45:00Z"); // 09:45, after the old deadline
        var response = await OrderHamAsync(customer);

        Assert.EndsWith("/Orders/Today", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task AnInvalidDeadline_IsNotSaved()
    {
        using var staff = await CreateStaffClientAsync();

        var response = await PostFormAsync(staff, "/Staff/Deadline", new Dictionary<string, string> { ["Deadline"] = "" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var visitor = CreateClient();
        Assert.Contains("09:30", await visitor.GetStringAsync("/"));
    }

    [Fact]
    public async Task ACustomerCannotChangeTheDeadline()
    {
        using var customer = await CreateSignedInCustomerAsync();

        var response = await PostFormAsync(customer, "/Staff/Deadline", new Dictionary<string, string> { ["Deadline"] = "12:00" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/AccessDenied", response.Headers.Location!.ToString());
    }
}
