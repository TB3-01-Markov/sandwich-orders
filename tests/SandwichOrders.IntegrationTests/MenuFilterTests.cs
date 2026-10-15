using Microsoft.Extensions.DependencyInjection;
using SandwichOrders.Core.Menu;
using SandwichOrders.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace SandwichOrders.IntegrationTests;

// US-09 through the real menu page. The base class seeds "Ham sandwich" (Ham); this adds "Cheese sandwich" (Cheese).
public class MenuFilterTests : IntegrationTestBase
{
    private int hamId;
    private int cheeseId;

    public MenuFilterTests()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cheese = new Ingredient { NameEn = "Cheese", NameNl = "Kaas" };
        db.Sandwiches.Add(new Sandwich
        {
            Number = 2,
            NameEn = "Cheese sandwich",
            NameNl = "Broodje kaas",
            Price = 3.50m,
            SandwichIngredients = { new SandwichIngredient { Ingredient = cheese } }
        });
        db.SaveChanges();
        hamId = db.Ingredients.Single(i => i.NameEn == "Ham").Id;
        cheeseId = db.Ingredients.Single(i => i.NameEn == "Cheese").Id;
    }

    private async Task<string> MenuAsync(string query)
    {
        using var visitor = CreateClient();
        return await visitor.GetStringAsync("/" + query);
    }

    [Fact]
    public async Task WithoutAFilter_BothSandwichesAreShown()
    {
        var html = await MenuAsync("");

        Assert.Contains("Ham sandwich", html);
        Assert.Contains("Cheese sandwich", html);
        Assert.DoesNotContain("Showing", html);
    }

    [Fact]
    public async Task IncludingAnIngredient_ShowsOnlySandwichesWithIt()
    {
        var html = await MenuAsync($"?include={cheeseId}");

        Assert.Contains("Cheese sandwich", html);
        Assert.DoesNotContain("Ham sandwich", html);
        Assert.Contains("Showing 1 of 2 sandwiches", html);
    }

    [Fact]
    public async Task ExcludingAnIngredient_HidesSandwichesWithIt()
    {
        var html = await MenuAsync($"?exclude={cheeseId}");

        Assert.Contains("Ham sandwich", html);
        Assert.DoesNotContain("Cheese sandwich", html);
    }

    [Fact]
    public async Task WhenNothingMatches_AMessageAndAClearLinkAreShown()
    {
        var html = await MenuAsync($"?include={hamId}&include={cheeseId}");

        Assert.Contains("No sandwich matches this filter", html);
        Assert.Contains("Clear filter", html);
        Assert.DoesNotContain("Ham sandwich", html);
        Assert.DoesNotContain("Cheese sandwich", html);
    }

    [Fact]
    public async Task TheChosenIngredientsStayTickedInTheFilterForm()
    {
        var html = await MenuAsync($"?include={cheeseId}");

        Assert.Matches($"id=\"include-{cheeseId}\"[^>]*checked", html);
    }
}
