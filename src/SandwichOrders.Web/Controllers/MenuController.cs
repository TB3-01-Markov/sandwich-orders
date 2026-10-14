using Microsoft.AspNetCore.Mvc;
using SandwichOrders.Core.Menu;
using SandwichOrders.Core.Orders;
using SandwichOrders.Web.Infrastructure;
using SandwichOrders.Web.Models;

namespace SandwichOrders.Web.Controllers;

// US-01: the menu is public. Everyone sees the photos, ingredients, prices and the order deadline.
public class MenuController(MenuService menu, OrderService orders) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var customerId = CustomerIdentity.CurrentId(HttpContext);

        return View(new MenuViewModel
        {
            Sandwiches = await menu.GetMenuAsync(ct),
            Window = await orders.GetWindowAsync(ct),
            IsSignedIn = CustomerIdentity.SignedInUserId(User) is not null,
            GuestName = CustomerIdentity.GuestName(Request) ?? "",
            TodaysOrder = customerId is null ? null : await orders.GetTodaysOrderAsync(customerId, ct)
        });
    }
}
