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

// Everything the canteen employee needs: US-06, US-07, US-08, US-13 and US-15.
[Authorize(Roles = Roles.Staff)]
public class StaffController(
    OrderService orders,
    MenuService menu,
    AnnouncementService announcements,
    TimeProvider clock,
    PhotoStorage photos,
    IStringLocalizer<SharedResource> L) : Controller
{
    // US-06 and US-07: who ordered what, and how many of each sandwich and bread to prepare.
    [HttpGet]
    public async Task<IActionResult> Today(CancellationToken ct) =>
        View(new StaffDayViewModel { Overview = await orders.GetPreparationOverviewAsync(ct), IsToday = true });

    // The canteen's history: per day of a month how many orders and for how much. Default: the current month.
    [HttpGet]
    public async Task<IActionResult> History(int? year, int? month, CancellationToken ct)
    {
        var today = BrusselsTime.Today(clock);
        var y = year ?? today.Year;
        var m = month ?? today.Month;
        if (y is < 2000 or > 2100 || m is < 1 or > 12)
        {
            (y, m) = (today.Year, today.Month);
        }

        return View(new StaffHistoryViewModel { Month = await orders.GetMonthSummaryAsync(y, m, ct), Today = today });
    }

    // Who ordered what on an earlier day. Same page as Today, but without the status buttons.
    [HttpGet]
    public async Task<IActionResult> Day(DateOnly? date, CancellationToken ct)
    {
        if (date is null)
        {
            return RedirectToAction(nameof(History));
        }

        if (date == BrusselsTime.Today(clock))
        {
            return RedirectToAction(nameof(Today));
        }

        return View(nameof(Today), new StaffDayViewModel
        {
            Overview = await orders.GetOverviewForDateAsync(date.Value, ct),
            IsToday = false
        });
    }

    // The canteen employee marks an order as ready or collected; the customer sees it on "My order".
    [HttpPost]
    public async Task<IActionResult> SetOrderStatus(int id, OrderStatus status, CancellationToken ct)
    {
        try
        {
            await orders.SetStatusAsync(id, status, ct);
        }
        catch (OrderingException exception)
        {
            TempData["Error"] = L.Describe(exception.Error);
        }

        return RedirectToAction(nameof(Today));
    }

    // Only the canteen employee can cancel (customers cannot); the ingredients or pieces go back into stock.
    [HttpPost]
    public async Task<IActionResult> CancelOrder(int id, CancellationToken ct)
    {
        try
        {
            await orders.CancelByStaffAsync(id, ct);
            TempData["Success"] = L["The order has been cancelled and the stock was put back."].Value;
        }
        catch (OrderingException exception)
        {
            TempData["Error"] = L.Describe(exception.Error);
        }

        return RedirectToAction(nameof(Today));
    }

    [HttpGet]
    public async Task<IActionResult> Menu(CancellationToken ct) =>
        View(new StaffMenuViewModel
        {
            Sandwiches = await menu.GetAllSandwichesAsync(ct),
            Ingredients = await menu.GetIngredientsAsync(ct)
        });

    // US-08. The form says explicitly which state it wants, so a double click cannot flip it back.
    [HttpPost]
    public async Task<IActionResult> SetSandwichUnavailable(int id, bool unavailable, CancellationToken ct)
    {
        try
        {
            await menu.SetTemporarilyUnavailableAsync(id, unavailable, ct);
        }
        catch (OrderingException exception)
        {
            TempData["Error"] = L.Describe(exception.Error);
        }
        catch (MenuException exception)
        {
            TempData["Error"] = L.Describe(exception.Error);
        }

        return RedirectToAction(nameof(Menu));
    }

    // US-13
    [HttpPost]
    public async Task<IActionResult> SetIngredientAvailable(int id, bool available, CancellationToken ct)
    {
        try
        {
            await menu.SetIngredientAvailableAsync(id, available, ct);
        }
        catch (OrderingException exception)
        {
            TempData["Error"] = L.Describe(exception.Error);
        }
        catch (MenuException exception)
        {
            TempData["Error"] = L.Describe(exception.Error);
        }

        return RedirectToAction(nameof(Menu));
    }

    // Counted stock of an ingredient, in portions. An empty field = stop counting this ingredient.
    [HttpPost]
    public async Task<IActionResult> SetIngredientStock(int id, int? portions, CancellationToken ct)
    {
        try
        {
            await menu.SetIngredientStockAsync(id, portions, ct);
            TempData["Success"] = L["The stock has been saved."].Value;
        }
        catch (MenuException exception)
        {
            TempData["Error"] = L.Describe(exception.Error);
        }

        return RedirectToAction(nameof(Menu));
    }

    // Pieces left of a supplied sandwich (after a delivery). An empty field = stop counting.
    [HttpPost]
    public async Task<IActionResult> SetSandwichStock(int id, int? count, CancellationToken ct)
    {
        try
        {
            await menu.SetSandwichStockAsync(id, count, ct);
            TempData["Success"] = L["The stock has been saved."].Value;
        }
        catch (MenuException exception)
        {
            TempData["Error"] = L.Describe(exception.Error);
        }

        return RedirectToAction(nameof(Menu));
    }

    // ---- Menu management: add and change sandwiches, photos and ingredients ---------------------------------

    [HttpGet]
    public async Task<IActionResult> SandwichEdit(int? id, CancellationToken ct)
    {
        var model = new SandwichEditViewModel();

        if (id is null)
        {
            var all = await menu.GetAllSandwichesAsync(ct);
            model.Number = all.Count == 0 ? 1 : all.Max(s => s.Number) + 1;
            model.PriceText = "";
        }
        else
        {
            var sandwich = await menu.GetSandwichAsync(id.Value, ct);
            if (sandwich is null)
            {
                return NotFound();
            }

            model.Id = sandwich.Id;
            model.Number = sandwich.Number;
            model.NameEn = sandwich.NameEn;
            model.NameNl = sandwich.NameNl;
            model.DescriptionEn = sandwich.DescriptionEn;
            model.DescriptionNl = sandwich.DescriptionNl;
            model.PriceText = sandwich.Price.ToString("0.00", System.Globalization.CultureInfo.CurrentCulture);
            model.IsActive = sandwich.IsActive;
            model.ExistingImagePath = sandwich.ImagePath;
            model.IngredientIds = sandwich.SandwichIngredients.Select(si => si.IngredientId).ToList();
            model.Portions = sandwich.SandwichIngredients.ToDictionary(si => si.IngredientId, si => si.Portions);
            model.Kind = sandwich.Kind;
            model.StockCount = sandwich.StockCount;
        }

        model.AllIngredients = await menu.GetIngredientsAsync(ct);
        return View(model);
    }

    [HttpPost]
    [RequestSizeLimit(PhotoStorage.MaxBytes + 512 * 1024)]
    public async Task<IActionResult> SandwichEdit(SandwichEditViewModel model, CancellationToken ct)
    {
        decimal price = 0;
        if (!string.IsNullOrWhiteSpace(model.PriceText) && !Money.TryParse(model.PriceText, out price))
        {
            ModelState.AddModelError(nameof(model.PriceText), L["Enter a price above 0 with at most two decimals, for example 4.50."].Value);
        }

        PhotoSaveResult? photo = null;
        if (ModelState.IsValid && model.Photo is { Length: > 0 })
        {
            photo = await photos.SaveAsync(model.Photo, ct);
            if (photo.Error != PhotoError.None)
            {
                ModelState.AddModelError(nameof(model.Photo), L.Describe(photo.Error));
            }
        }

        if (ModelState.IsValid)
        {
            var keepImage = photo?.Path is null && !model.RemovePhoto;
            try
            {
                var saved = await menu.SaveSandwichAsync(new SandwichInput(
                    model.Id, model.Number!.Value, model.NameEn!, model.NameNl!,
                    model.DescriptionEn, model.DescriptionNl, price,
                    model.IngredientIds, model.IsActive, keepImage, photo?.Path,
                    model.Kind, model.StockCount, model.Portions), ct);

                photos.Delete(saved.OldImagePath);
                TempData["Success"] = L["The sandwich has been saved."].Value;
                return RedirectToAction(nameof(Menu));
            }
            catch (MenuException exception)
            {
                photos.Delete(photo?.Path); // the new file was not used
                ModelState.AddModelError("", L.Describe(exception.Error));
            }
        }

        model.AllIngredients = await menu.GetIngredientsAsync(ct);
        return View(model);
    }

    // A sandwich is never deleted (old orders point to it); it is taken off the menu and can be put back.
    [HttpPost]
    public async Task<IActionResult> SetSandwichActive(int id, bool active, CancellationToken ct)
    {
        try
        {
            await menu.SetActiveAsync(id, active, ct);
        }
        catch (MenuException exception)
        {
            TempData["Error"] = L.Describe(exception.Error);
        }

        return RedirectToAction(nameof(Menu));
    }

    [HttpPost]
    public async Task<IActionResult> AddIngredient(string? nameEn, string? nameNl, int? stockPortions, CancellationToken ct)
    {
        try
        {
            await menu.AddIngredientAsync(nameEn ?? "", nameNl ?? "", stockPortions, ct);
            TempData["Success"] = L["The ingredient has been added."].Value;
        }
        catch (MenuException exception)
        {
            TempData["Error"] = L.Describe(exception.Error);
        }

        return RedirectToAction(nameof(Menu));
    }

    // US-15
    [HttpGet]
    public async Task<IActionResult> Deadline(CancellationToken ct) =>
        View(new DeadlineViewModel { Deadline = (await orders.GetWindowAsync(ct)).Deadline });

    [HttpPost]
    public async Task<IActionResult> Deadline(DeadlineViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid || model.Deadline is null)
        {
            return View(model);
        }

        await orders.SetDeadlineAsync(model.Deadline.Value, ct);
        TempData["Success"] = L["The deadline has been saved."].Value;
        return RedirectToAction(nameof(Deadline));
    }

    // ---- Announcements: "closed from ... to ...", informing first, blocking during the period -----------------

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
