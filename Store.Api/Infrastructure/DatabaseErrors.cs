using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

namespace Store.Api.Infrastructure;

public static class DatabaseErrors
{
    public static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 } ||
        exception.InnerException is SqliteException { SqliteErrorCode: 19 };

    public static bool IsDeadlock(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 1205 } ||
        exception.InnerException is SqliteException { SqliteErrorCode: 5 or 6 };

    public static bool IsDeadlock(Exception exception) =>
        exception is SqlException { Number: 1205 } or SqliteException { SqliteErrorCode: 5 or 6 } ||
        exception is DbUpdateException updateException && IsDeadlock(updateException);
}
