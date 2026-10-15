using System.Net;

namespace SandwichOrders.IntegrationTests;

// US-01, US-02 and US-03 through the real web application.
public class MenuAndAccountTests : IntegrationTestBase
{
    [Fact]
    public async Task TheMenuIsPublic_AndShowsNamePriceIngredientAndDeadline()
    {
        using var visitor = CreateClient();

        var html = await visitor.GetStringAsync("/");

        Assert.Contains("Ham sandwich", html);
        Assert.Contains("4.20", html);
        Assert.Contains("Ham", html);
        Assert.Contains("09:30", html);
    }

    [Fact]
    public async Task AVisitorWhoIsNotSignedIn_CanOrderByTypingAName()
    {
        using var visitor = CreateClient();

        var html = await visitor.GetStringAsync("/");

        Assert.Contains("No account needed", html);
        Assert.Contains("id=\"guestName\"", html);
        Assert.Contains("name=\"sandwichId\"", html);
    }

    [Fact]
    public async Task ChoosingDutch_ShowsDutchTextNamesAndPrices_AndTheChoiceIsRemembered()
    {
        using var visitor = CreateClient();

        var response = await PostFormAsync(visitor, "/Culture/Set", new Dictionary<string, string>
        {
            ["culture"] = "nl",
            ["returnUrl"] = "/"
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        // Razor HTML-encodes non-ASCII letters (ó becomes &#xF3;), so compare the decoded text.
        var html = WebUtility.HtmlDecode(await visitor.GetStringAsync("/"));
        Assert.Contains("Broodje ham", html);
        Assert.Contains("4,20", html);
        Assert.Contains("Bestel vóór 09:30", html);

        // A second visit in the same browser is still Dutch.
        Assert.Contains("Broodje ham", await visitor.GetStringAsync("/"));
    }

    [Fact]
    public async Task RegisteringCreatesAnAccountAndSignsTheCustomerIn()
    {
        using var customer = await CreateSignedInCustomerAsync();

        var html = await customer.GetStringAsync("/");

        Assert.Contains("Sign out", html);
        Assert.Contains("ada@example.test", html);
    }

    [Fact]
    public async Task RegisteringWithAnEmailThatIsAlreadyUsed_IsRefused()
    {
        using var first = await CreateSignedInCustomerAsync();
        using var second = CreateClient();

        var response = await RegisterAsync(second, "ada@example.test", "Someone Else");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("already exists", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RegisteringWithATooShortPassword_IsRefused()
    {
        using var visitor = CreateClient();

        var response = await RegisterAsync(visitor, "ada@example.test", "Ada Lovelace", "short1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("at least 8", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SigningInWithAWrongPassword_IsRefused()
    {
        using var visitor = CreateClient();

        var response = await LoginAsync(visitor, StaffEmail, "Not-The-Password-1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Invalid email or password", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SigningOutEndsTheSession_AndTheAccountsOrderIsNoLongerShown()
    {
        using var customer = await CreateSignedInCustomerAsync();
        await OrderHamAsync(customer);

        var response = await PostFormAsync(customer, "/Account/Logout", new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        // "My order" is open to guests now, so a signed-out visitor sees an empty page instead of a sign-in redirect.
        var page = await customer.GetStringAsync("/Orders/Today");
        Assert.DoesNotContain("Ada Lovelace", page);
        Assert.Contains("You have no order for today", page);
        Assert.DoesNotContain("Sign out", await customer.GetStringAsync("/"));
    }
}
