using SandwichOrders.Core.Orders;

namespace SandwichOrders.Core.Menu;

public class Sandwich
{
    public int Id { get; set; }
    public int Number { get; set; }
    public string NameEn { get; set; } = "";
    public string NameNl { get; set; } = "";
    public string DescriptionEn { get; set; } = "";
    public string DescriptionNl { get; set; } = "";
    public decimal Price { get; set; }
    public string? ImagePath { get; set; }

    // Prepared by the canteen from ingredients, or delivered ready-made by a supplier.
    public SandwichKind Kind { get; set; } = SandwichKind.Prepared;

    // Only for Supplied sandwiches: how many delivered pieces are left. null = not counted.
    public int? StockCount { get; private set; }

    // Concurrency token for StockCount (see Ingredient.StockVersion).
    public int StockVersion { get; private set; }

    // IsActive = on the menu at all; IsTemporarilyUnavailable = switched off by the canteen employee (US-08).
    public bool IsActive { get; set; } = true;
    public bool IsTemporarilyUnavailable { get; set; }

    public ICollection<SandwichIngredient> SandwichIngredients { get; set; } = [];

    // How many can still be ordered. null = no limit (nothing is counted).
    //   Supplied: the delivered pieces that are left.
    //   Prepared: the scarcest counted ingredient decides, e.g. 7 slices of cheese and 2 per sandwich -> 3.
    // The ingredients must be loaded (Include) for this to be correct.
    public int? AvailableCount => Kind == SandwichKind.Supplied
        ? StockCount
        : SandwichIngredients
            .Where(si => si.Ingredient.StockPortions is not null)
            .Select(si => (int?)Math.Max(0, si.Ingredient.StockPortions!.Value / Math.Max(1, si.Portions)))
            .Min();

    // Orderable only if it is on the menu, not switched off, every ingredient is switched on (prepared sandwiches)
    // and there is stock left. The ingredients must be loaded (Include) for this to be correct.
    public bool IsAvailable =>
        IsActive
        && !IsTemporarilyUnavailable
        && (Kind == SandwichKind.Supplied || SandwichIngredients.All(si => si.Ingredient.IsAvailable))
        && AvailableCount is null or > 0;

    public string NameFor(string culture) => Localized.Pick(culture, NameEn, NameNl);
    public string DescriptionFor(string culture) => Localized.Pick(culture, DescriptionEn, DescriptionNl);

    // The canteen employee counts the delivered pieces (null = stop counting).
    public void SetStock(int? count)
    {
        if (count is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Stock cannot be negative.");
        }

        StockCount = count;
        StockVersion++;
    }

    // Called when an order is placed: takes the pieces or the ingredient portions out of stock.
    // Checks everything first, so a refusal leaves the stock untouched.
    public void TakeFromStock(int quantity)
    {
        if (!IsAvailable || AvailableCount < quantity)
        {
            throw new OrderingException(OrderingError.SandwichUnavailable);
        }

        if (Kind == SandwichKind.Supplied)
        {
            if (StockCount is not null)
            {
                StockCount -= quantity;
                StockVersion++;
            }

            return;
        }

        foreach (var link in SandwichIngredients)
        {
            link.Ingredient.Take(link.Portions * quantity);
        }
    }

    // Called when the canteen employee cancels an order: everything goes back into stock.
    public void ReturnToStock(int quantity)
    {
        if (Kind == SandwichKind.Supplied)
        {
            if (StockCount is not null)
            {
                StockCount += quantity;
                StockVersion++;
            }

            return;
        }

        foreach (var link in SandwichIngredients)
        {
            link.Ingredient.Return(link.Portions * quantity);
        }
    }
}
