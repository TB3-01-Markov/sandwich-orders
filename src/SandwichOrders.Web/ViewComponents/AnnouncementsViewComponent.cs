using Microsoft.AspNetCore.Mvc;
using SandwichOrders.Core;
using SandwichOrders.Core.Announcements;
using SandwichOrders.Web.Models;

namespace SandwichOrders.Web.ViewComponents;

// Shows the canteen's announcements at the top of every page (rendered from _Layout).
public class AnnouncementsViewComponent(AnnouncementService announcements, TimeProvider clock) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync() =>
        View(new AnnouncementBannerViewModel
        {
            Announcements = await announcements.GetVisibleTodayAsync(HttpContext.RequestAborted),
            Today = BrusselsTime.Today(clock)
        });
}
