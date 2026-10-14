using SandwichOrders.Core.Menu;

namespace SandwichOrders.Core.Orders;

// What the core needs from storage. The web project implements these with Entity Framework Core;
// unit tests replace them with NSubstitute.
public interface IOrderRepository
{
    // The customer's order for that day, unless it was cancelled. Lines and sandwiches are loaded.
    Task<Order?> GetActiveForCustomerAsync(string customerId, DateOnly date, CancellationToken ct = default);

    // All non-cancelled orders for that day, sorted by customer name. Lines and sandwiches are loaded.
    Task<IReadOnlyList<Order>> GetActiveForDateAsync(DateOnly date, CancellationToken ct = default);

    void Add(Order order);

    Task SaveChangesAsync(CancellationToken ct = default);
}

public interface IOrderingSettings
{
    Task<TimeOnly> GetDeadlineAsync(CancellationToken ct = default);

    Task SetDeadlineAsync(TimeOnly deadline, CancellationToken ct = default);
}

public static class OrderingRules
{
    // Used until the canteen employee sets another time (US-15).
    public static readonly TimeOnly DefaultDeadline = new(9, 30);

    // Ordering is open strictly BEFORE the deadline: 09:29:59 is open, 09:30:00 is closed.
    public static bool IsOpen(TimeOnly timeOfDay, TimeOnly deadline) => timeOfDay < deadline;
}

public record OrderingWindow(TimeOnly Deadline, bool IsOpen);

// One line of the canteen's preparation list: how many of this sandwich on this bread (US-07).
public record PreparationRow(Sandwich Sandwich, Bread Bread, int Quantity);

public record PreparationOverview(
    DateOnly Date,
    IReadOnlyList<Order> Orders,
    IReadOnlyList<PreparationRow> Rows,
    int TotalSandwiches,
    decimal TotalAmount);
