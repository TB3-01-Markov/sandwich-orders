using SandwichOrders.Core.Menu;

namespace SandwichOrders.Core.Orders;

public class Order
{
    public int Id { get; set; }

    // Id of the signed-in user (Identity). A plain string on purpose: the core knows nothing about Identity.
    public string CustomerId { get; set; } = "";

    // The name the canteen reads out, copied at order time (replaces the name written on paper).
    public string CustomerName { get; set; } = "";

    // The Belgian calendar day the order is for.
    public DateOnly OrderDate { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Placed;
    public ICollection<OrderLine> Lines { get; set; } = new List<OrderLine>();

    public decimal Total => Lines.Sum(line => line.UnitPrice * line.Quantity);
    public bool IsCancelled => Status == OrderStatus.Cancelled;

    public void Cancel()
    {
        if (Status == OrderStatus.Collected)
        {
            throw new InvalidOperationException("A collected order cannot be cancelled.");
        }

        Status = OrderStatus.Cancelled;
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
