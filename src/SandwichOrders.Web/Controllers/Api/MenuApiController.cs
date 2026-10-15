using Microsoft.AspNetCore.Mvc;
using SandwichOrders.Core.Menu;
using SandwichOrders.Core.Orders;

namespace SandwichOrders.Web.Controllers.Api;

// Public, read only. The menu page asks this every few seconds to update "Only N left", "Sold out" and the
// countdown to the deadline without reloading. The server still checks everything again when an order is placed.
[ApiController]
[Route("api/menu")]
public class MenuApiController(MenuService menu, OrderService orders) : ControllerBase
{
    [HttpGet("status")]
    public async Task<MenuStatusDto> Status(CancellationToken ct) =>
        ApiMapper.ToDto(await orders.GetWindowAsync(ct), await menu.GetMenuAsync(ct));
}
