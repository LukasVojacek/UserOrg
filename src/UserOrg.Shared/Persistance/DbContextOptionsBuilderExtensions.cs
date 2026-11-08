using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace UserOrg.Shared.Persistance;

/// <summary>
/// Extension methods for configuring diagnostic behaviour on a <see cref="DbContextOptionsBuilder"/>.
/// </summary>
public static class DbContextOptionsBuilderExtensions
{
    /// <summary>
    /// Controls whether EF Core emits the executed-SQL log events
    /// (<see cref="RelationalEventId.CommandExecuting"/> and <see cref="RelationalEventId.CommandExecuted"/>).
    /// </summary>
    /// <remarks>
    /// When <paramref name="enabled"/> is <see langword="false"/> the two command-execution events are ignored
    /// via <see cref="WarningsConfigurationBuilder.Ignore(EventId[])"/>, so EF never formats the SQL string and
    /// no logger ever observes the event.
    /// <para>
    /// This is deliberately suppression <em>at the EF source</em> rather than a log-level filter: a
    /// <c>Logging:LogLevel</c>/Serilog <c>MinimumLevel.Override</c> only filters records EF has already produced,
    /// and a <c>Lukdrasil.StepUpLogging</c> step-up can lower the effective minimum level back to
    /// <c>Information</c> for a request and resurrect the SQL. An ignored event cannot be resurrected by any
    /// override, step-up or future log sink. See ADR-0076 (issue #241).
    /// </para>
    /// <para>
    /// <see cref="RelationalEventId.CommandError"/> is intentionally left enabled: failing commands are rare and
    /// their SQL is the primary diagnostic for a production incident.
    /// </para>
    /// </remarks>
    /// <param name="options">The options builder to configure. Cannot be <see langword="null"/>.</param>
    /// <param name="enabled">
    /// <see langword="true"/> to leave SQL command logging untouched; <see langword="false"/> to suppress it.
    /// </param>
    /// <returns>The same <paramref name="options"/> instance, to allow chaining.</returns>
    public static DbContextOptionsBuilder ConfigureSqlCommandLogging(
        this DbContextOptionsBuilder options, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (enabled)
        {
            return options;
        }

        return options.ConfigureWarnings(w => w.Ignore(
            RelationalEventId.CommandExecuting,
            RelationalEventId.CommandExecuted));
    }
}