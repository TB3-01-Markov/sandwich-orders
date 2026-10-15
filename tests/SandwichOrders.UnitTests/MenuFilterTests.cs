using SandwichOrders.Core.Menu;

namespace SandwichOrders.UnitTests;

// US-09: the ingredient filter.
public class MenuFilterTests
{
    private const int Ham = 1, Cheese = 2, Tuna = 3, Egg = 4;

    private static Sandwich Make(int number, params int[] ingredientIds) => new()
    {
        Id = number,
        Number = number,
        NameEn = $"Sandwich {number}",
        SandwichIngredients = ingredientIds.Select(id => new SandwichIngredient { IngredientId = id }).ToList()
    };

    // 1 = ham+cheese, 2 = tuna+egg, 3 = cheese only, 4 = ham+cheese+egg
    private readonly List<Sandwich> menu =
    [
        Make(1, Ham, Cheese),
        Make(2, Tuna, Egg),
        Make(3, Cheese),
        Make(4, Ham, Cheese, Egg)
    ];

    private static int[] Numbers(IEnumerable<Sandwich> sandwiches) => sandwiches.Select(s => s.Number).ToArray();

    [Fact]
    public void WithoutAnyFilter_EverySandwichIsShown() =>
        Assert.Equal(new[] { 1, 2, 3, 4 }, Numbers(MenuFilter.Apply(menu, [], [])));

    [Fact]
    public void Including_AnIngredient_ShowsOnlySandwichesThatContainIt() =>
        Assert.Equal(new[] { 1, 3, 4 }, Numbers(MenuFilter.Apply(menu, [Cheese], [])));

    [Fact]
    public void Including_TwoIngredients_RequiresBoth() =>
        Assert.Equal(new[] { 4 }, Numbers(MenuFilter.Apply(menu, [Cheese, Egg], [])));

    [Fact]
    public void Excluding_AnIngredient_HidesSandwichesThatContainIt() =>
        Assert.Equal(new[] { 2, 3 }, Numbers(MenuFilter.Apply(menu, [], [Ham])));

    [Fact]
    public void IncludeAndExclude_BothApply() =>
        Assert.Equal(new[] { 1 }, Numbers(MenuFilter.Apply(menu, [Cheese], [Egg])));

    [Fact]
    public void AnIngredientThatIsBothIncludedAndExcluded_MatchesNothing() =>
        Assert.Empty(MenuFilter.Apply(menu, [Ham], [Ham]));

    [Fact]
    public void NoMatch_GivesAnEmptyList() =>
        Assert.Empty(MenuFilter.Apply(menu, [Tuna, Ham], []));

    [Fact]
    public void AnUnknownIncludedIngredient_MatchesNothing_AnUnknownExcludedOneChangesNothing()
    {
        Assert.Empty(MenuFilter.Apply(menu, [999], []));
        Assert.Equal(4, MenuFilter.Apply(menu, [], [999]).Count);
    }
}
