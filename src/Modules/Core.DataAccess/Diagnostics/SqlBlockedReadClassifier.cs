using Microsoft.Data.SqlClient;

namespace Core.DataAccess.Diagnostics;

/// <summary>
/// SQL Server's shape of a blocked read: error -2 is the client-side command timeout (the driver gave up
/// waiting for a reply — the case that used to stall for the full 300 s), 1222 is SQL Server's own
/// "Lock request time out period exceeded" raised when a query's own internal LOCK_TIMEOUT elapses.
/// </summary>
internal sealed class SqlBlockedReadClassifier : IBlockedReadClassifier
{
    public bool IsLockWaitOrCommandTimeout(Exception exception) =>
        exception is SqlException { Number: -2 or 1222 };
}