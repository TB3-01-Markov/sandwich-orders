using Microsoft.AspNetCore.Mvc;

namespace SandwichOrders.Web.Controllers;

// The error pages are also shown for a failed POST; they must not ask for an anti-forgery token themselves.
[IgnoreAntiforgeryToken]
public class ErrorController : Controller
{
    // An exception that was not handled (production only, see Program.cs).
    [Route("Error")]
    public IActionResult Index() => View("Error");

    // An answer without content, e.g. 404 for an unknown address. The status code stays the same.
    [Route("Error/{code:int}")]
    public IActionResult Status(int code)
    {
        Response.StatusCode = code;
        return View(code == StatusCodes.Status404NotFound ? "NotFound" : "Error");
    }
}
