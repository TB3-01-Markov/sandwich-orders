using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using SandwichOrders.Core;
using SandwichOrders.Core.Announcements;
using SandwichOrders.Core.Menu;
using SandwichOrders.Core.Orders;
using SandwichOrders.Web.Infrastructure;
using SandwichOrders.Web.Models;

namespace SandwichOrders.Web.Controllers;

// Announcements: "closed from ... to ...", informing first, blocking ordering during the period.
// Split off from StaffController; the attribute route keeps the old addresses (/Staff/Announcements...).
[Authorize(Roles = Roles.Staff)]
[Route("Staff/[action]/{id?}")]
[ApiExplorerSettings(IgnoreApi = true)]
public class StaffAnnouncementsController(
    AnnouncementService announcements,
    TimeProvider clock,
    IStringLocalizer<SharedResource> L) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Announcements(CancellationToken ct) =>
        View(new StaffAnnouncementsViewModel
        {
            Announcements = await announcements.GetAllAsync(ct),
            Today = BrusselsTime.Today(clock)
        });

    [HttpGet]
    public async Task<IActionResult> AnnouncementEdit(int? id, CancellationToken ct)
    {
        if (id is null)
        {
            var today = BrusselsTime.Today(clock);
            return View(new AnnouncementEditViewModel
            {
                ShowFrom = today,
                PeriodStart = today.AddDays(7),
                PeriodEnd = today.AddDays(7),
                BlocksOrdering = true
            });
        }

        var announcement = await announcements.GetAsync(id.Value, ct);
        if (announcement is null)
        {
            return NotFound();
        }

        return View(new AnnouncementEditViewModel
        {
            Id = announcement.Id,
            MessageEn = announcement.MessageEn,
            MessageNl = announcement.MessageNl,
            PeriodMessageEn = announcement.PeriodMessageEn,
            PeriodMessageNl = announcement.PeriodMessageNl,
            ShowFrom = announcement.ShowFrom,
            PeriodStart = announcement.PeriodStart,
            PeriodEnd = announcement.PeriodEnd,
            BlocksOrdering = announcement.BlocksOrdering
        });
    }

    [HttpPost]
    public async Task<IActionResult> AnnouncementEdit(AnnouncementEditViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            await announcements.SaveAsync(new AnnouncementInput(
                model.Id, model.MessageEn, model.MessageNl, model.PeriodMessageEn, model.PeriodMessageNl,
                model.ShowFrom!.Value, model.PeriodStart!.Value, model.PeriodEnd!.Value, model.BlocksOrdering), ct);

            TempData["Success"] = L["The announcement has been saved."].Value;
            return RedirectToAction(nameof(Announcements));
        }
        catch (AnnouncementException exception)
        {
            ModelState.AddModelError("", L.Describe(exception.Error));
            return View(model);
        }
    }

    [HttpPost]
    public async Task<IActionResult> AnnouncementDelete(int id, CancellationToken ct)
    {
        try
        {
            await announcements.DeleteAsync(id, ct);
            TempData["Success"] = L["The announcement has been deleted."].Value;
        }
        catch (AnnouncementException exception)
        {
            TempData["Error"] = L.Describe(exception.Error);
        }

        return RedirectToAction(nameof(Announcements));
    }
}
