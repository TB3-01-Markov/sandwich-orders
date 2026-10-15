using SandwichOrders.Core.Announcements;
using SandwichOrders.Core.Menu;

namespace SandwichOrders.Core.Orders;

// The business rules for ordering (US-04, US-05, US-06, US-07, US-15) plus stock and closing days.
// Rules that refuse an action throw OrderingException with a code; nothing here knows about HTTP or databases.
public class OrderService(
    IOrderRepository orders,
    IMenuRepository menu,
    IOrderingSettings settings,
    IAnnouncementRepository announcements,
    TimeProvider clock)
{
    public async Task<OrderingWindow> GetWindowAsync(CancellationToken ct = default)
    {
        var deadline = await settings.GetDeadlineAsync(ct);
        var weekend = !OrderingRules.IsWorkingDay(BrusselsTime.Today(clock));
        var closedToday = await IsClosedTodayAsync(ct);
        return new OrderingWindow(deadline, !closedToday && OrderingRules.IsOpen(TimeOfDayNow(), deadline), closedToday, weekend);
    }

    // US-04: one order per person per day, only before the deadline, not at the weekend or on a closing day,
    // only for an available sandwich with stock left. The stock is taken in the same save as the order.
    // customerPhone: only registered customers can have one (guests order with a name only).
    public async Task<Order> PlaceAsync(
        string customerId,
        string customerName,
        int sandwichId,
        Bread bread,
        string? customerPhone = null,
        CancellationToken ct = default)
    {
        if (!Enum.IsDefined(bread))
        {
            throw new ArgumentOutOfRangeException(nameof(bread));
        }

        if (await IsClosedTodayAsync(ct))
        {
            throw new OrderingException(OrderingError.CanteenClosed);
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

        const int quantity = 1;
        sandwich.TakeFromStock(quantity);

        var order = new Order
        {
            CustomerId = customerId,
            CustomerName = customerName,
            CustomerPhone = string.IsNullOrWhiteSpace(customerPhone) ? null : customerPhone.Trim(),
            OrderDate = today,
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
            Status = OrderStatus.Placed
        };
        order.Lines.Add(new OrderLine
        {
            SandwichId = sandwich.Id,
            Sandwich = sandwich,
            Bread = bread,
            Quantity = quantity,
            UnitPrice = sandwich.Price + OrderingRules.SurchargeFor(bread)
        });

        orders.Add(order);
        await orders.SaveChangesAsync(ct);
        return order;
    }

    // US-05 changed: customers cannot cancel themselves, because once the canteen has started making a sandwich it is
    // hard to undo. The canteen employee cancels any order that is not collected yet; the stock goes back.
    public async Task CancelByStaffAsync(int orderId, CancellationToken ct = default)
    {
        var order = await orders.GetAsync(orderId, ct) ?? throw new OrderingException(OrderingError.OrderNotFound);
        order.Cancel();
        await orders.SaveChangesAsync(ct);
    }

    // The canteen employee marks an order as ready (Prepared) or collected. The customer sees it on "My order".
    public async Task SetStatusAsync(int orderId, OrderStatus status, CancellationToken ct = default)
    {
        var order = await orders.GetAsync(orderId, ct) ?? throw new OrderingException(OrderingError.OrderNotFound);
        order.MoveTo(status);
        await orders.SaveChangesAsync(ct);
    }

    public Task<Order?> GetTodaysOrderAsync(string customerId, CancellationToken ct = default) =>
        orders.GetActiveForCustomerAsync(customerId, BrusselsTime.Today(clock), ct);

    // US-06 and US-07: who ordered what today, and how many of each sandwich and bread to prepare.
    public async Task<PreparationOverview> GetPreparationOverviewAsync(CancellationToken ct = default)
    {
        var today = BrusselsTime.Today(clock);
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

    // Closed at the weekend, and on days an announcement blocks ordering.
    private async Task<bool> IsClosedTodayAsync(CancellationToken ct)
    {
        var today = BrusselsTime.Today(clock);
        return !OrderingRules.IsWorkingDay(today)
               || (await announcements.GetAllAsync(ct)).Any(a => a.BlocksOrderingOn(today));
    }

    private TimeOnly TimeOfDayNow() => TimeOnly.FromDateTime(BrusselsTime.Now(clock));
}
