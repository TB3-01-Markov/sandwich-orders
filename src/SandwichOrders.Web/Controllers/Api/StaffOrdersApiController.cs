using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using SandwichOrders.Core;
using SandwichOrders.Core.Orders;
using SandwichOrders.Web.Infrastructure;

namespace SandwichOrders.Web.Controllers.Api;

// Used by the live "Today's orders" page. Signed in with the normal cookie, so every POST must also send the
// anti-forgery token in the "RequestVerificationToken" header (checked by the global AutoValidateAntiforgeryToken).
// Not signed in -> 401, signed in without the Staff role -> 403 (see Program.cs).
[ApiController]
[Route("api/staff/orders")]
[Authorize(Roles = Roles.Staff)]
public class StaffOrdersApiController(OrderService orders, IStringLocalizer<SharedResource> L) : ControllerBase
{
    [HttpGet("today")]
    public async Task<StaffDayDto> Today(CancellationToken ct) =>
        ApiMapper.ToDto(await orders.GetPreparationOverviewAsync(ct), L);

    // Ready or Collected. 204 = done; 404 = no such order; 409 = not possible now (e.g. already collected).
    [HttpPost("{id:int}/status")]
    public Task<IActionResult> SetStatus(int id, StatusChangeDto change, CancellationToken ct) =>
        Run(() => orders.SetStatusAsync(id, change.Status, ct));

    [HttpPost("{id:int}/cancel")]
    public Task<IActionResult> Cancel(int id, CancellationToken ct) =>
        Run(() => orders.CancelByStaffAsync(id, ct));

    private async Task<IActionResult> Run(Func<Task> action)
    {
        try
        {
            await action();
            return NoContent();
        }
        catch (OrderingException exception)
        {
            var body = new ApiErrorDto(L.Describe(exception.Error));
            return exception.Error == OrderingError.OrderNotFound ? NotFound(body) : Conflict(body);
        }
    }
}
