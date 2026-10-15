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

// The canteen employee manages the menu (US-08, US-13): sandwiches, photos, ingredients and stock.
// Split off from StaffController; the attribute route keeps the old addresses (/Staff/Menu, /Staff/SandwichEdit/5...).
// Not part of the API description (these are HTML pages, see Program.cs: OpenAPI).
[Authorize(Roles = Roles.Staff)]
[Route("Staff/[action]/{id?}")]
[ApiExplorerSettings(IgnoreApi = true)]
public class MenuManagementController(
    MenuService menu,
    PhotoStorage photos,
    IStringLocalizer<SharedResource> L) : Controller
{
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
}
