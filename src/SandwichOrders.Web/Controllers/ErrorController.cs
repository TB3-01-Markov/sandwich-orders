using Microsoft.AspNetCore.Mvc;

namespace SandwichOrders.Web.Controllers;

public class ErrorController : Controller
{
    [Route("Error")]
    public IActionResult Index() => View("Error");
}
