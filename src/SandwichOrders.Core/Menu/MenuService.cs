using SandwichOrders.Core.Orders;

namespace SandwichOrders.Core.Menu;

public class MenuService(IMenuRepository menu)
{
    public Task<IReadOnlyList<Sandwich>> GetMenuAsync(CancellationToken ct = default) =>
        menu.GetActiveAsync(ct);

    public Task<IReadOnlyList<Sandwich>> GetAllSandwichesAsync(CancellationToken ct = default) =>
        menu.GetAllAsync(ct);

    public Task<Sandwich?> GetSandwichAsync(int sandwichId, CancellationToken ct = default) =>
        menu.GetAsync(sandwichId, ct);

    public Task<IReadOnlyList<Ingredient>> GetIngredientsAsync(CancellationToken ct = default) =>
        menu.GetIngredientsAsync(ct);

    // US-08: the canteen employee switches a sandwich off or on.
    public async Task SetTemporarilyUnavailableAsync(int sandwichId, bool unavailable, CancellationToken ct = default)
    {
        var sandwich = await menu.GetAsync(sandwichId, ct)
                       ?? throw new OrderingException(OrderingError.SandwichNotFound);

        sandwich.IsTemporarilyUnavailable = unavailable;
        await menu.SaveChangesAsync(ct);
    }

    // US-13: an ingredient ran out, so every sandwich that contains it becomes unavailable at once.
    public async Task SetIngredientAvailableAsync(int ingredientId, bool available, CancellationToken ct = default)
    {
        var ingredient = await menu.GetIngredientAsync(ingredientId, ct)
                         ?? throw new OrderingException(OrderingError.IngredientNotFound);

        ingredient.IsAvailable = available;
        await menu.SaveChangesAsync(ct);
    }

    // Creates (Id is null) or updates a sandwich. All rules are checked here, before anything is stored.
    public async Task<SavedSandwich> SaveSandwichAsync(SandwichInput input, CancellationToken ct = default)
    {
        var nameEn = (input.NameEn ?? "").Trim();
        var nameNl = (input.NameNl ?? "").Trim();
        if (nameEn.Length is 0 or > MenuLimits.MaxNameLength || nameNl.Length is 0 or > MenuLimits.MaxNameLength)
        {
            throw new MenuException(MenuError.NameRequired);
        }

        var descriptionEn = (input.DescriptionEn ?? "").Trim();
        var descriptionNl = (input.DescriptionNl ?? "").Trim();
        if (descriptionEn.Length > MenuLimits.MaxDescriptionLength || descriptionNl.Length > MenuLimits.MaxDescriptionLength)
        {
            throw new MenuException(MenuError.NameRequired);
        }

        if (input.Price <= 0 || input.Price > MenuLimits.MaxPrice || decimal.Round(input.Price, 2) != input.Price)
        {
            throw new MenuException(MenuError.InvalidPrice);
        }

        var all = await menu.GetAllAsync(ct);
        if (input.Number < 1 || all.Any(s => s.Number == input.Number && s.Id != (input.Id ?? 0)))
        {
            throw new MenuException(MenuError.NumberTaken);
        }

        var ingredientIds = input.IngredientIds.Distinct().ToList();
        var known = (await menu.GetIngredientsAsync(ct)).Select(i => i.Id).ToHashSet();
        if (ingredientIds.Any(id => !known.Contains(id)))
        {
            throw new MenuException(MenuError.UnknownIngredient);
        }

        int PortionsFor(int ingredientId) =>
            input.Portions is not null && input.Portions.TryGetValue(ingredientId, out var portions) ? portions : 1;

        if (ingredientIds.Any(id => PortionsFor(id) is < 1 or > SandwichIngredient.MaxPortions))
        {
            throw new MenuException(MenuError.InvalidPortions);
        }

        if (!Enum.IsDefined(input.Kind) || input.StockCount is < 0 or > MenuLimits.MaxStock)
        {
            throw new MenuException(MenuError.InvalidStock);
        }

        Sandwich sandwich;
        if (input.Id is null)
        {
            sandwich = new Sandwich();
            menu.Add(sandwich);
        }
        else
        {
            sandwich = await menu.GetAsync(input.Id.Value, ct) ?? throw new MenuException(MenuError.SandwichNotFound);
        }

        sandwich.Number = input.Number;
        sandwich.NameEn = nameEn;
        sandwich.NameNl = nameNl;
        sandwich.DescriptionEn = descriptionEn;
        sandwich.DescriptionNl = descriptionNl;
        sandwich.Price = input.Price;
        sandwich.IsActive = input.IsActive;
        sandwich.Kind = input.Kind;

        // Only a supplied sandwich counts pieces; a prepared one is limited by its ingredients.
        var stock = input.Kind == SandwichKind.Supplied ? input.StockCount : null;
        if (sandwich.StockCount != stock)
        {
            sandwich.SetStock(stock);
        }

        string? oldImage = null;
        if (!input.KeepImage)
        {
            oldImage = sandwich.ImagePath;
            sandwich.ImagePath = input.ImagePath;
        }

        // Remove ingredients that were unticked, add the new ones. Only the ids are used, so nothing is inserted twice.
        foreach (var link in sandwich.SandwichIngredients.Where(si => !ingredientIds.Contains(si.IngredientId)).ToList())
        {
            sandwich.SandwichIngredients.Remove(link);
        }

        foreach (var link in sandwich.SandwichIngredients)
        {
            link.Portions = PortionsFor(link.IngredientId);
        }

        var current = sandwich.SandwichIngredients.Select(si => si.IngredientId).ToHashSet();
        foreach (var id in ingredientIds.Where(id => !current.Contains(id)))
        {
            sandwich.SandwichIngredients.Add(new SandwichIngredient { IngredientId = id, Portions = PortionsFor(id) });
        }

        await menu.SaveChangesAsync(ct);
        return new SavedSandwich(sandwich, oldImage);
    }

    // A sandwich that was ordered before can never be deleted (old orders refer to it), so it is taken off the menu instead.
    public async Task SetActiveAsync(int sandwichId, bool active, CancellationToken ct = default)
    {
        var sandwich = await menu.GetAsync(sandwichId, ct) ?? throw new MenuException(MenuError.SandwichNotFound);
        sandwich.IsActive = active;
        await menu.SaveChangesAsync(ct);
    }

    // The canteen employee counts what is in the fridge. null = do not count this ingredient.
    public async Task SetIngredientStockAsync(int ingredientId, int? portions, CancellationToken ct = default)
    {
        if (portions is < 0 or > MenuLimits.MaxStock)
        {
            throw new MenuException(MenuError.InvalidStock);
        }

        var ingredient = await menu.GetIngredientAsync(ingredientId, ct)
                         ?? throw new MenuException(MenuError.IngredientNotFound);
        ingredient.SetStock(portions);
        await menu.SaveChangesAsync(ct);
    }

    // A delivery arrived (or was counted again) for a supplied sandwich.
    public async Task SetSandwichStockAsync(int sandwichId, int? count, CancellationToken ct = default)
    {
        if (count is < 0 or > MenuLimits.MaxStock)
        {
            throw new MenuException(MenuError.InvalidStock);
        }

        var sandwich = await menu.GetAsync(sandwichId, ct) ?? throw new MenuException(MenuError.SandwichNotFound);
        if (sandwich.Kind != SandwichKind.Supplied)
        {
            throw new MenuException(MenuError.InvalidStock);
        }

        sandwich.SetStock(count);
        await menu.SaveChangesAsync(ct);
    }

    // stockPortions: null = the stock of this ingredient is not counted.
    public async Task AddIngredientAsync(string nameEn, string nameNl, int? stockPortions = null, CancellationToken ct = default)
    {
        nameEn = (nameEn ?? "").Trim();
        nameNl = (nameNl ?? "").Trim();
        if (nameEn.Length is 0 or > MenuLimits.MaxNameLength || nameNl.Length is 0 or > MenuLimits.MaxNameLength)
        {
            throw new MenuException(MenuError.IngredientNameRequired);
        }

        var existing = await menu.GetIngredientsAsync(ct);
        if (existing.Any(i => string.Equals(i.NameEn, nameEn, StringComparison.OrdinalIgnoreCase)))
        {
            throw new MenuException(MenuError.IngredientExists);
        }

        if (stockPortions is < 0 or > MenuLimits.MaxStock)
        {
            throw new MenuException(MenuError.InvalidStock);
        }

        var ingredient = new Ingredient { NameEn = nameEn, NameNl = nameNl };
        if (stockPortions is not null)
        {
            ingredient.SetStock(stockPortions);
        }

        menu.Add(ingredient);
        await menu.SaveChangesAsync(ct);
    }
}
