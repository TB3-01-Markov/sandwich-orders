namespace SandwichOrders.Core.Menu;

// US-09: show only the sandwiches that contain ALL the "include" ingredients and NONE of the "exclude" ones.
// The menu has a few dozen sandwiches, so this is done in memory on the already loaded list (easy to test, no SQL translation risk).
public static class MenuFilter
{
    public static IReadOnlyList<Sandwich> Apply(
        IEnumerable<Sandwich> sandwiches,
        IReadOnlyCollection<int> include,
        IReadOnlyCollection<int> exclude) =>
        sandwiches
            .Where(s =>
            {
                var ids = s.SandwichIngredients.Select(si => si.IngredientId).ToHashSet();
                return include.All(ids.Contains) && !exclude.Any(ids.Contains);
            })
            .ToList();
}
