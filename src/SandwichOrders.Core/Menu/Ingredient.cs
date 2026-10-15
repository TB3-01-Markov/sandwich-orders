using SandwichOrders.Core.Orders;

namespace SandwichOrders.Core.Menu;

public class Ingredient
{
    public int Id { get; set; }
    public string NameEn { get; set; } = "";
    public string NameNl { get; set; } = "";

    // Switched off by the canteen employee ("we ran out", US-13). Works also when the stock is not counted.
    public bool IsAvailable { get; set; } = true;

    // How many portions are in stock (1 portion = what goes on one sandwich, e.g. 1 slice of cheese).
    // null = the stock of this ingredient is not counted (never limits ordering). Same idea as RecipeMatcher,
    // but with quantities: the app can now calculate how many sandwiches can still be made.
    public int? StockPortions { get; private set; }

    // Changes every time the stock changes. EF Core uses it as a concurrency token, so two orders that both
    // take the last portion at the same moment cannot both succeed (the second one gets "try again").
    public int StockVersion { get; private set; }

    public ICollection<SandwichIngredient> SandwichIngredients { get; set; } = [];

    public string NameFor(string culture) => Localized.Pick(culture, NameEn, NameNl);

    // The canteen employee sets the counted stock (null = stop counting).
    public void SetStock(int? portions)
    {
        if (portions is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(portions), "Stock cannot be negative.");
        }

        StockPortions = portions;
        StockVersion++;
    }

    public bool HasStockFor(int portions) => StockPortions is null || StockPortions >= portions;

    internal void Take(int portions)
    {
        if (StockPortions is null)
        {
            return;
        }

        if (StockPortions < portions)
        {
            throw new OrderingException(OrderingError.SandwichUnavailable);
        }

        StockPortions -= portions;
        StockVersion++;
    }

    internal void Return(int portions)
    {
        if (StockPortions is null)
        {
            return;
        }

        StockPortions += portions;
        StockVersion++;
    }
}
