using System.Net;

namespace SandwichOrders.IntegrationTests;

// The OpenAPI description lists the JSON API and nothing else.
public class OpenApiTests : IntegrationTestBase
{
    [Fact]
    public async Task TheApiDescription_ListsTheApiEndpoints_AndNoHtmlPages()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"/api/menu/status\"", json);
        Assert.Contains("\"/api/orders/today\"", json);
        Assert.Contains("\"/api/staff/orders/{id}/status\"", json);
        Assert.DoesNotContain("\"/Staff/", json);
        Assert.DoesNotContain("\"/Error", json);
    }
}
