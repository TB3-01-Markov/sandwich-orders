using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace SandwichOrders.IntegrationTests;

// Account lockout, rate limiting and the health check endpoint.
public class SecurityTests : IntegrationTestBase
{
    [Fact]
    public async Task Health_AnswersHealthy_WhenTheDatabaseIsReachable()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task FiveWrongPasswords_LockTheAccount_EvenForTheRightPassword()
    {
        using var client = CreateClient();
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var wrong = await LoginAsync(client, StaffEmail, "Wrong-Password-1");
            Assert.Equal(HttpStatusCode.OK, wrong.StatusCode); // the login page again, not signed in
        }

        var right = await LoginAsync(client, StaffEmail, StaffPassword);

        Assert.Equal(HttpStatusCode.OK, right.StatusCode);
        Assert.Contains("locked for 5 minutes", await right.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task FourWrongPasswords_DoNotLockTheAccount()
    {
        using var client = CreateClient();
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            await LoginAsync(client, StaffEmail, "Wrong-Password-1");
        }

        var right = await LoginAsync(client, StaffEmail, StaffPassword);

        Assert.Equal(HttpStatusCode.Redirect, right.StatusCode);
    }

    [Fact]
    public async Task TooManyOrderRequestsFromOneAddress_AreRefusedWith429()
    {
        using var limited = Factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RateLimiting:PermitsPerMinute"] = "3"
                })));
        using var client = limited.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        // The first is placed, the next two are refused as "already ordered" (still answered normally).
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var allowed = await OrderHamAsync(client, guestName: "Ada Lovelace");
            Assert.Equal(HttpStatusCode.Redirect, allowed.StatusCode);
        }

        var refused = await OrderHamAsync(client, guestName: "Ada Lovelace");

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Contains("Too many requests", await refused.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ReadingPages_IsNotLimited()
    {
        using var limited = Factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RateLimiting:PermitsPerMinute"] = "1"
                })));
        using var client = limited.CreateClient();

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
        }
    }
}
