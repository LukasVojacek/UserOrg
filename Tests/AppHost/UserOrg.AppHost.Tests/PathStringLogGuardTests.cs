namespace UserOrg.AppHost.Tests;

public sealed class PathStringLogGuardTests
{
    private const string FixtureFile = "Fixture.cs";

    private static readonly string InternalTokenTransformFile = Path.Combine(
        "src", "Modules", "Services", "Gateway", "UserOrg.Gateway", "Startup", "InternalTokenTransformExtensions.cs");

    [Fact]
    public void Should_FlagAPathInAnInterpolationHole()
    {
        const string source = """
            class Fixture
            {
                void Handle(HttpContext context, ILogger logger)
                {
                    logger.LogInformation($"Request to {context.Request.Path} failed");
                }
            }
            """;

        Assert.Equal(["Fixture.cs:5"], OffencesIn(source));
    }

    [Fact]
    public void Should_FlagAPathValueConcatenatedIntoTheTemplate()
    {
        const string source = """
            class Fixture(ILogger<Fixture> _logger)
            {
                void Handle(HttpContext context)
                {
                    _logger.LogWarning("Blocked " + context.Request.Path.Value);
                }
            }
            """;

        Assert.Equal(["Fixture.cs:5"], OffencesIn(source));
    }

    [Fact]
    public void Should_FlagAPathBaseConcatenatedIntoTheTemplate()
    {
        const string source = """
            class Fixture
            {
                void Handle(HttpContext context, ILogger logger)
                {
                    logger.Log(LogLevel.Error, "Failed under " + context.Request.PathBase + " for {User}", "u-1");
                }
            }
            """;

        Assert.Equal(["Fixture.cs:5"], OffencesIn(source));
    }

    [Fact]
    public void Should_FlagAQueryStringConcatenatedIntoTheTemplate()
    {
        const string source = """
            class Fixture(ILogger<Fixture> _logger)
            {
                void Handle(HttpContext context)
                {
                    _logger.LogDebug("Query " + context.Request.QueryString);
                }
            }
            """;

        Assert.Equal(["Fixture.cs:5"], OffencesIn(source));
    }

    [Fact]
    public void Should_PassAPathStringHandedToTheLoggerAsAnArgument()
    {
        const string source = """
            class Fixture(ILogger<Fixture> _logger)
            {
                void Handle(HttpContext context)
                {
                    var path = context.Request.Path;
                    _logger.LogWarning("Blocked {Path} under {PathBase}", path, context.Request.PathBase);
                    _logger.HttpRequest(context.Request.Path);
                    Log.Foo(_logger, context.Request.Path);
                }
            }
            """;

        Assert.Empty(OffencesIn(source));
    }

    [Fact]
    public void Should_PassAConcatenatedTemplateThatCarriesNoRequestValue()
    {
        const string source = """
            class Fixture(ILogger<Fixture> _logger)
            {
                void Handle(HttpContext context)
                {
                    _logger.LogWarning("Blocked " + "{Path}", context.Request.Path.Value);
                }
            }
            """;

        Assert.Empty(OffencesIn(source));
    }

    [Fact]
    public void Should_PassAPathHandedToACallThatIsNotALogger()
    {
        const string source = """
            class Fixture
            {
                void Handle(HttpContext httpContext, RouteSurface routeSurface)
                {
                    if (routeSurface.Evaluate(httpContext.Request.Path) is not ERouteExistence.NotServed)
                    {
                        return;
                    }
                }
            }
            """;

        Assert.Empty(OffencesIn(source));
    }

    [Fact]
    public void Should_ListEveryOffendingLine()
    {
        const string source = """
            class Fixture(ILogger<Fixture> _logger)
            {
                void Handle(HttpContext context)
                {
                    _logger.LogDebug("Path='{Path}'", context.Request.Path);
                    _logger.LogDebug($"Path='{context.Request.Path}'");
                    _logger.LogDebug("Path='{Path}'", context.Request.Path.Value);
                    _logger.LogDebug(
                        "PathBase=" + context.Request.PathBase);
                }
            }
            """;

        Assert.Equal(["Fixture.cs:6", "Fixture.cs:9"], OffencesIn(source));
    }

    [Fact]
    public void Should_ReadEverySourceFileUnderSrc()
    {
        Assert.True(
            RepositorySource.AllTrees.ContainsKey(InternalTokenTransformFile),
            $"RepositorySource.AllTrees does not hold {InternalTokenTransformFile}; it holds {RepositorySource.AllTrees.Count} files");

        Assert.Contains(RepositorySource.AllTrees.Keys, file => file.StartsWith(Path.Combine("src", "UserOrg.AppHost"), StringComparison.Ordinal));

        Assert.DoesNotContain(
            RepositorySource.AllTrees.Keys,
            file => file.Split(Path.DirectorySeparatorChar) is var segments
                && (segments.Contains("bin") || segments.Contains("obj") || segments.Contains("artifacts")));
    }

    [Fact]
    public void Should_InterpolateNoRequestPathIntoALogTemplateInSrc()
    {
        var offences = RepositorySource.AllTrees
            .OrderBy(file => file.Key, StringComparer.Ordinal)
            .SelectMany(file => PathStringLogGuard.Offences(file.Key, file.Value))
            .ToList();

        Assert.True(
            offences.Count == 0,
            $"{offences.Count} log calls build their message template from an interpolated or concatenated value, which the property redaction sweep never reaches; pass the value as a template argument instead at:{Environment.NewLine}{string.Join(Environment.NewLine, offences)}");
    }

    private static IReadOnlyList<string> OffencesIn(string source) =>
        PathStringLogGuard.Offences(FixtureFile, AppHostSource.Parse(source));
}