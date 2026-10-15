using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SandwichOrders.Core;
using SandwichOrders.Core.Announcements;
using SandwichOrders.Core.Menu;
using SandwichOrders.Core.Orders;
using SandwichOrders.Web.Models;

namespace SandwichOrders.Web.Data;

public class EfMenuRepository(AppDbContext db) : IMenuRepository
{
    private IQueryable<Sandwich> WithIngredients =>
        db.Sandwiches.Include(s => s.SandwichIngredients).ThenInclude(si => si.Ingredient);

    public async Task<IReadOnlyList<Sandwich>> GetActiveAsync(CancellationToken ct = default) =>
        await WithIngredients.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.Number).ToListAsync(ct);

    public async Task<IReadOnlyList<Sandwich>> GetAllAsync(CancellationToken ct = default) =>
        await WithIngredients.AsNoTracking().OrderBy(s => s.Number).ToListAsync(ct);

    public Task<Sandwich?> GetAsync(int id, CancellationToken ct = default) =>
        WithIngredients.FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<IReadOnlyList<Ingredient>> GetIngredientsAsync(CancellationToken ct = default) =>
        await db.Ingredients.AsNoTracking().OrderBy(i => i.NameEn).ToListAsync(ct);

    public Task<Ingredient?> GetIngredientAsync(int id, CancellationToken ct = default) =>
        db.Ingredients.FirstOrDefaultAsync(i => i.Id == id, ct);

    public void Add(Sandwich sandwich) => db.Sandwiches.Add(sandwich);

    public void Add(Ingredient ingredient) => db.Ingredients.Add(ingredient);

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // An order changed the same stock while the employee was saving. Nothing is stored; reload and retry.
            db.ChangeTracker.Clear();
            throw new MenuException(MenuError.StockChanged);
        }
        catch (DbUpdateException exception) when (DbErrors.IsUniqueViolation(exception))
        {
            // MenuService checks names and numbers first, but two employees saving at the same moment can both pass
            // that check. The unique indexes then refuse the second save; show a message instead of an error page.
            var ingredientRefused = db.ChangeTracker.Entries<Ingredient>()
                .Any(e => e.State == EntityState.Added);
            db.ChangeTracker.Clear();
            throw new MenuException(ingredientRefused ? MenuError.IngredientExists : MenuError.NumberTaken);
        }
    }
}

public class EfOrderRepository(AppDbContext db) : IOrderRepository
{
    public Task<Order?> GetActiveForCustomerAsync(string customerId, DateOnly date, CancellationToken ct = default) =>
        db.Orders
            .Include(o => o.Lines).ThenInclude(l => l.Sandwich)
            .FirstOrDefaultAsync(
                o => o.CustomerId == customerId && o.OrderDate == date && o.Status != OrderStatus.Cancelled,
                ct);

    public async Task<IReadOnlyList<Order>> GetActiveForDateAsync(DateOnly date, CancellationToken ct = default) =>
        await db.Orders.AsNoTracking()
            .Include(o => o.Lines).ThenInclude(l => l.Sandwich)
            .Where(o => o.OrderDate == date && o.Status != OrderStatus.Cancelled)
            .OrderBy(o => o.CustomerName)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Order>> GetForCustomerBeforeAsync(
        string customerId, DateOnly before, int max, CancellationToken ct = default) =>
        await db.Orders.AsNoTracking()
            .Include(o => o.Lines).ThenInclude(l => l.Sandwich)
            .Where(o => o.CustomerId == customerId && o.OrderDate < before)
            .OrderByDescending(o => o.OrderDate)
            .Take(max)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Order>> GetForPeriodAsync(DateOnly from, DateOnly to, CancellationToken ct = default) =>
        await db.Orders.AsNoTracking()
            .Include(o => o.Lines)
            .Where(o => o.OrderDate >= from && o.OrderDate <= to)
            .ToListAsync(ct);

    public Task<Order?> GetAsync(int id, CancellationToken ct = default) =>
        db.Orders
            .Include(o => o.Lines).ThenInclude(l => l.Sandwich)
            .ThenInclude(s => s.SandwichIngredients).ThenInclude(si => si.Ingredient)
            .AsSplitQuery() // two collections (lines, ingredients) in one query would multiply the rows
            .FirstOrDefaultAsync(o => o.Id == id, ct);

    public void Add(Order order) => db.Orders.Add(order);

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone else changed the same stock between our read and our save (e.g. took the last portion).
            // Forget our changes so this request leaves nothing half-done, and let the user try again.
            db.ChangeTracker.Clear();
            throw new OrderingException(OrderingError.StockChanged);
        }
        catch (DbUpdateException exception) when (DbErrors.IsUniqueViolation(exception))
        {
            // The only unique index on orders: one active order per customer per day (see AppDbContext).
            // The stock taken in the same save is not stored either.
            db.ChangeTracker.Clear();
            throw new OrderingException(OrderingError.AlreadyOrdered);
        }
    }
}

public class EfAnnouncementRepository(AppDbContext db) : IAnnouncementRepository
{
    public async Task<IReadOnlyList<Announcement>> GetAllAsync(CancellationToken ct = default) =>
        await db.Announcements.AsNoTracking()
            .OrderByDescending(a => a.PeriodStart)
            .ToListAsync(ct);

    public Task<Announcement?> GetAsync(int id, CancellationToken ct = default) =>
        db.Announcements.FirstOrDefaultAsync(a => a.Id == id, ct);

    public void Add(Announcement announcement) => db.Announcements.Add(announcement);

    public void Remove(Announcement announcement) => db.Announcements.Remove(announcement);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

public class EfOrderingSettings(AppDbContext db) : IOrderingSettings
{
    private const string DeadlineKey = "OrderDeadline";
    private const string TimeFormat = "HH:mm";

    public async Task<TimeOnly> GetDeadlineAsync(CancellationToken ct = default)
    {
        var setting = await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == DeadlineKey, ct);

        return setting is not null
               && TimeOnly.TryParseExact(setting.Value, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var deadline)
            ? deadline
            : OrderingRules.DefaultDeadline;
    }

    public async Task SetDeadlineAsync(TimeOnly deadline, CancellationToken ct = default)
    {
        var value = deadline.ToString(TimeFormat, CultureInfo.InvariantCulture);
        var setting = await db.Settings.FirstOrDefaultAsync(s => s.Key == DeadlineKey, ct);

        if (setting is null)
        {
            db.Settings.Add(new AppSetting { Key = DeadlineKey, Value = value });
        }
        else
        {
            setting.Value = value;
        }

        await db.SaveChangesAsync(ct);
    }
}
