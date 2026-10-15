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
    public async Task<IActionResult> Index(int[]? include, int[]? exclude, CancellationToken ct)
    {
        var customerId = CustomerIdentity.CurrentId(HttpContext);
        var whole = await menu.GetMenuAsync(ct);
        var includeIds = (include ?? []).Distinct().ToList();
        var excludeIds = (exclude ?? []).Distinct().ToList();

        return View(new MenuViewModel
        {
            Sandwiches = MenuFilter.Apply(whole, includeIds, excludeIds),
            TotalCount = whole.Count,
            Ingredients = await menu.GetIngredientsAsync(ct),
            Include = includeIds,
            Exclude = excludeIds,
            Window = await orders.GetWindowAsync(ct),
            IsSignedIn = CustomerIdentity.SignedInUserId(User) is not null,
            GuestName = CustomerIdentity.GuestName(Request) ?? "",
            TodaysOrder = customerId is null ? null : await orders.GetTodaysOrderAsync(customerId, ct)
        });
    }
}
