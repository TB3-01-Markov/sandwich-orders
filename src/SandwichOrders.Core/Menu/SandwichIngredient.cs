namespace SandwichOrders.Core.Menu;

// Join entity for the many-to-many relation, same idea as RecipeIngredient in RecipeMatcher,
// plus the quantity: how many portions of this ingredient one sandwich uses.
public class SandwichIngredient
{
    public const int MaxPortions = 20;

    public int SandwichId { get; set; }
    public Sandwich Sandwich { get; set; } = null!;

    public int IngredientId { get; set; }
    public Ingredient Ingredient { get; set; } = null!;

    public int Portions { get; set; } = 1;
}
