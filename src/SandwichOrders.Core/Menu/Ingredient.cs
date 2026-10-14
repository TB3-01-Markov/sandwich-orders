namespace SandwichOrders.Core.Menu;

public class Ingredient
{
    public int Id { get; set; }
    public string NameEn { get; set; } = "";
    public string NameNl { get; set; } = "";
    public bool IsAvailable { get; set; } = true;
    public ICollection<SandwichIngredient> SandwichIngredients { get; set; } = new List<SandwichIngredient>();

    public string NameFor(string culture) => Localized.Pick(culture, NameEn, NameNl);
}
