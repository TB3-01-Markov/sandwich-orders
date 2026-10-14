using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SandwichOrders.Core;
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

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
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

    public void Add(Order order) => db.Orders.Add(order);

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
