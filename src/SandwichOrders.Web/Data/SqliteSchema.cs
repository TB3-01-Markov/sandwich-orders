using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace SandwichOrders.Web.Data;

// Development helper for the local SQLite file. EnsureCreated never changes a file that already exists, so after a
// model change (a new table or column) the old file misses it and the app fails with "no such table".
// This compares the file with the model and lists what is missing, e.g. "DataProtectionKeys" or "Orders.CustomerPhone".
public static class SqliteSchema
{
    public static async Task<IReadOnlyList<string>> FindMissingAsync(AppDbContext db, CancellationToken ct = default)
    {
        var missing = new List<string>();
        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            foreach (var entity in db.Model.GetEntityTypes())
            {
                var table = entity.GetTableName();
                if (table is null)
                {
                    continue;
                }

                var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                await using (var command = connection.CreateCommand())
                {
                    // The table names come from our own model, not from users; quotes are doubled anyway.
                    command.CommandText = $"SELECT name FROM pragma_table_info('{table.Replace("'", "''")}')";
                    await using var reader = await command.ExecuteReaderAsync(ct);
                    while (await reader.ReadAsync(ct))
                    {
                        existing.Add(reader.GetString(0));
                    }
                }

                if (existing.Count == 0)
                {
                    missing.Add(table);
                    continue;
                }

                var store = StoreObjectIdentifier.Table(table, entity.GetSchema());
                missing.AddRange(entity.GetProperties()
                    .Select(property => property.GetColumnName(store))
                    .Where(column => column is not null && !existing.Contains(column))
                    .Select(column => $"{table}.{column}"));
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        return missing;
    }
}
