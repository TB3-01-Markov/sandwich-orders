using System.Security.Claims;
using System.Text.RegularExpressions;

namespace SandwichOrders.Web.Infrastructure;

// Who is ordering? A signed-in user is identified by the Identity user id.
// A visitor without an account gets a random guest id stored in a cookie, so they can see their own order.
// Guest ids always look like "guest-<32 hex digits>", which can never equal an Identity id (a GUID with dashes),
// so a guest cannot pretend to be a registered customer by editing the cookie.
public static partial class CustomerIdentity
{
    public const string GuestIdCookie = "guest_id";
    public const string GuestNameCookie = "guest_name";
    public const int MaxNameLength = 100;

    [GeneratedRegex("^guest-[0-9a-f]{32}$")]
    private static partial Regex GuestIdPattern();

    public static string? SignedInUserId(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true ? user.FindFirstValue(ClaimTypes.NameIdentifier) : null;

    public static string? GuestId(HttpRequest request) =>
        request.Cookies.TryGetValue(GuestIdCookie, out var value) && GuestIdPattern().IsMatch(value) ? value : null;

    public static string? GuestName(HttpRequest request) =>
        request.Cookies.TryGetValue(GuestNameCookie, out var value) ? value : null;

    // The id used to look up today's order: the account if signed in, otherwise the guest cookie (may be null).
    public static string? CurrentId(HttpContext context) =>
        SignedInUserId(context.User) ?? GuestId(context.Request);

    public static string CreateGuest(HttpContext context, string name)
    {
        var id = GuestId(context.Request) ?? "guest-" + Guid.NewGuid().ToString("N");
        var options = new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = context.Request.IsHttps,
            Expires = DateTimeOffset.UtcNow.AddDays(30)
        };
        context.Response.Cookies.Append(GuestIdCookie, id, options);
        context.Response.Cookies.Append(GuestNameCookie, name, options);
        return id;
    }
}
