using Microsoft.AspNetCore.Identity;

namespace SandwichOrders.Web.Models;

public class ApplicationUser : IdentityUser
{
    // The name the canteen reads out for an order.
    public string DisplayName { get; set; } = "";
}

// A simple key/value row, used for the order deadline (US-15).
public class AppSetting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
