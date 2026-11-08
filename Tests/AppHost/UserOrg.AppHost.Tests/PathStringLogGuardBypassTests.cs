namespace UserOrg.AppHost.Tests;

public sealed class PathStringLogGuardBypassTests
{
    private const string FixtureFile = "Fixture.cs";

    [Fact]
    public void Should_FlagAnInterpolatedPath_When_TheAuthenticationHandlerLoggerPropertyLogsIt()
    {
        const string source = """
            class Fixture : AuthenticationHandler<Options>
            {
                void Handle()
                {
                    Logger.LogDebug($"Handling {Request.Path}");
                }
            }
            """;

        Assert.Equal(["Fixture.cs:5"], OffencesIn(source));
    }

    [Fact]
    public void Should_FlagAnInterpolatedPath_When_ACreateLoggerResultLogsIt()
    {
        const string source = """
            class Fixture
            {
                void Handle(HttpContext context, ILoggerFactory factory)
                {
                    factory.CreateLogger("Fixture").LogWarning($"Blocked {context.Request.Path}");
                }
            }
            """;

        Assert.Equal(["Fixture.cs:5"], OffencesIn(source));
    }

    [Fact]
    public void Should_FlagAnInterpolatedPath_When_AConditionalLoggerLogsIt()
    {
        const string source = """
            class Fixture : ControllerBase
            {
                void Handle(ILogger? logger)
                {
                    logger?.LogDebug($"{Request.PathBase}");
                }
            }
            """;

        Assert.Equal(["Fixture.cs:5"], OffencesIn(source));
    }

    [Fact]
    public void Should_FlagAnInterpolatedMessage_When_ALoggerLogsIt()
    {
        const string source = """
            class Fixture(ILogger<Fixture> _logger)
            {
                void Handle(string user)
                {
                    _logger.LogInformation($"Signed in {user}");
                }
            }
            """;

        Assert.Equal(["Fixture.cs:5"], OffencesIn(source));
    }

    [Fact]
    public void Should_PassAnInterpolatedValue_When_ALiteralTemplatePrecedesIt()
    {
        const string source = """
            class Fixture(ILogger<Fixture> _logger)
            {
                void Handle(DateTime today)
                {
                    _logger.LogWarning("Today {Today}", $"{today:o}");
                }
            }
            """;

        Assert.Empty(OffencesIn(source));
    }

    [Fact]
    public void Should_PassAnInterpolatedValue_When_ItIsATagValue()
    {
        const string source = """
            class Fixture
            {
                void Handle(Activity activity, HttpContext context)
                {
                    activity.SetTag("user", $"u-{context.Request.Path}");
                }
            }
            """;

        Assert.Empty(OffencesIn(source));
    }

    private static IReadOnlyList<string> OffencesIn(string source) =>
        PathStringLogGuard.Offences(FixtureFile, AppHostSource.Parse(source));
}