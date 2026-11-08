using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace UserOrg.AppHost.Tests;

static class PathStringLogGuard
{
    private static readonly HashSet<string> LoggerNames = ["logger", "_logger", "Logger"];

    private static readonly HashSet<string> RequestMembers = ["Path", "PathBase", "QueryString", "Query"];

    public static IReadOnlyList<string> Offences(string file, SyntaxTree tree) =>
        [.. tree.GetRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(IsLoggerCall)
            .Select(Template)
            .OfType<ExpressionSyntax>()
            .Where(IsOffendingTemplate)
            .Select(node => node.SyntaxTree.GetLineSpan(node.Span).StartLinePosition.Line + 1)
            .Distinct()
            .Order()
            .Select(line => $"{file}:{line}")];

    private static bool IsLoggerCall(InvocationExpressionSyntax call) =>
        ReceiverExpression(call) is { } receiver && IsLogger(receiver)
        || call.ArgumentList.Arguments is [{ Expression: IdentifierNameSyntax first }, ..] && LoggerNames.Contains(first.Identifier.ValueText);

    private static ExpressionSyntax? Template(InvocationExpressionSyntax call) =>
        MethodName(call) is { } name && name.StartsWith("Log", StringComparison.Ordinal)
            ? call.ArgumentList.Arguments
                .Select(argument => argument.Expression)
                .FirstOrDefault(IsTextExpression)
            : null;

    private static bool IsTextExpression(ExpressionSyntax expression) => expression switch
    {
        LiteralExpressionSyntax or InterpolatedStringExpressionSyntax => true,
        BinaryExpressionSyntax binary => binary.IsKind(SyntaxKind.AddExpression)
            && binary.DescendantNodes().Any(node => node is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression)),
        _ => false
    };

    private static bool IsOffendingTemplate(ExpressionSyntax template) => template switch
    {
        InterpolatedStringExpressionSyntax => true,
        BinaryExpressionSyntax binary => binary.DescendantNodes().Any(IsRequestMember),
        _ => false
    };

    private static bool IsRequestMember(SyntaxNode node) =>
        node is ExpressionSyntax expression && MemberName(expression) is { } name && RequestMembers.Contains(name);

    private static bool IsLogger(ExpressionSyntax receiver) => receiver switch
    {
        InvocationExpressionSyntax invocation => MethodName(invocation) == "CreateLogger",
        _ => RightmostName(receiver) is { } name && LoggerNames.Contains(name)
    };

    private static string? MethodName(InvocationExpressionSyntax call) => MemberName(call.Expression);

    private static ExpressionSyntax? ReceiverExpression(InvocationExpressionSyntax call) => call.Expression switch
    {
        MemberAccessExpressionSyntax member => member.Expression,
        MemberBindingExpressionSyntax => call.FirstAncestorOrSelf<ConditionalAccessExpressionSyntax>(
            access => access.WhenNotNull.Span.Contains(call.Span))?.Expression,
        _ => null
    };

    private static string? RightmostName(ExpressionSyntax expression) => expression switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
        _ => null
    };

    private static string? MemberName(ExpressionSyntax expression) => expression switch
    {
        MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
        MemberBindingExpressionSyntax binding => binding.Name.Identifier.ValueText,
        _ => null
    };
}