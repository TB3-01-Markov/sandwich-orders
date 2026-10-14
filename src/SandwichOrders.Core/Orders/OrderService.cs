using SandwichOrders.Core.Menu;

namespace SandwichOrders.Core.Orders;

// The business rules for ordering (US-04, US-05, US-06, US-07, US-15).
// Rules that refuse an action throw OrderingException with a code; nothing here knows about HTTP or databases.
public class OrderService(
    IOrderRepository orders,
    IMenuRepository menu,
    IOrderingSettings settings,
    TimeProvider clock)
{
    public async Task<OrderingWindow> GetWindowAsync(CancellationToken ct = default)
    {
        var deadline = await settings.GetDeadlineAsync(ct);
        return new OrderingWindow(deadline, OrderingRules.IsOpen(TimeOfDayNow(), deadline));
    }

    // US-04: one order per person per day, only before the deadline, only for an available sandwich.
    public async Task<Order> PlaceAsync(
        string customerId,
        string customerName,
        int sandwichId,
        Bread bread,
        CancellationToken ct = default)
    {
        if (!Enum.IsDefined(bread))
        {
            throw new ArgumentOutOfRangeException(nameof(bread));
        }

        var now = BrusselsTime.Now(clock);
        var deadline = await settings.GetDeadlineAsync(ct);
        if (!OrderingRules.IsOpen(TimeOnly.FromDateTime(now), deadline))
        {
            throw new OrderingException(OrderingError.DeadlinePassed);
        }

        var today = DateOnly.FromDateTime(now);
        if (await orders.GetActiveForCustomerAsync(customerId, today, ct) is not null)
        {
            throw new OrderingException(OrderingError.AlreadyOrdered);
        }

        var sandwich = await menu.GetAsync(sandwichId, ct);
        if (sandwich is null || !sandwich.IsAvailable)
        {
            throw new OrderingException(OrderingError.SandwichUnavailable);
        }

        var order = new Order
        {
            CustomerId = customerId,
            CustomerName = customerName,
            OrderDate = today,
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
            Status = OrderStatus.Placed
        };
        order.Lines.Add(new OrderLine
        {
            SandwichId = sandwich.Id,
            Sandwich = sandwich,
            Bread = bread,
            Quantity = 1,
            UnitPrice = sandwich.Price
        });

        orders.Add(order);
        await orders.SaveChangesAsync(ct);
        return order;
    }

    // US-05: cancelling is only possible before the deadline, just like ordering.
    public async Task CancelAsync(string customerId, CancellationToken ct = default)
    {
        var now = BrusselsTime.Now(clock);
        var order = await orders.GetActiveForCustomerAsync(customerId, DateOnly.FromDateTime(now), ct)
                    ?? throw new OrderingException(OrderingError.OrderNotFound);

        var deadline = await settings.GetDeadlineAsync(ct);
        if (!OrderingRules.IsOpen(TimeOnly.FromDateTime(now), deadline))
        {
            throw new OrderingException(OrderingError.DeadlinePassed);
        }

        order.Cancel();
        await orders.SaveChangesAsync(ct);
    }

    public Task<Order?> GetTodaysOrderAsync(string customerId, CancellationToken ct = default) =>
        orders.GetActiveForCustomerAsync(customerId, DateOnly.FromDateTime(BrusselsTime.Now(clock)), ct);

    // US-06 and US-07: who ordered what today, and how many of each sandwich and bread to prepare.
    public async Task<PreparationOverview> GetPreparationOverviewAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(BrusselsTime.Now(clock));
        var todaysOrders = await orders.GetActiveForDateAsync(today, ct);

        var rows = todaysOrders
            .SelectMany(order => order.Lines)
            .GroupBy(line => (line.SandwichId, line.Bread))
            .Select(group => new PreparationRow(group.First().Sandwich, group.Key.Bread, group.Sum(line => line.Quantity)))
            .OrderBy(row => row.Sandwich.Number)
            .ThenBy(row => row.Bread)
            .ToList();

        return new PreparationOverview(
            today,
            todaysOrders,
            rows,
            rows.Sum(row => row.Quantity),
            todaysOrders.Sum(order => order.Total));
    }

    // US-15: the canteen employee decides until when trainees may order.
    public Task SetDeadlineAsync(TimeOnly deadline, CancellationToken ct = default) =>
        settings.SetDeadlineAsync(deadline, ct);

    private TimeOnly TimeOfDayNow() => TimeOnly.FromDateTime(BrusselsTime.Now(clock));
}
