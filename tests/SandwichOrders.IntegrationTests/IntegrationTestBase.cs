using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SandwichOrders.Core;
using SandwichOrders.Core.Menu;
using SandwichOrders.Web.Data;
using SandwichOrders.Web.Models;

namespace SandwichOrders.IntegrationTests;

// Every test gets its own application and its own database, so tests cannot influence each other.
public abstract class IntegrationTestBase : IDisposable
{
    protected const string StaffEmail = "staff@example.test";
    protected const string StaffPassword = "Staff-Test-1234";
    protected const string CustomerPassword = "Customer-Test-1234";

    protected TestWebApplicationFactory Factory { get; } = new();
    protected int HamSandwichId { get; private set; }

    protected IntegrationTestBase()
    {
        SeedAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        Factory.Dispose();
        GC.SuppressFinalize(this);
    }

    // A browser stand-in: keeps cookies (sign-in, language, TempData) and does not follow redirects automatically.
    protected HttpClient CreateClient() =>
        Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    protected async Task<HttpClient> CreateStaffClientAsync()
    {
        var client = CreateClient();
        var response = await LoginAsync(client, StaffEmail, StaffPassword);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    protected async Task<HttpClient> CreateSignedInCustomerAsync(string email = "ada@example.test", string name = "Ada Lovelace")
    {
        var client = CreateClient();
        var response = await RegisterAsync(client, email, name);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    protected static Task<HttpResponseMessage> RegisterAsync(
        HttpClient client, string email, string name = "Ada Lovelace", string password = CustomerPassword) =>
        PostFormAsync(client, "/Account/Register", new Dictionary<string, string>
        {
            ["DisplayName"] = name,
            ["Email"] = email,
            ["Password"] = password,
            ["ConfirmPassword"] = password
        });

    protected static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
        PostFormAsync(client, "/Account/Login", new Dictionary<string, string>
        {
            ["Email"] = email,
            ["Password"] = password,
            ["RememberMe"] = "false"
        });

    // Signed-in customers send no name (the account has one); a guest passes the name typed on the menu page.
    protected Task<HttpResponseMessage> OrderHamAsync(HttpClient client, string bread = "White", string? guestName = null)
    {
        var fields = new Dictionary<string, string>
        {
            ["sandwichId"] = HamSandwichId.ToString(),
            ["bread"] = bread
        };
        if (guestName is not null)
        {
            fields["customerName"] = guestName;
        }

        return PostFormAsync(client, "/Orders/Create", fields);
    }

    // Forms are protected against forgery, so a test first opens a page to get a token, like a browser does.
    protected static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient client, string url, Dictionary<string, string> fields, string tokenPage = "/")
    {
        fields["__RequestVerificationToken"] = await GetTokenAsync(client, tokenPage);
        return await client.PostAsync(url, new FormUrlEncodedContent(fields));
    }

    protected static async Task<string> GetTokenAsync(HttpClient client, string pageUrl)
    {
        var html = await client.GetStringAsync(pageUrl);
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, $"No anti-forgery token found on {pageUrl}");
        return match.Groups[1].Value;
    }

    protected void SetBrusselsClockToUtc(string iso) =>
        Factory.Time.SetUtcNow(DateTimeOffset.Parse(iso, System.Globalization.CultureInfo.InvariantCulture));

    private async Task SeedAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var sandwich = new Sandwich
        {
            Number = 1,
            NameEn = "Ham sandwich",
            NameNl = "Broodje ham",
            Price = 4.20m,
            SandwichIngredients =
            {
                new SandwichIngredient { Ingredient = new Ingredient { NameEn = "Ham", NameNl = "Ham" } }
            }
        };
        db.Sandwiches.Add(sandwich);
        await db.SaveChangesAsync();
        HamSandwichId = sandwich.Id;

        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var staff = new ApplicationUser { UserName = StaffEmail, Email = StaffEmail, DisplayName = "Canteen staff" };
        var created = await users.CreateAsync(staff, StaffPassword);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
        await users.AddToRoleAsync(staff, Roles.Staff);
    }
}
