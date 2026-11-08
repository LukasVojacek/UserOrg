using Microsoft.Extensions.Logging;

namespace UserOrg.ServiceDefaults.Tests.Logging;

/// <summary>
/// The pre-error buffer is on (ADR 0112, StepUpLogging 5.0.2) and flushes only on an Error in the trace: disposing
/// the host flushes nothing, so a Warning is exported exactly once and held Information is never exported at
/// shutdown (QS-05).
/// </summary>
[Collection(LoggingPipelineCollection.Name)]
public sealed class PreErrorBufferDisposeTests
{
    private const string OurCategory = "UserOrg.Core.Probe";

    [Fact(DisplayName = "Should_ExportAWarningExactlyOnce_And_NoHeldInformation_After_TheHostIsDisposed")]
    public async Task Should_ExportAWarningExactlyOnce_And_NoHeldInformation_After_TheHostIsDisposed()
    {
        await using var host = LoggingPipelineHost.Build();
        Assert.True(host.StepUpOptions.EnablePreErrorBuffering);
        string warning;
        string information;
        using (new System.Diagnostics.Activity("buffered-request").Start())
        {
            warning = host.Log(OurCategory, LogLevel.Warning);
            information = host.Log(OurCategory, LogLevel.Information);
        }

        Assert.Equal(1, host.CountOf(warning));

        await host.App.DisposeAsync();

        Assert.Equal(1, host.CountOf(warning));
        Assert.Equal(0, host.CountOf(information));
    }
}