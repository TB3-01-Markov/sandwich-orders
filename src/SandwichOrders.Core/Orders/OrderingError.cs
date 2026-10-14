namespace SandwichOrders.Core.Orders;

// Every business rule that can refuse an action has one code here.
// The web layer turns a code into a translated message for the user.
public enum OrderingError
{
    SandwichNotFound,
    IngredientNotFound,
    SandwichUnavailable,
    DeadlinePassed,
    AlreadyOrdered,
    OrderNotFound
}

public class OrderingException(OrderingError error) : Exception($"Ordering rule violated: {error}")
{
    public OrderingError Error { get; } = error;
}
