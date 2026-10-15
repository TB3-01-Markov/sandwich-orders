namespace SandwichOrders.Core.Menu;

// Reasons why the canteen employee's change to the menu is refused. The web layer turns each into a readable message.
public enum MenuError
{
    SandwichNotFound,
    NumberTaken,
    NameRequired,
    DescriptionTooLong,
    InvalidPrice,
    UnknownIngredient,
    IngredientNameRequired,
    IngredientExists,
    IngredientNotFound,
    InvalidStock,
    InvalidPortions,
    StockChanged
}

public class MenuException(MenuError error) : Exception($"Menu change refused: {error}")
{
    public MenuError Error { get; } = error;
}
