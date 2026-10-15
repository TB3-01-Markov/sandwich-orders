using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SandwichOrders.Core;
using SandwichOrders.Web.Data;

namespace SandwichOrders.IntegrationTests;

// The JSON API behind the live pages. The test clock starts on Wednesday 14 January 2026, 09:00 in Brussels;
// the default deadline is 09:30.
public class ApiTests : IntegrationTestBase
{
    // ---- GET /api/menu/status ----------------------------------------------------------------------------------

    [Fact]
    public async Task MenuStatus_BeforeTheDeadline_IsOpenWithTheSecondsLeft()
    {
        using var visitor = CreateClient();

        var json = await GetJsonAsync(visitor, "/api/menu/status");

        Assert.True(json.GetProperty("isOpen").GetBoolean());
        Assert.Equal("09:30", json.GetProperty("deadline").GetString());
        Assert.Equal(30 * 60, json.GetProperty("secondsLeft").GetInt32());
        var ham = Sandwich(json, HamSandwichId);
        Assert.True(ham.GetProperty("available").GetBoolean());
        Assert.Equal(JsonValueKind.Null, ham.GetProperty("left").ValueKind); // stock not counted
    }

    [Fact]
    public async Task MenuStatus_AfterTheDeadline_IsClosed()
    {
        using var visitor = CreateClient();
        SetBrusselsClockToUtc("2026-01-14T08:30:00Z"); // 09:30

        var json = await GetJsonAsync(visitor, "/api/menu/status");

        Assert.False(json.GetProperty("isOpen").GetBoolean());
        Assert.Equal(0, json.GetProperty("secondsLeft").GetInt32());
    }

    [Fact]
    public async Task MenuStatus_ShowsHowManyAreLeft_AndSoldOut()
    {
        await SetHamStockAsync(1);
        using var visitor = CreateClient();
        Assert.Equal(1, Sandwich(await GetJsonAsync(visitor, "/api/menu/status"), HamSandwichId).GetProperty("left").GetInt32());

        await OrderHamAsync(visitor, guestName: "Ada Lovelace");

        var ham = Sandwich(await GetJsonAsync(visitor, "/api/menu/status"), HamSandwichId);
        Assert.Equal(0, ham.GetProperty("left").GetInt32());
        Assert.False(ham.GetProperty("available").GetBoolean());
    }

    // ---- GET /api/orders/today ---------------------------------------------------------------------------------

    [Fact]
    public async Task MyOrder_WithoutAnOrder_IsNoContent()
    {
        using var visitor = CreateClient();

        var response = await visitor.GetAsync("/api/orders/today");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task MyOrder_ShowsTheStatus_AndChangesWhenTheCanteenPressesReady()
    {
        using var guest = CreateClient();
        await OrderHamAsync(guest, "WholeGrain", guestName: "Ada Lovelace");
        var before = await GetJsonAsync(guest, "/api/orders/today");
        Assert.Equal("Placed", before.GetProperty("status").GetString());
        Assert.Equal("€5.00", before.GetProperty("totalText").GetString());

        using var staff = await CreateStaffClientAsync();
        var ready = await PostApiAsync(staff, $"/api/staff/orders/{await OnlyOrderIdAsync()}/status", new { status = "Prepared" });
        Assert.Equal(HttpStatusCode.NoContent, ready.StatusCode);

        var after = await GetJsonAsync(guest, "/api/orders/today");
        Assert.Equal("Prepared", after.GetProperty("status").GetString());
    }

    // ---- /api/staff/orders -------------------------------------------------------------------------------------

    [Fact]
    public async Task StaffApi_NotSignedIn_Is401_AndForACustomer_Is403()
    {
        using var visitor = CreateClient();
        using var customer = await CreateSignedInCustomerAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync("/api/staff/orders/today")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/staff/orders/today")).StatusCode);
    }

    [Fact]
    public async Task StaffApi_ListsTodaysOrders()
    {
        using var guest = CreateClient();
        await OrderHamAsync(guest, guestName: "Ada Lovelace");
        using var staff = await CreateStaffClientAsync();

        var json = await GetJsonAsync(staff, "/api/staff/orders/today");

        Assert.Equal(1, json.GetProperty("totalSandwiches").GetInt32());
        var order = json.GetProperty("orders")[0];
        Assert.Equal("Ada Lovelace", order.GetProperty("customerName").GetString());
        Assert.Equal("Placed", order.GetProperty("status").GetString());
        Assert.Equal("Ham sandwich", order.GetProperty("lines")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task StaffApi_APostWithoutTheAntiForgeryToken_IsRefused()
    {
        using var guest = CreateClient();
        await OrderHamAsync(guest, guestName: "Ada Lovelace");
        using var staff = await CreateStaffClientAsync();

        var response = await PostApiAsync(staff, $"/api/staff/orders/{await OnlyOrderIdAsync()}/status",
            new { status = "Prepared" }, withToken: false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Placed", (await GetJsonAsync(guest, "/api/orders/today")).GetProperty("status").GetString());
    }

    [Fact]
    public async Task StaffApi_AnImpossibleStatusChange_Is409WithAMessage()
    {
        using var guest = CreateClient();
        await OrderHamAsync(guest, guestName: "Ada Lovelace");
        using var staff = await CreateStaffClientAsync();

        var response = await PostApiAsync(staff, $"/api/staff/orders/{await OnlyOrderIdAsync()}/status", new { status = "Placed" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("not possible", error.GetProperty("error").GetString());
    }

    [Fact]
    public async Task StaffApi_CancelPutsTheStockBack_AndAnUnknownOrderIs404()
    {
        await SetHamStockAsync(1);
        using var guest = CreateClient();
        await OrderHamAsync(guest, guestName: "Ada Lovelace");
        using var staff = await CreateStaffClientAsync();

        var cancel = await PostApiAsync(staff, $"/api/staff/orders/{await OnlyOrderIdAsync()}/cancel", null);
        var unknown = await PostApiAsync(staff, "/api/staff/orders/999/cancel", null);

        Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await guest.GetAsync("/api/orders/today")).StatusCode);
        Assert.Equal(1, Sandwich(await GetJsonAsync(guest, "/api/menu/status"), HamSandwichId).GetProperty("left").GetInt32());
    }

    // ---- helpers ------------------------------------------------------------------------------------------------

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static JsonElement Sandwich(JsonElement status, int id) =>
        status.GetProperty("sandwiches").EnumerateArray().Single(s => s.GetProperty("id").GetInt32() == id);

    // Like the JavaScript in the pages: the anti-forgery token of a page goes into the RequestVerificationToken header.
    private static async Task<HttpResponseMessage> PostApiAsync(HttpClient client, string url, object? body, bool withToken = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body ?? new { }) };
        if (withToken)
        {
            request.Headers.Add("RequestVerificationToken", await GetTokenAsync(client, "/Staff/Today"));
        }

        return await client.SendAsync(request);
    }

    private async Task<string> OnlyOrderIdAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.Orders.SingleAsync(o => o.Status != OrderStatus.Cancelled)).Id.ToString();
    }

    private async Task SetHamStockAsync(int portions)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Ingredients.SingleAsync(i => i.NameEn == "Ham")).SetStock(portions);
        await db.SaveChangesAsync();
    }
}
