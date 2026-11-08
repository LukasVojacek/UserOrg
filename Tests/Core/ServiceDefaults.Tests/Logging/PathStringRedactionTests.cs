using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

using Serilog.Events;

namespace UserOrg.ServiceDefaults.Tests.Logging;

/// <summary>
/// A request path or path base handed to a logger as a <see cref="PathString"/> argument is swept by
/// RedactLogEventProperties (StepUpLogging #34), so a public token in it reaches no exported event in Normal or in
/// Diagnostic mode. This replaces the scalar-argument cases the PathString log guard no longer checks.
/// </summary>
[Collection(LoggingPipelineCollection.Name)]
public sealed class PathStringRedactionTests
{
    private const string Template = "Public request {Path} under {PathBase}";

    [Theory(DisplayName = "Should_ExportThePathStringEventWithoutTheToken_InEveryMode")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_ExportThePathStringEventWithoutTheToken_InEveryMode(bool diagnostic)
    {
        await using var host = LoggingPipelineHost.Build(diagnostic ? LoggingPipelineHost.Diagnostic() : null);
        var level = diagnostic ? LogLevel.Debug : LogLevel.Warning;
        if (diagnostic)
        {
            Assert.Equal(LogEventLevel.Debug, host.Controller.LevelSwitch.MinimumLevel);
        }

        host.LoggerFactory.CreateLogger("UserOrg.Core.Probe").Log(
            level,
            Template,
            new PathString($"/api/v2/Public/ActionLinks/{PublicTokenPaths.Token}"),
            new PathString($"/api/Public/Handle/{PublicTokenPaths.Token}"));

        var logged = Assert.Single(host.Events, e => e.MessageTemplate.Text == Template);
        Assert.DoesNotContain(PublicTokenPaths.Token, RequestPipelineHost.TextOf(logged), StringComparison.OrdinalIgnoreCase);
    }
}