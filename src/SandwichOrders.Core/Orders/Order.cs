using SandwichOrders.Core.Menu;

namespace SandwichOrders.Core.Orders;

public class Order
{
    public int Id { get; set; }

    // Id of the signed-in user (Identity). A plain string on purpose: the core knows nothing about Identity.
    public string CustomerId { get; set; } = "";

    // The name the canteen reads out, copied at order time (replaces the name written on paper).
    public string CustomerName { get; set; } = "";

    // Optional phone number of a registered customer, copied at order time so the canteen can call
    // ("did you forget your sandwich?"). Guests have none.
    public string? CustomerPhone { get; set; }

    // The Belgian calendar day the order is for.
    public DateOnly OrderDate { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Placed;
    public ICollection<OrderLine> Lines { get; set; } = [];

    public decimal Total => Lines.Sum(line => line.UnitPrice * line.Quantity);
    public bool IsCancelled => Status == OrderStatus.Cancelled;

    // Cancels the order and puts the sandwiches back into stock. The lines' sandwiches (and for prepared
    // sandwiches their ingredients) must be loaded and tracked.
    public void Cancel()
    {
        if (Status is OrderStatus.Collected or OrderStatus.Cancelled)
        {
            throw new OrderingException(OrderingError.InvalidStatusChange);
        }

        Status = OrderStatus.Cancelled;
        foreach (var line in Lines)
        {
            line.Sandwich.ReturnToStock(line.Quantity);
        }
    }

    // Only forward: Placed -> Prepared -> Collected (a step may be skipped). Cancelling goes through Cancel().
    public void MoveTo(OrderStatus status)
    {
        if (status == OrderStatus.Cancelled || status <= Status || Status == OrderStatus.Cancelled)
        {
            throw new OrderingException(OrderingError.InvalidStatusChange);
        }

        Status = status;
    }
}

public class OrderLine
{
    public int Id { get; set; }

    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public int SandwichId { get; set; }
    public Sandwich Sandwich { get; set; } = null!;

    public Bread Bread { get; set; }
    public int Quantity { get; set; } = 1;

    // The price at the moment of ordering. A later price change must not change old orders.
    public decimal UnitPrice { get; set; }
}
