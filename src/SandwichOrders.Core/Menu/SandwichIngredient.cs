namespace SandwichOrders.Core.Menu;

// Join entity for the many-to-many relation, same idea as RecipeIngredient in RecipeMatcher.
public class SandwichIngredient
{
    public int SandwichId { get; set; }
    public Sandwich Sandwich { get; set; } = null!;

    public int IngredientId { get; set; }
    public Ingredient Ingredient { get; set; } = null!;
}
