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

    // IsActive = on the menu at all; IsTemporarilyUnavailable = switched off by the canteen employee (US-08).
    public bool IsActive { get; set; } = true;
    public bool IsTemporarilyUnavailable { get; set; }

    public ICollection<SandwichIngredient> SandwichIngredients { get; set; } = new List<SandwichIngredient>();

    // Orderable only if it is on the menu, not switched off and every ingredient is available.
    // The ingredients must be loaded (Include) for this to be correct.
    public bool IsAvailable =>
        IsActive
        && !IsTemporarilyUnavailable
        && SandwichIngredients.All(si => si.Ingredient.IsAvailable);

    public string NameFor(string culture) => Localized.Pick(culture, NameEn, NameNl);
    public string DescriptionFor(string culture) => Localized.Pick(culture, DescriptionEn, DescriptionNl);
}
