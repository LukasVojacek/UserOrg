using System.Data.Common;

using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Core.DataAccess.Diagnostics;

/// <summary>
/// Names the query, its tag and the configured timeout when a read stalls behind an open writer's lock and
/// then fails on the command timeout (D2a). Every query already carries a <c>TagWith</c>/<c>TagWithCallSite</c>
/// comment (<see cref="Core.DataAccess.Queries.DataQuery{T}"/>),
/// so the tag is always present in <see cref="DbCommand.CommandText"/> — this interceptor pulls it back out at
/// the one point a stalled read becomes an observable failure instead of an opaque 300 s hang.
/// </summary>
/// <remarks>
/// This only observes: <see cref="Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor"/>'s
/// <c>CommandFailed</c>/<c>CommandFailedAsync</c> hooks cannot suppress the exception EF is already about to
/// throw — the original failure always reaches the caller unchanged, this only adds the diagnostic entry
/// alongside it.
/// </remarks>
internal sealed class BlockedReadDiagnosticsInterceptor(
    IBlockedReadClassifier classifier,
    ILogger<BlockedReadDiagnosticsInterceptor> logger) : DbCommandInterceptor
{
    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData) =>
        HandleCommandFailureAsync(command, eventData.Exception, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

    public override async Task CommandFailedAsync(
        DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default) =>
        await HandleCommandFailureAsync(command, eventData.Exception, cancellationToken);

    /// <summary>
    /// The testable core of the interceptor — kept independent of <see cref="CommandErrorEventData"/> (which
    /// has no public constructor) so a test can drive it directly with a fake <see cref="DbCommand"/> and a
    /// plain <see cref="Exception"/>. Stays a <see cref="Task"/>-returning method (rather than synchronous)
    /// to keep the call sites unchanged even though nothing in the body awaits anymore, now that the
    /// DMV blocking-session lookup is gone.
    /// </summary>
    internal Task HandleCommandFailureAsync(DbCommand command, Exception exception, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(exception);

        if (!classifier.IsLockWaitOrCommandTimeout(exception))
        {
            return Task.CompletedTask;
        }

        var tag = ExtractQueryTag(command.CommandText);

        // Error: this is the exact opaque-hang incident the task exists to fix — nobody could previously tell
        // which query stalled. Somebody opens a ticket on this the first time it fires again. No blocking
        // session id is logged here: by the time this handler runs, the failed command has already finished,
        // so sys.dm_exec_requests.blocking_session_id would only ever report 0 ("not blocked") or match this
        // lookup's own command — never the real blocker. Sampling the real blocker needs a second connection
        // with its own short LOCK_TIMEOUT while the read is still waiting, which is a separate, larger design
        // (out of scope here).
        logger.LogError(
            exception,
            "Read blocked and timed out after {CommandTimeoutSeconds}s. Query: {QueryTag}. Command: {CommandText}",
            command.CommandTimeout,
            tag ?? "(untagged)",
            command.CommandText);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Pulls the <c>TagWith</c> tag back out of the leading SQL comment EF Core generates for it — the first
    /// <c>--</c> line, before <c>TagWithCallSite</c>'s file/line comment.
    /// </summary>
    private static string? ExtractQueryTag(string? commandText)
    {
        if (string.IsNullOrEmpty(commandText))
        {
            return null;
        }

        using var reader = new StringReader(commandText);
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith("--", StringComparison.Ordinal))
            {
                return line[2..].Trim();
            }

            if (!string.IsNullOrWhiteSpace(line))
            {
                break;
            }
        }

        return null;
    }
}