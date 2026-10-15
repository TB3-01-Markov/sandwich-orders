using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using SandwichOrders.Core.Orders;
using SandwichOrders.Web.Infrastructure;

namespace SandwichOrders.Web.Controllers.Api;

// The visitor's own order for today (signed-in account or guest cookie), so "My order" can show
// "your sandwich is ready" as soon as the canteen presses Ready.
[ApiController]
[Route("api/orders")]
public class OrdersApiController(OrderService orders, IStringLocalizer<SharedResource> L) : ControllerBase
{
    [HttpGet("today")]
    [ProducesResponseType<MyOrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Today(CancellationToken ct)
    {
        var customerId = CustomerIdentity.CurrentId(HttpContext);
        var order = customerId is null ? null : await orders.GetTodaysOrderAsync(customerId, ct);

        return order is null ? NoContent() : Ok(ApiMapper.ToDto(order, L));
    }
}
