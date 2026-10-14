using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using SandwichOrders.Core;
using SandwichOrders.Core.Orders;
using SandwichOrders.Web.Infrastructure;
using SandwichOrders.Web.Models;

namespace SandwichOrders.Web.Controllers;

// US-04 and US-05. The business rules live in OrderService; this class only translates HTTP to those calls.
// Ordering does not require an account: a visitor types his name and is remembered by a guest cookie.
public class OrdersController(
    OrderService orders,
    UserManager<ApplicationUser> users,
    IStringLocalizer<SharedResource> L) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Today(CancellationToken ct)
    {
        var customerId = CustomerIdentity.CurrentId(HttpContext);

        return View(new TodayOrderViewModel
        {
            Order = customerId is null ? null : await orders.GetTodaysOrderAsync(customerId, ct),
            Window = await orders.GetWindowAsync(ct)
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create(int sandwichId, Bread bread, string? customerName, CancellationToken ct)
    {
        if (!Enum.IsDefined(bread))
        {
            return BadRequest();
        }

        string customerId;
        string name;

        var userId = CustomerIdentity.SignedInUserId(User);
        if (userId is not null)
        {
            var user = await users.GetUserAsync(User);
            if (user is null)
            {
                return Challenge();
            }

            customerId = user.Id;
            name = user.DisplayName;
        }
        else
        {
            name = (customerName ?? "").Trim();
            if (name.Length == 0 || name.Length > CustomerIdentity.MaxNameLength)
            {
                TempData["Error"] = L["Please enter your name (at most 100 characters)."].Value;
                return RedirectToAction("Index", "Menu");
            }

            customerId = CustomerIdentity.CreateGuest(HttpContext, name);
        }

        try
        {
            await orders.PlaceAsync(customerId, name, sandwichId, bread, ct);
            TempData["Success"] = L["Your order has been placed."].Value;
            return RedirectToAction(nameof(Today));
        }
        catch (OrderingException exception)
        {
            TempData["Error"] = L.Describe(exception.Error);
            return RedirectToAction("Index", "Menu");
        }
    }

    [HttpPost]
    public async Task<IActionResult> Cancel(CancellationToken ct)
    {
        var customerId = CustomerIdentity.CurrentId(HttpContext);
        if (customerId is null)
        {
            TempData["Error"] = L["You have no order for today."].Value;
            return RedirectToAction(nameof(Today));
        }

        try
        {
            await orders.CancelAsync(customerId, ct);
            TempData["Success"] = L["Your order has been cancelled."].Value;
        }
        catch (OrderingException exception)
        {
            TempData["Error"] = L.Describe(exception.Error);
        }

        return RedirectToAction(nameof(Today));
    }
}
