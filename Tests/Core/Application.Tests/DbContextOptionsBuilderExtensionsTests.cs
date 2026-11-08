using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;

using UserOrg.Shared.Persistance;

namespace UserOrganizationManagement.Application.Tests;

/// <summary>
/// Unit tests for <see cref="DbContextOptionsBuilderExtensions.ConfigureSqlCommandLogging"/> (issue #241):
/// the SQL command log events must be suppressed at the EF source when logging is disabled.
/// </summary>
public sealed class DbContextOptionsBuilderExtensionsTests
{
    private static WarningBehavior? BehaviorOf(DbContextOptionsBuilder options, EventId eventId)
        => options.Options.FindExtension<CoreOptionsExtension>()?.WarningsConfiguration.GetBehavior(eventId);

    [Fact(DisplayName = "ConfigureSqlCommandLogging(false) ignores CommandExecuting and CommandExecuted")]
    public void ConfigureSqlCommandLogging_Disabled_IgnoresCommandExecutionEvents()
    {
        var options = new DbContextOptionsBuilder().ConfigureSqlCommandLogging(enabled: false);

        Assert.Equal(WarningBehavior.Ignore, BehaviorOf(options, RelationalEventId.CommandExecuting));
        Assert.Equal(WarningBehavior.Ignore, BehaviorOf(options, RelationalEventId.CommandExecuted));
    }

    [Fact(DisplayName = "ConfigureSqlCommandLogging(true) leaves command execution events unsuppressed")]
    public void ConfigureSqlCommandLogging_Enabled_DoesNotIgnoreCommandExecutionEvents()
    {
        var options = new DbContextOptionsBuilder().ConfigureSqlCommandLogging(enabled: true);

        Assert.NotEqual(WarningBehavior.Ignore, BehaviorOf(options, RelationalEventId.CommandExecuting));
        Assert.NotEqual(WarningBehavior.Ignore, BehaviorOf(options, RelationalEventId.CommandExecuted));
    }

    [Fact(DisplayName = "ConfigureSqlCommandLogging(false) leaves CommandError diagnostics enabled")]
    public void ConfigureSqlCommandLogging_Disabled_DoesNotIgnoreCommandError()
    {
        var options = new DbContextOptionsBuilder().ConfigureSqlCommandLogging(enabled: false);

        Assert.NotEqual(WarningBehavior.Ignore, BehaviorOf(options, RelationalEventId.CommandError));
    }

    [Fact(DisplayName = "ConfigureSqlCommandLogging returns the same builder instance")]
    public void ConfigureSqlCommandLogging_ReturnsSameBuilder()
    {
        var options = new DbContextOptionsBuilder();

        var result = options.ConfigureSqlCommandLogging(enabled: false);

        Assert.Same(options, result);
    }
}
