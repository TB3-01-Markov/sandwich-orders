using Microsoft.Extensions.Localization;
using SandwichOrders.Core;
using SandwichOrders.Core.Menu;
using SandwichOrders.Core.Orders;
using SandwichOrders.Web.Infrastructure;

namespace SandwichOrders.Web.Controllers.Api;

// What the JSON API returns. Texts (names, bread, status, money) come already translated and formatted for the
// visitor's language, so the JavaScript in the pages only puts them on the screen.

// GET /api/menu/status. SecondsLeft counts down to the deadline (0 when ordering is closed).
public record MenuStatusDto(
    bool IsOpen,
    bool ClosedToday,
    bool Weekend,
    string Deadline,
    int SecondsLeft,
    IReadOnlyList<SandwichStatusDto> Sandwiches);

// Left = how many can still be ordered; null = not counted.
public record SandwichStatusDto(int Id, bool Available, int? Left);

public record OrderLineDto(int Number, string Name, Bread Bread, string BreadText, string PriceText);

// GET /api/orders/today: the visitor's own order for today.
public record MyOrderDto(int Id, OrderStatus Status, string StatusText, IReadOnlyList<OrderLineDto> Lines, string TotalText);

// GET /api/staff/orders/today
public record StaffDayDto(
    string Date,
    IReadOnlyList<PreparationRowDto> Rows,
    int TotalSandwiches,
    string TotalAmountText,
    IReadOnlyList<StaffOrderDto> Orders);

public record PreparationRowDto(int Number, string Name, Bread Bread, string BreadText, int Quantity);

public record StaffOrderDto(
    int Id,
    string CustomerName,
    string? Phone,
    OrderStatus Status,
    string StatusText,
    IReadOnlyList<OrderLineDto> Lines);

// POST /api/staff/orders/{id}/status
public record StatusChangeDto(OrderStatus Status);

// Every refusal has a readable, translated message.
public record ApiErrorDto(string Error);

public static class ApiMapper
{
    public static MenuStatusDto ToDto(OrderingWindow window, IEnumerable<Sandwich> sandwiches) => new(
        window.IsOpen,
        window.ClosedToday,
        window.Weekend,
        window.Deadline.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
        (int)Math.Ceiling(window.TimeLeft.TotalSeconds),
        sandwiches.Select(s => new SandwichStatusDto(s.Id, s.IsAvailable, s.AvailableCount)).ToList());

    public static MyOrderDto ToDto(Order order, IStringLocalizer L) => new(
        order.Id,
        order.Status,
        L.StatusName(order.Status),
        Lines(order, L),
        Money.Format(order.Total));

    public static StaffDayDto ToDto(PreparationOverview overview, IStringLocalizer L) => new(
        Dates.Format(overview.Date),
        overview.Rows
            .Select(row => new PreparationRowDto(row.Sandwich.Number, row.Sandwich.Name(), row.Bread, L.BreadName(row.Bread), row.Quantity))
            .ToList(),
        overview.TotalSandwiches,
        Money.Format(overview.TotalAmount),
        overview.Orders
            .Select(order => new StaffOrderDto(
                order.Id, order.CustomerName, order.CustomerPhone, order.Status, L.StatusName(order.Status), Lines(order, L)))
            .ToList());

    private static IReadOnlyList<OrderLineDto> Lines(Order order, IStringLocalizer L) =>
        order.Lines
            .Select(line => new OrderLineDto(
                line.Sandwich.Number, line.Sandwich.Name(), line.Bread, L.BreadName(line.Bread), Money.Format(line.UnitPrice * line.Quantity)))
            .ToList();
}
