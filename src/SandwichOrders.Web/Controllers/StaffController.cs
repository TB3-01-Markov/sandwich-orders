using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using SandwichOrders.Core;
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
    PhotoStorage photos,
    IStringLocalizer<SharedResource> L) : Controller
{
    // US-06 and US-07: who ordered what, and how many of each sandwich and bread to prepare.
    [HttpGet]
    public async Task<IActionResult> Today(CancellationToken ct) =>
        View(await orders.GetPreparationOverviewAsync(ct));

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
            var sandwich = (await menu.GetAllSandwichesAsync(ct)).FirstOrDefault(s => s.Id == id);
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
                    model.IngredientIds, model.IsActive, keepImage, photo?.Path), ct);

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
    public async Task<IActionResult> AddIngredient(string? nameEn, string? nameNl, CancellationToken ct)
    {
        try
        {
            await menu.AddIngredientAsync(nameEn ?? "", nameNl ?? "", ct);
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
}
