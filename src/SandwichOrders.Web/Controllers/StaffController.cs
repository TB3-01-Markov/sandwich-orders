using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using SandwichOrders.Core;
using SandwichOrders.Core.Announcements;
using SandwichOrders.Core.Menu;
using SandwichOrders.Core.Orders;
using SandwichOrders.Web.Infrastructure;
using SandwichOrders.Web.Models;

namespace SandwichOrders.Web.Controllers;

// The canteen employee's daily work: today's orders and their status (US-06, US-07), the history and the deadline
// (US-15). Menu management and announcements have their own controllers with the same /Staff/... addresses.
[Authorize(Roles = Roles.Staff)]
public class StaffController(
    OrderService orders,
    TimeProvider clock,
    IStringLocalizer<SharedResource> L) : Controller
{
    // US-06 and US-07: who ordered what, and how many of each sandwich and bread to prepare.
    [HttpGet]
    public async Task<IActionResult> Today(CancellationToken ct) =>
        View(new StaffDayViewModel { Overview = await orders.GetPreparationOverviewAsync(ct), IsToday = true });

    // The canteen's history: per day of a month how many orders and for how much. Default: the current month.
    [HttpGet]
    public async Task<IActionResult> History(int? year, int? month, CancellationToken ct)
    {
        var today = BrusselsTime.Today(clock);
        var y = year ?? today.Year;
        var m = month ?? today.Month;
        if (y is < 2000 or > 2100 || m is < 1 or > 12)
        {
            (y, m) = (today.Year, today.Month);
        }

        return View(new StaffHistoryViewModel { Month = await orders.GetMonthSummaryAsync(y, m, ct), Today = today });
    }

    // Who ordered what on an earlier day. Same page as Today, but without the status buttons.
    [HttpGet]
    public async Task<IActionResult> Day(DateOnly? date, CancellationToken ct)
    {
        if (date is null)
        {
            return RedirectToAction(nameof(History));
        }

        if (date == BrusselsTime.Today(clock))
        {
            return RedirectToAction(nameof(Today));
        }

        return View(nameof(Today), new StaffDayViewModel
        {
            Overview = await orders.GetOverviewForDateAsync(date.Value, ct),
            IsToday = false
        });
    }

    // The canteen employee marks an order as ready or collected; the customer sees it on "My order".
    [HttpPost]
    public async Task<IActionResult> SetOrderStatus(int id, OrderStatus status, CancellationToken ct)
    {
        try
        {
            await orders.SetStatusAsync(id, status, ct);
        }
        catch (OrderingException exception)
        {
            TempData["Error"] = L.Describe(exception.Error);
        }

        return RedirectToAction(nameof(Today));
    }

    // Only the canteen employee can cancel (customers cannot); the ingredients or pieces go back into stock.
    [HttpPost]
    public async Task<IActionResult> CancelOrder(int id, CancellationToken ct)
    {
        try
        {
            await orders.CancelByStaffAsync(id, ct);
            TempData["Success"] = L["The order has been cancelled and the stock was put back."].Value;
        }
        catch (OrderingException exception)
        {
            TempData["Error"] = L.Describe(exception.Error);
        }

        return RedirectToAction(nameof(Today));
    }

    // US-15
    [HttpGet]
    public async Task<IActionResult> Deadline(CancellationToken ct) =>
        View(new DeadlineViewModel { Deadline = (await orders.GetWindowAsync(ct)).Deadline });

    [HttpPost]
    public async Task<IActionResult> Deadline(DeadlineViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid || model.Deadline is null)
        {
            return View(model);
        }

        await orders.SetDeadlineAsync(model.Deadline.Value, ct);
        TempData["Success"] = L["The deadline has been saved."].Value;
        return RedirectToAction(nameof(Deadline));
    }
}
