namespace UserOrg.AppHost.Tests;

public sealed class AppHostGateSyntaxSelfTests
{
    private const string FakeExtensions = """
        namespace Fake;

        internal static class Fake
        {
            internal static void Wire(IResourceBuilder<ProjectResource> api, IResourceBuilder<ParameterResource> tokenExchangeEnabled, bool askServiceAccount)
            {
                if (!builder.ExecutionContext.IsRunMode)
                {
                    api.WithEnvironment("Published", tokenExchangeEnabled);
                    return;
                }

                api.WithEnvironment(async context =>
                {
                    var enabled = NexusApiModes.ParseTokenExchangeEnabled(await tokenExchangeEnabled.Resource.GetValueAsync(context.CancellationToken));
                    context.EnvironmentVariables["Always"] = "a";

                    if (enabled)
                    {
                        context.EnvironmentVariables["ThenBranch"] = "b";
                    }

                    if (!enabled || other)
                    {
                        context.EnvironmentVariables["EitherBranch"] = "c";
                    }

                    context.EnvironmentVariables["Conditional"] = enabled ? "on" : "off";
                    var check = enabled && string.IsNullOrWhiteSpace("AndRight");
                    var loose = enabled || string.IsNullOrWhiteSpace("OrRight");

                    if (!enabled)
                    {
                        return;
                    }

                    context.EnvironmentVariables["AfterEarlyReturn"] = "d";
                });

                var inputs = askServiceAccount ? new[] { "Asked" } : [];
                var named = "NotAsked";
                var prompted = Prompt(inputs);
                if (prompted.Saved)
                {
                    var saved = "SavedWhateverAsked";
                }

                var asked = askServiceAccount;
                if (asked)
                {
                    var throughLocal = "AskedThroughLocal";
                }
            }
        }
        """;

    private static readonly CompilationUnitSyntax Root = (CompilationUnitSyntax)AppHostSource.Parse(FakeExtensions).GetRoot();

    [Theory]
    [InlineData("ThenBranch")]
    [InlineData("AfterEarlyReturn")]
    [InlineData("on")]
    [InlineData("AndRight")]
    public void Should_FindTheFlagGate(string literal) =>
        Assert.NotNull(Literal(literal).TokenExchangeGateOf());

    [Theory]
    [InlineData("Always")]
    [InlineData("EitherBranch")]
    [InlineData("off")]
    [InlineData("OrRight")]
    [InlineData("Published")]
    public void Should_NotTakeAnUnrelatedConditionForTheFlagGate(string literal) =>
        Assert.Null(Literal(literal).TokenExchangeGateOf());

    [Fact]
    public void Should_FindTheGateOfANamedArgument()
    {
        Assert.NotNull(Literal("Asked").GateOf(expression => expression.ReadsWord("askServiceAccount")));
        Assert.NotNull(Literal("AskedThroughLocal").GateOf(expression => expression.ReadsWord("askServiceAccount")));
        Assert.Null(Literal("NotAsked").GateOf(expression => expression.ReadsWord("askServiceAccount")));
    }

    [Fact]
    public void Should_NotTraceTheGateThroughAValueMerelyBuiltFromIt() =>
        Assert.Null(Literal("SavedWhateverAsked").GateOf(expression => expression.ReadsWord("askServiceAccount")));

    [Fact]
    public void Should_TellThePublishBranchFromRunMode()
    {
        Assert.True(Literal("Published").IsInsidePublishModeBranch());
        Assert.False(Literal("ThenBranch").IsInsidePublishModeBranch());
    }

    private static LiteralExpressionSyntax Literal(string text) =>
        Root.DescendantNodes().OfType<LiteralExpressionSyntax>().Single(literal => literal.Token.ValueText == text);
}
