using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

namespace SandwichOrders.Web.Controllers;

// US-02: the language is remembered in a cookie, so it stays after the visitor comes back.
public class CultureController : Controller
{
    [HttpPost]
    public IActionResult Set(string culture, string? returnUrl)
    {
        if (culture is not ("en" or "nl"))
        {
            culture = "en";
        }

        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                IsEssential = true,
                SameSite = SameSiteMode.Lax
            });

        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl : "~/");
    }
}
