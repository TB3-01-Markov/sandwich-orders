using SandwichOrders.Web.Data;
using Microsoft.EntityFrameworkCore;
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
    public async Task AGuestCanOnlyOrderOncePerDay()
    {
        using var visitor = CreateClient();
        await OrderHamAsync(visitor, "White", "Jan Peeters");

        await OrderHamAsync(visitor, "White", "Jan Peeters");
        Assert.Contains("already have an order", await visitor.GetStringAsync("/"));
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
        Assert.Contains("5.00", html); // 4.20 + 0.80 for brown bread
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

    // ---- Cancelling: only the canteen employee can cancel ------------------------------------------------------

    [Fact]
    public async Task ACustomerCannotCancel_ThereIsNoButtonAndNoEndpoint()
    {
        using var customer = await CreateSignedInCustomerAsync();
        await OrderHamAsync(customer);

        var html = await customer.GetStringAsync("/Orders/Today");
        Assert.DoesNotContain("/Orders/Cancel", html);
        Assert.Contains("ask the canteen staff", html);

        var cancel = await PostFormAsync(customer, "/Orders/Cancel", new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.NotFound, cancel.StatusCode);
        Assert.Contains("Ham sandwich", await customer.GetStringAsync("/Orders/Today"));
    }

    [Fact]
    public async Task ACustomerCannotUseTheStaffCancel()
    {
        using var customer = await CreateSignedInCustomerAsync();
        await OrderHamAsync(customer);

        var response = await PostFormAsync(customer, "/Staff/CancelOrder", new Dictionary<string, string> { ["id"] = "1" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/AccessDenied", response.Headers.Location!.ToString());
        Assert.Contains("Ham sandwich", await customer.GetStringAsync("/Orders/Today"));
    }

    [Fact]
    public async Task TheCanteenCanCancel_EvenAfterTheDeadline_AndTheCustomerCanOrderAgainTheNextDay()
    {
        using var customer = await CreateSignedInCustomerAsync();
        await OrderHamAsync(customer);
        SetBrusselsClockToUtc("2026-01-14T08:31:00Z"); // after the deadline
        using var staff = await CreateStaffClientAsync();

        var cancel = await PostFormAsync(staff, "/Staff/CancelOrder",
            new Dictionary<string, string> { ["id"] = await OnlyOrderIdAsync() }, "/Staff/Today");

        Assert.Equal(HttpStatusCode.Redirect, cancel.StatusCode);
        Assert.Contains("You have no order for today", await customer.GetStringAsync("/Orders/Today"));
        Assert.Contains("No orders yet", await staff.GetStringAsync("/Staff/Today"));
    }

    [Fact]
    public async Task WhenTheCanteenMarksAnOrderReady_TheCustomerSeesIt()
    {
        using var customer = await CreateSignedInCustomerAsync();
        await OrderHamAsync(customer);
        using var staff = await CreateStaffClientAsync();

        await PostFormAsync(staff, "/Staff/SetOrderStatus",
            new Dictionary<string, string> { ["id"] = await OnlyOrderIdAsync(), ["status"] = "Prepared" }, "/Staff/Today");

        Assert.Contains("Your sandwich is ready", await customer.GetStringAsync("/Orders/Today"));
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
        Assert.Contains("9.20", html); // 4.20 (white) + 5.00 (brown)
    }

    [Fact]
    public async Task TheCanteenSeesThePhoneNumberOfARegisteredCustomer()
    {
        using var customer = CreateClient();
        await PostFormAsync(customer, "/Account/Register", new Dictionary<string, string>
        {
            ["DisplayName"] = "Ada Lovelace",
            ["Email"] = "ada@example.test",
            ["PhoneNumber"] = "+32 470 12 34 56",
            ["Password"] = CustomerPassword,
            ["ConfirmPassword"] = CustomerPassword
        });
        await OrderHamAsync(customer);
        using var staff = await CreateStaffClientAsync();

        // Razor HTML-encodes "+", so only the digits are compared.
        Assert.Contains("470 12 34 56", await staff.GetStringAsync("/Staff/Today"));
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

    // ---- Stock and closing days -----------------------------------------------------------------------------

    [Fact]
    public async Task WhenTheLastPortionIsOrdered_TheSandwichIsSoldOut_AndCancellingPutsItBack()
    {
        using var staff = await CreateStaffClientAsync();
        await PostFormAsync(staff, "/Staff/SetIngredientStock",
            new Dictionary<string, string> { ["id"] = await HamIngredientIdAsync(), ["portions"] = "1" }, "/Staff/Menu");

        using var ada = await CreateSignedInCustomerAsync("ada@example.test", "Ada Lovelace");
        await OrderHamAsync(ada);

        using var grace = await CreateSignedInCustomerAsync("grace@example.test", "Grace Hopper");
        await OrderHamAsync(grace);
        Assert.Contains("This sandwich is not available", await grace.GetStringAsync("/"));
        Assert.Contains("Sold out", await grace.GetStringAsync("/"));

        await PostFormAsync(staff, "/Staff/CancelOrder",
            new Dictionary<string, string> { ["id"] = await OnlyOrderIdAsync() }, "/Staff/Today");

        var again = await OrderHamAsync(grace);
        Assert.EndsWith("/Orders/Today", again.Headers.Location!.ToString());
    }

    [Fact]
    public async Task DuringAClosingPeriod_OrderingIsBlocked_AndTheAnnouncementIsShown()
    {
        using var staff = await CreateStaffClientAsync();
        var saved = await PostFormAsync(staff, "/Staff/AnnouncementEdit", new Dictionary<string, string>
        {
            ["MessageEn"] = "Closed for holidays",
            ["ShowFrom"] = "2026-01-10",
            ["PeriodStart"] = "2026-01-14",
            ["PeriodEnd"] = "2026-01-20",
            ["BlocksOrdering"] = "true"
        }, "/Staff/AnnouncementEdit");
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);

        using var visitor = CreateClient();
        var menu = await visitor.GetStringAsync("/");
        Assert.Contains("Closed for holidays", menu);
        Assert.Contains("Closed today", menu);

        await OrderHamAsync(visitor, "White", "Jan Peeters");
        Assert.Contains("The canteen is closed today", await visitor.GetStringAsync("/"));
        Assert.Contains("You have no order for today", await visitor.GetStringAsync("/Orders/Today"));
    }

    [Fact]
    public async Task BeforeTheClosingPeriod_TheAnnouncementOnlyInforms()
    {
        using var staff = await CreateStaffClientAsync();
        await PostFormAsync(staff, "/Staff/AnnouncementEdit", new Dictionary<string, string>
        {
            ["MessageEn"] = "Closed next week",
            ["ShowFrom"] = "2026-01-10",
            ["PeriodStart"] = "2026-01-19",
            ["PeriodEnd"] = "2026-01-23",
            ["BlocksOrdering"] = "true"
        }, "/Staff/AnnouncementEdit");

        using var visitor = CreateClient();
        Assert.Contains("Closed next week", await visitor.GetStringAsync("/"));

        var order = await OrderHamAsync(visitor, "White", "Jan Peeters");
        Assert.EndsWith("/Orders/Today", order.Headers.Location!.ToString());
    }

    private async Task<string> OnlyOrderIdAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.Orders.SingleAsync(o => o.Status != SandwichOrders.Core.OrderStatus.Cancelled)).Id.ToString();
    }

    private async Task<string> HamIngredientIdAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.Ingredients.SingleAsync(i => i.NameEn == "Ham")).Id.ToString();
    }
}
