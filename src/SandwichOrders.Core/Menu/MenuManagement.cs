namespace SandwichOrders.Core.Menu;

// What the employee typed in the sandwich form. KeepImage = true means "leave the photo as it is".
// Kind/StockCount/Portions were added later and have defaults, so older callers keep working.
// Portions: ingredient id -> portions per sandwich (missing = 1).
public record SandwichInput(
    int? Id,
    int Number,
    string NameEn,
    string NameNl,
    string? DescriptionEn,
    string? DescriptionNl,
    decimal Price,
    IReadOnlyCollection<int> IngredientIds,
    bool IsActive,
    bool KeepImage,
    string? ImagePath,
    SandwichKind Kind = SandwichKind.Prepared,
    int? StockCount = null,
    IReadOnlyDictionary<int, int>? Portions = null);

// OldImagePath is the photo that was replaced or removed, so the caller can delete the file.
public record SavedSandwich(Sandwich Sandwich, string? OldImagePath);

public static class MenuLimits
{
    public const int MaxNameLength = 100;
    public const int MaxDescriptionLength = 500;
    public const decimal MaxPrice = 100m;
    public const int MaxStock = 100_000;
}
