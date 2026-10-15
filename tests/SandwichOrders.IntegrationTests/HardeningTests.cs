using System.Net;

namespace SandwichOrders.IntegrationTests;

// Security headers, caching, the "page not found" page and changing a password.
public class HardeningTests : IntegrationTestBase
{
    [Fact]
    public async Task Pages_HaveTheSecurityHeaders()
    {
        using var visitor = CreateClient();

        var response = await visitor.GetAsync("/");

        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("DENY", Header(response, "X-Frame-Options"));
        Assert.Equal("frame-ancestors 'none'", Header(response, "Content-Security-Policy"));
        Assert.Equal("strict-origin-when-cross-origin", Header(response, "Referrer-Policy"));
    }

    [Fact]
    public async Task StaticFiles_AreCached_AndAlsoGetNosniff()
    {
        using var visitor = CreateClient();

        var response = await visitor.GetAsync("/css/site.css");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("max-age=2592000", response.Headers.CacheControl!.ToString());
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
    }

    [Fact]
    public async Task AnUnknownAddress_Gives404WithAFriendlyPage()
    {
        using var visitor = CreateClient();

        var response = await visitor.GetAsync("/this/does/not/exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Page not found", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AnUnknownApiAddress_Stays404WithoutAnHtmlPage()
    {
        using var visitor = CreateClient();

        var response = await visitor.GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("Page not found", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ChangingThePassword_NeedsTheCurrentOne_AndTheNewOneWorks()
    {
        using var customer = await CreateSignedInCustomerAsync("ada@example.test");
        const string newPassword = "New-Password-5678";

        var wrong = await ChangePasswordAsync(customer, "Not-The-Password-1", newPassword);
        Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
        Assert.Contains("current password is not correct", await wrong.Content.ReadAsStringAsync());

        var changed = await ChangePasswordAsync(customer, CustomerPassword, newPassword);
        Assert.Equal(HttpStatusCode.Redirect, changed.StatusCode);

        using var fresh = CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(fresh, "ada@example.test", CustomerPassword)).StatusCode); // refused
        Assert.Equal(HttpStatusCode.Redirect, (await LoginAsync(fresh, "ada@example.test", newPassword)).StatusCode);
    }

    [Fact]
    public async Task ChangingThePassword_NeedsASignedInUser()
    {
        using var visitor = CreateClient();

        var response = await visitor.GetAsync("/Account/ChangePassword");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location!.ToString());
    }

    private static Task<HttpResponseMessage> ChangePasswordAsync(HttpClient client, string current, string next) =>
        PostFormAsync(client, "/Account/ChangePassword", new Dictionary<string, string>
        {
            ["CurrentPassword"] = current,
            ["NewPassword"] = next,
            ["ConfirmNewPassword"] = next
        }, "/Account/ChangePassword");

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(",", values)
        : response.Content.Headers.TryGetValues(name, out var contentValues) ? string.Join(",", contentValues)
        : null;
}
