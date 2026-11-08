using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace UserOrg.AppHost.Tests;

public sealed class SerilogParameterWiringTests
{
    private const string DiagnosticLoggingPrefix = "DiagnosticLogging__";

    private const string ModeKey = "SerilogStepUp__Mode";

    private const string OtherTaskPrefix = "SerilogStepUp__AdditionalSensitiveHeaders";

    private static readonly string[] LoggingVariablePrefixes = ["SerilogStepUp__", DiagnosticLoggingPrefix];

    [Theory]
    [InlineData("SerilogBaseLevel", "Warning")]
    [InlineData("SerilogStepUpLevel", "Information")]
    [InlineData("SerilogCaptureRequestBody", "false")]
    [InlineData("SerilogAlwaysLogRequestSummary", "true")]
    [InlineData("SerilogMode", "Auto")]
    [InlineData("SerilogEnableConsoleLogging", "false")]
    public void Should_PrefillTheLoggingParameterWithTheProductionValue(string parameterName, string expected)
    {
        var declarations = ResourceCommandSyntax.AllRoots.Values
            .SelectMany(root => root.Calls("AddParameter", parameterName))
            .ToList();

        Assert.True(
            declarations.Count > 0,
            $"the AppHost declares no `AddParameter(\"{parameterName}\", ...)`; the run-mode logging parameters must include it, prefilled {expected}");

        var prefills = declarations.SelectMany(Prefills).ToList();

        Assert.True(
            prefills.Count > 0,
            $"`AddParameter(\"{parameterName}\", ...)` carries no string prefill, neither a `Value = \"...\"` in its custom input nor a default argument; expected {expected}");

        Assert.True(
            prefills.All(prefill => string.Equals(prefill, expected, StringComparison.OrdinalIgnoreCase)),
            $"`AddParameter(\"{parameterName}\", ...)` is prefilled [{string.Join(", ", prefills)}]; expected {expected}, the production logging default");
    }

    [Theory]
    [InlineData("SerilogStepUp__Mode")]
    [InlineData("SerilogStepUp__EnableConsoleLogging")]
    [InlineData("SerilogStepUp__BaseLevel")]
    [InlineData("SerilogStepUp__StepUpLevel")]
    [InlineData("SerilogStepUp__CaptureRequestBody")]
    [InlineData("SerilogStepUp__AlwaysLogRequestSummary")]
    public void Should_WireTheLoggingVariableOnlyInRunMode(string key)
    {
        var sites = LoggingVariableSites().Where(site => site.Key == key).ToList();

        Assert.True(
            sites.Count > 0,
            $"no AppHost source sets `{key}`; under `aspire run` the API and the Gateway would not receive the dashboard's logging parameter");

        var ungated = sites.Where(site => !IsRunModeGated(site.Node, [])).ToList();

        Assert.True(
            ungated.Count == 0,
            $"`{key}` is set outside a run-mode branch at {Describe(ungated)}; a published deployment takes its logging from the production logging defaults, so the call, or every call that reaches it, has to sit in a branch on ExecutionContext.IsRunMode");
    }

    [Fact]
    public void Should_SetNoSerilogOrDiagnosticLoggingVariableOutsideRunMode()
    {
        var sites = LoggingVariableSites()
            .Where(site => !site.Key.StartsWith(OtherTaskPrefix, StringComparison.Ordinal))
            .ToList();

        var ungated = sites.Where(site => !IsRunModeGated(site.Node, [])).ToList();

        Assert.True(
            ungated.Count == 0,
            $"these logging variables reach a published deployment: {Describe(ungated)}");

        Assert.True(
            sites.Any(site => site.Key == ModeKey),
            $"found the logging variables [{string.Join(", ", sites.Select(site => site.Key).Distinct())}] but not `{ModeKey}`, so the run-mode check above could pass on a missing logging wiring");
    }

    [Fact]
    public void Should_SetNoDiagnosticLoggingVariable()
    {
        var sites = LoggingVariableSites().Where(site => site.Key.StartsWith(DiagnosticLoggingPrefix, StringComparison.Ordinal)).ToList();

        Assert.True(
            sites.Count == 0,
            $"the AppHost still sets a `{DiagnosticLoggingPrefix}*` variable at {Describe(sites)}; Diagnostic mode is `{ModeKey}=Diagnostic` from the SerilogMode parameter");
    }

    [Fact]
    public void Should_OfferAutoAndDiagnosticAsSerilogModeOptions()
    {
        var options = ResourceCommandSyntax.AllRoots.Values
            .SelectMany(root => root.Calls("AddParameter", "SerilogMode"))
            .SelectMany(declaration => WholeDeclaration(declaration)
                .DescendantNodes()
                .OfType<AssignmentExpressionSyntax>()
                .Where(assignment => assignment.Left is IdentifierNameSyntax { Identifier.ValueText: "Options" }))
            .SelectMany(assignment => assignment.Right.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>())
            .Select(option => option.ArgumentList?.Arguments.FirstOrDefault() is { } first ? AppHostSyntax.StringValue(first.Expression) : null)
            .OfType<string>()
            .ToList();

        Assert.True(
            options.Contains("Auto") && options.Contains("Diagnostic"),
            $"the SerilogMode Choice offers [{string.Join(", ", options)}]; it must offer Auto and Diagnostic, the only switch into Diagnostic mode");
    }

    private static IEnumerable<string> Prefills(InvocationExpressionSyntax declaration)
    {
        var customInputValues = WholeDeclaration(declaration)
            .DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Where(assignment => assignment.Parent is InitializerExpressionSyntax
                && assignment.Left is IdentifierNameSyntax { Identifier.ValueText: "Value" })
            .Select(assignment => AppHostSyntax.StringValue(assignment.Right))
            .OfType<string>();

        var defaultArguments = declaration.ArgumentList.Arguments
            .Skip(1)
            .SelectMany(argument => argument.Expression.DescendantNodesAndSelf().OfType<LiteralExpressionSyntax>())
            .Where(literal => literal.IsKind(SyntaxKind.StringLiteralExpression))
            .Select(literal => literal.Token.ValueText);

        return customInputValues.Concat(defaultArguments);
    }

    private static SyntaxNode WholeDeclaration(InvocationExpressionSyntax declaration)
    {
        SyntaxNode node = declaration;

        while (node.Parent is { } parent && parent switch
        {
            MemberAccessExpressionSyntax member => member.Expression == node,
            InvocationExpressionSyntax invocation => invocation.Expression == node,
            ParenthesizedExpressionSyntax => true,
            ConditionalExpressionSyntax conditional => conditional.Condition != node,
            _ => false
        })
        {
            node = parent;
        }

        return node;
    }

    private static List<LoggingVariableSite> LoggingVariableSites() =>
        [.. ResourceCommandSyntax.AllRoots.SelectMany(file => file.Value
            .DescendantNodes()
            .Select(node => (Node: node, Key: LeadingText(node)))
            .Where(candidate => candidate.Key is { } key
                && LoggingVariablePrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal)))
            .Select(candidate => new LoggingVariableSite(file.Key, candidate.Key!, candidate.Node)))];

    private static string? LeadingText(SyntaxNode node) => node switch
    {
        LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression) => literal.Token.ValueText,
        InterpolatedStringExpressionSyntax { Contents: [InterpolatedStringTextSyntax text, ..] } => text.TextToken.ValueText,
        _ => null
    };

    private static string Describe(IEnumerable<LoggingVariableSite> sites) =>
        string.Join("; ", sites.Select(site => $"{site.File}:{Line(site.Node)} {Call(site.Node)}"));

    private static int Line(SyntaxNode node) => node.SyntaxTree.GetLineSpan(node.Span).StartLinePosition.Line + 1;

    private static string Call(SyntaxNode node) =>
        node.Ancestors().OfType<InvocationExpressionSyntax>().FirstOrDefault() is { } call
            ? $"{AppHostSyntax.NameOf(call)}{call.ArgumentList}"
            : node.ToString();

    private static bool IsRunModeGated(SyntaxNode node, HashSet<string> visited)
    {
        var scope = ScopeOf(node);

        if (AppHostSyntax.IsInsideRunModeBranch(node, scope))
        {
            return true;
        }

        var name = scope switch
        {
            MethodDeclarationSyntax method => method.Identifier.ValueText,
            LocalFunctionStatementSyntax function => function.Identifier.ValueText,
            _ => null
        };

        if (name is null || visited.Contains(name))
        {
            return false;
        }

        var callers = ResourceCommandSyntax.AllRoots.Values.SelectMany(root => root.Calls(name)).ToList();

        return callers.Count > 0 && callers.All(caller => IsRunModeGated(caller, [.. visited, name]));
    }

    private static SyntaxNode ScopeOf(SyntaxNode node) =>
        node.Ancestors().FirstOrDefault(ancestor => ancestor is MethodDeclarationSyntax or LocalFunctionStatementSyntax)
        ?? node.SyntaxTree.GetRoot();

    private sealed record LoggingVariableSite(string File, string Key, SyntaxNode Node);
}