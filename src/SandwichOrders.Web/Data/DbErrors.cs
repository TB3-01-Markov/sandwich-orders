using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace SandwichOrders.Web.Data;

// Recognises "a unique index refused this row" for both supported databases.
public static class DbErrors
{
    private const int SqliteConstraint = 19;
    private const int SqliteUnique = 2067;      // SQLITE_CONSTRAINT_UNIQUE
    private const int SqlitePrimaryKey = 1555;  // SQLITE_CONSTRAINT_PRIMARYKEY
    private const int SqlServerUniqueIndex = 2601;
    private const int SqlServerUniqueConstraint = 2627;

    public static bool IsUniqueViolation(DbUpdateException exception) => exception.InnerException switch
    {
        SqliteException sqlite => sqlite.SqliteErrorCode == SqliteConstraint
                                  && sqlite.SqliteExtendedErrorCode is SqliteUnique or SqlitePrimaryKey,
        SqlException sqlServer => sqlServer.Number is SqlServerUniqueIndex or SqlServerUniqueConstraint,
        _ => false
    };
}
