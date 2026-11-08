using System.Text.RegularExpressions;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace UserOrg.AppHost.Tests;

/// <summary>
/// The vocabulary the AppHost composition tests are written in: fluent-chain lookup, and the
/// build-time-versus-run-time distinction that this block turns on.
/// </summary>
static partial class AppHostSyntax
{
    /// <summary>Every invocation of <paramref name="method"/>, optionally narrowed by a string argument.</summary>
    public static IReadOnlyList<InvocationExpressionSyntax> Calls(
        this SyntaxNode root,
        string method,
        string? withStringArgument = null) =>
        // …AndSelf: the last link of a fluent chain (`…​.ExcludeFromManifest()`) *is* the chain node.
        [.. root.DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Where(invocation => NameOf(invocation) == method)
            .Where(invocation => withStringArgument is null
                || invocation.ArgumentList.Arguments.Any(argument => StringValue(argument.Expression) == withStringArgument))];

    /// <summary>The single invocation of <paramref name="method"/>; fails the test when it is missing or ambiguous.</summary>
    public static InvocationExpressionSyntax Call(
        this SyntaxNode root,
        string method,
        string? withStringArgument = null)
    {
        var matches = root.Calls(method, withStringArgument);
        var description = withStringArgument is null ? method : $"{method}(\"{withStringArgument}\", …)";
        Assert.True(matches.Count == 1, $"expected exactly one `{description}` call, found {matches.Count}");
        return matches[0];
    }

    /// <summary>The simple name being invoked (<c>a.B(c)</c> → <c>B</c>).</summary>
    public static string? NameOf(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        GenericNameSyntax generic => generic.Identifier.ValueText,
        MemberBindingExpressionSyntax binding => binding.Name.Identifier.ValueText,
        _ => null
    };

    /// <summary>The constant string an expression carries, or <see langword="null"/> when it is not a string literal.</summary>
    public static string? StringValue(ExpressionSyntax expression) =>
        expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression)
            ? literal.Token.ValueText
            : null;

    /// <summary>The whole fluent expression an invocation belongs to (<c>a.B().C().D()</c> for any link of it).</summary>
    public static ExpressionSyntax Chain(this InvocationExpressionSyntax invocation)
    {
        ExpressionSyntax chain = invocation;
        while (chain.Parent is MemberAccessExpressionSyntax member && member.Expression == chain)
        {
            chain = member.Parent is InvocationExpressionSyntax call ? call : member;
        }

        return chain;
    }

    /// <summary>The name of the local the fluent chain is assigned to, if any.</summary>
    public static string? AssignedVariable(this InvocationExpressionSyntax invocation) =>
        invocation.Chain().Ancestors().OfType<VariableDeclaratorSyntax>().FirstOrDefault()?.Identifier.ValueText;

    /// <summary>The leftmost identifier a fluent chain starts from (<c>api.WithEnvironment(…)…</c> → <c>api</c>).</summary>
    public static string? Receiver(this InvocationExpressionSyntax invocation)
    {
        ExpressionSyntax current = invocation;
        while (true)
        {
            switch (current)
            {
                case InvocationExpressionSyntax call:
                    current = call.Expression;
                    break;
                case MemberAccessExpressionSyntax member:
                    current = member.Expression;
                    break;
                case IdentifierNameSyntax identifier:
                    return identifier.Identifier.ValueText;
                default:
                    return null;
            }
        }
    }

    /// <summary>
    /// Whether <paramref name="method"/> is applied to the resource <paramref name="anchor"/> creates —
    /// either in the same fluent chain, or later on the local that chain was assigned to.
    /// </summary>
    public static bool AppliesToSameResource(this InvocationExpressionSyntax anchor, SyntaxNode root, string method)
    {
        if (anchor.Chain().Calls(method).Count > 0)
        {
            return true;
        }

        var variable = anchor.AssignedVariable();
        return variable is not null
            && root.Calls(method).Any(call => call.Receiver() == variable);
    }

    /// <summary>
    /// The conditions of every <c>if</c> that decides, <em>at model-build time</em>, whether
    /// <paramref name="node"/> runs at all.
    /// </summary>
    /// <remarks>
    /// A node inside a lambda is anchored at the outermost lambda: the lambda body runs when Aspire
    /// invokes the callback (resource start), so an <c>if</c> inside it is a run-time decision, while an
    /// <c>if</c> around the lambda's registration is still a build-time one. That anchoring is the whole
    /// point of this block — see the task's `## Context`.
    /// </remarks>
    public static IReadOnlyList<string> BuildTimeConditions(this SyntaxNode node)
    {
        var anchor = node.AncestorsAndSelf().OfType<AnonymousFunctionExpressionSyntax>().LastOrDefault() ?? node;

        return [.. anchor.Ancestors()
            .OfType<IfStatementSyntax>()
            .Select(statement => statement.Condition.ToString())];
    }

    /// <summary>Whether the node is registered unconditionally at model-build time.</summary>
    public static bool IsBuildTimeUnconditional(this SyntaxNode node) => node.BuildTimeConditions().Count == 0;

    /// <summary>Whether a build-time <c>if</c> around the node branches on the database-source choice.</summary>
    public static bool IsGatedOnDatabaseSource(this SyntaxNode node) =>
        node.BuildTimeConditions().Any(condition =>
            condition.Contains("useLocalConnections", StringComparison.OrdinalIgnoreCase)
            || condition.Contains("DatabaseSource", StringComparison.OrdinalIgnoreCase)
            || condition.Contains("LocalContainer", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether <paramref name="node"/> runs only in run mode within <paramref name="scope"/>: inside the
    /// run-mode branch of an <c>if</c> or conditional, or after an early return taken outside run mode.
    /// </summary>
    public static bool IsInsideRunModeBranch(SyntaxNode node, SyntaxNode scope)
    {
        foreach (var ancestor in node.Ancestors().TakeWhile(ancestor => ancestor != scope))
        {
            var gated = ancestor switch
            {
                IfStatementSyntax statement => statement.Statement.Span.Contains(node.Span)
                    ? RunModeWhen(statement.Condition) is true
                    : statement.Else is not null && statement.Else.Span.Contains(node.Span) && RunModeWhen(statement.Condition) is false,
                ConditionalExpressionSyntax conditional => conditional.WhenTrue.Span.Contains(node.Span)
                    ? RunModeWhen(conditional.Condition) is true
                    : conditional.WhenFalse.Span.Contains(node.Span) && RunModeWhen(conditional.Condition) is false,
                _ => false
            };

            if (gated)
            {
                return true;
            }
        }

        return scope.DescendantNodes()
            .OfType<IfStatementSyntax>()
            .Any(statement => statement.Else is null
                && statement.Span.End <= node.SpanStart
                && statement.Parent is BlockSyntax block
                && block.Span.Contains(node.Span)
                && RunModeWhen(statement.Condition) is false
                && statement.Statement.DescendantNodesAndSelf().OfType<ReturnStatementSyntax>().Any());
    }

    private static bool? RunModeWhen(ExpressionSyntax condition)
    {
        var code = condition.Code();
        var negated = NegatedModePattern().IsMatch(code);

        if (code.Contains("IsRunMode", StringComparison.OrdinalIgnoreCase))
        {
            return !negated;
        }

        if (code.Contains("IsPublishMode", StringComparison.OrdinalIgnoreCase))
        {
            return negated;
        }

        return null;
    }

    [GeneratedRegex(@"!\s*\(?\s*[\w.]*Is(Run|Publish)Mode\b|Is(Run|Publish)Mode\s*(==\s*false|!=\s*true|is\s+false)", RegexOptions.IgnoreCase)]
    private static partial Regex NegatedModePattern();

    /// <summary>Whether the node sits inside a lambda — i.e. is evaluated when Aspire invokes a callback.</summary>
    public static bool IsInsideCallback(this SyntaxNode node) =>
        node.Ancestors().OfType<AnonymousFunctionExpressionSyntax>().Any();

    /// <summary>The lambda arguments of an invocation.</summary>
    public static IReadOnlyList<AnonymousFunctionExpressionSyntax> LambdaArguments(this InvocationExpressionSyntax invocation) =>
        [.. invocation.ArgumentList.Arguments
            .Select(argument => argument.Expression)
            .OfType<AnonymousFunctionExpressionSyntax>()];

    /// <summary>A one-line, human-readable rendering of where a node sits, for assertion messages.</summary>
    public static string Describe(this SyntaxNode node)
    {
        var line = node.SyntaxTree.GetLineSpan(node.Span).StartLinePosition.Line + 1;
        var text = node.ToString();
        var flattened = string.Join(' ', text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(part => part.Trim()));
        return $"line {line}: {(flattened.Length > 160 ? $"{flattened[..160]}…" : flattened)}";
    }
}
