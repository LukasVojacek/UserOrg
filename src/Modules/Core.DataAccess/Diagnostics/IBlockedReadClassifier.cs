namespace Core.DataAccess.Diagnostics;

/// <summary>
/// Decides whether a command failure is the specific shape this diagnostic cares about — a reader stalled
/// behind an open writer's lock and then either hit the client-side <see cref="System.Data.Common.DbCommand.CommandTimeout"/>
/// or SQL Server's own lock-wait timeout — as opposed to any other command failure (bad SQL, constraint
/// violation, connection refused, …) that already has its own handling elsewhere.
/// </summary>
internal interface IBlockedReadClassifier
{
    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="exception"/> is a lock-wait or command-timeout
    /// failure worth naming the query, its tag and its configured timeout for.
    /// </summary>
    bool IsLockWaitOrCommandTimeout(Exception exception);
}