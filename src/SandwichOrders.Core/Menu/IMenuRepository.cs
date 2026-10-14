namespace SandwichOrders.Core.Menu;

public interface IMenuRepository
{
    // Active sandwiches ordered by number, with their ingredients loaded.
    Task<IReadOnlyList<Sandwich>> GetActiveAsync(CancellationToken ct = default);

    // Every sandwich (also removed ones) ordered by number, with ingredients loaded. For the canteen employee.
    Task<IReadOnlyList<Sandwich>> GetAllAsync(CancellationToken ct = default);

    // One sandwich with its ingredients loaded, or null.
    Task<Sandwich?> GetAsync(int id, CancellationToken ct = default);

    Task<IReadOnlyList<Ingredient>> GetIngredientsAsync(CancellationToken ct = default);

    Task<Ingredient?> GetIngredientAsync(int id, CancellationToken ct = default);

    void Add(Sandwich sandwich);

    void Add(Ingredient ingredient);

    Task SaveChangesAsync(CancellationToken ct = default);
}
