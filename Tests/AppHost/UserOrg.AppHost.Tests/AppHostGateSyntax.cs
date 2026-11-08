using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace UserOrg.AppHost.Tests;

/// <summary>
/// Which runtime condition decides that a node runs: the token exchange flag in an environment callback,
/// the <c>askServiceAccount</c> argument of the NexusApi prompt, the publish-mode branch of a model build.
/// </summary>
static partial class AppHostSyntax
{
    /// <summary>The name every reference to the <c>FeatureTokenExchangeEnabled</c> parameter carries.</summary>
    public const string TokenExchangeFlag = "TokenExchangeEnabled";

    /// <summary>
    /// The <c>if</c>, conditional or short-circuit operand that lets <paramref name="node"/> run only when
    /// <paramref name="readsGate"/> holds, or <see langword="null"/> when the node runs whatever its value.
    /// </summary>
    /// <remarks>
    /// Recognised shapes, within the enclosing member: the then-branch of <c>if (gate)</c> or the else-branch of
    /// <c>if (!gate)</c>, the matching arm of <c>gate ? a : b</c>, the right operand of <c>gate &amp;&amp; x</c> or
    /// <c>!gate || x</c>, and every statement after an <c>if (!gate) return;</c> in the same or an enclosing block.
    /// A condition that combines the gate with <c>&amp;&amp;</c> still gates; one that combines it with <c>||</c> does not.
    /// </remarks>
    public static SyntaxNode? GateOf(this SyntaxNode node, Func<ExpressionSyntax, bool> readsGate)
    {
        foreach (var ancestor in node.Ancestors().TakeWhile(ancestor => ancestor is not MemberDeclarationSyntax and not CompilationUnitSyntax))
        {
            SyntaxNode? gate = ancestor switch
            {
                IfStatementSyntax statement when statement.Statement.Span.Contains(node.Span) =>
                    Holds(statement.Condition, readsGate) ? statement : null,
                IfStatementSyntax { Else: not null } statement when statement.Else.Span.Contains(node.Span) =>
                    Fails(statement.Condition, readsGate) ? statement : null,
                ConditionalExpressionSyntax conditional when conditional.WhenTrue.Span.Contains(node.Span) =>
                    Holds(conditional.Condition, readsGate) ? conditional : null,
                ConditionalExpressionSyntax conditional when conditional.WhenFalse.Span.Contains(node.Span) =>
                    Fails(conditional.Condition, readsGate) ? conditional : null,
                BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.LogicalAndExpression) && binary.Right.Span.Contains(node.Span) =>
                    Holds(binary.Left, readsGate) ? binary : null,
                BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.LogicalOrExpression) && binary.Right.Span.Contains(node.Span) =>
                    Fails(binary.Left, readsGate) ? binary : null,
                BlockSyntax block => EarlyExitBefore(block, node, readsGate),
                _ => null
            };

            if (gate is not null)
            {
                return gate;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether <paramref name="expression"/> reads the token exchange flag, directly or through the local its member
    /// chain starts from (<c>enabled</c>, <c>flag.Resource</c>) when that local is initialised from it.
    /// </summary>
    public static bool ReadsTokenExchangeFlag(this ExpressionSyntax expression) =>
        expression.Reads(code => code.Contains(TokenExchangeFlag, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether <paramref name="expression"/> reads <paramref name="parameter"/> as a whole word, directly or through a local initialised from it.</summary>
    public static bool ReadsWord(this ExpressionSyntax expression, string parameter) =>
        expression.Reads(code => code.MentionsWord(parameter));

    /// <summary>Whether <paramref name="expression"/> mentions <paramref name="text"/>, ignoring case, directly or through a local initialised from it.</summary>
    public static bool ReadsText(this ExpressionSyntax expression, string text) =>
        expression.Reads(code => code.Contains(text, StringComparison.OrdinalIgnoreCase));

    /// <summary>The <c>if</c> on the token exchange flag, read inside a resource-start callback, that gates <paramref name="node"/>.</summary>
    public static SyntaxNode? TokenExchangeGateOf(this SyntaxNode node) => node.GateOf(ReadsTokenExchangeFlag);

    /// <summary>
    /// Whether <paramref name="node"/> runs only under <c>aspire publish</c>: in the publish-mode branch of an
    /// <c>if</c> or conditional on <c>IsRunMode</c> or <c>IsPublishMode</c>.
    /// </summary>
    public static bool IsInsidePublishModeBranch(this SyntaxNode node) =>
        node.Ancestors().Any(ancestor => ancestor switch
        {
            IfStatementSyntax statement when statement.Statement.Span.Contains(node.Span) => RunModeWhen(statement.Condition) is false,
            IfStatementSyntax { Else: not null } statement when statement.Else.Span.Contains(node.Span) => RunModeWhen(statement.Condition) is true,
            ConditionalExpressionSyntax conditional when conditional.WhenTrue.Span.Contains(node.Span) => RunModeWhen(conditional.Condition) is false,
            ConditionalExpressionSyntax conditional when conditional.WhenFalse.Span.Contains(node.Span) => RunModeWhen(conditional.Condition) is true,
            _ => false
        });

    private static bool Reads(this ExpressionSyntax expression, Func<string, bool> mentionsGate) =>
        Reads(expression, mentionsGate, depth: 3);

    private static bool Reads(SyntaxNode expression, Func<string, bool> mentionsGate, int depth)
    {
        if (mentionsGate(expression.Code()))
        {
            return true;
        }

        if (depth == 0)
        {
            return false;
        }

        if (LocalOf(expression) is not { } local)
        {
            return false;
        }

        return expression.SyntaxTree.GetRoot().DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Where(declarator => declarator.Identifier.ValueText == local && declarator.Initializer is not null)
            .Any(declarator => Reads(declarator.Initializer!.Value, mentionsGate, depth - 1));
    }

    private static string? LocalOf(SyntaxNode expression) => expression switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        ParenthesizedExpressionSyntax parenthesized => LocalOf(parenthesized.Expression),
        AwaitExpressionSyntax awaited => LocalOf(awaited.Expression),
        PostfixUnaryExpressionSyntax suppressed when suppressed.IsKind(SyntaxKind.SuppressNullableWarningExpression) => LocalOf(suppressed.Operand),
        MemberAccessExpressionSyntax member => LocalOf(member.Expression),
        _ => null
    };

    private static bool Holds(ExpressionSyntax condition, Func<ExpressionSyntax, bool> readsGate) => Strip(condition) switch
    {
        BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.LogicalAndExpression) =>
            Holds(binary.Left, readsGate) || Holds(binary.Right, readsGate),
        BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.LogicalOrExpression) =>
            Holds(binary.Left, readsGate) && Holds(binary.Right, readsGate),
        PrefixUnaryExpressionSyntax unary when unary.IsKind(SyntaxKind.LogicalNotExpression) => Fails(unary.Operand, readsGate),
        IsPatternExpressionSyntax { Pattern: ConstantPatternSyntax constant } pattern => constant.Expression.IsKind(SyntaxKind.TrueLiteralExpression)
            ? Holds(pattern.Expression, readsGate)
            : constant.Expression.IsKind(SyntaxKind.FalseLiteralExpression) && Fails(pattern.Expression, readsGate),
        BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.EqualsExpression) && binary.Right.IsKind(SyntaxKind.TrueLiteralExpression) =>
            Holds(binary.Left, readsGate),
        BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.EqualsExpression) && binary.Right.IsKind(SyntaxKind.FalseLiteralExpression) =>
            Fails(binary.Left, readsGate),
        BinaryExpressionSyntax => false,
        var plain => readsGate(plain)
    };

    private static bool Fails(ExpressionSyntax condition, Func<ExpressionSyntax, bool> readsGate) => Strip(condition) switch
    {
        BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.LogicalOrExpression) =>
            Fails(binary.Left, readsGate) || Fails(binary.Right, readsGate),
        BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.LogicalAndExpression) =>
            Fails(binary.Left, readsGate) && Fails(binary.Right, readsGate),
        PrefixUnaryExpressionSyntax unary when unary.IsKind(SyntaxKind.LogicalNotExpression) => Holds(unary.Operand, readsGate),
        IsPatternExpressionSyntax { Pattern: ConstantPatternSyntax constant } pattern => constant.Expression.IsKind(SyntaxKind.FalseLiteralExpression)
            ? Holds(pattern.Expression, readsGate)
            : constant.Expression.IsKind(SyntaxKind.TrueLiteralExpression) && Fails(pattern.Expression, readsGate),
        BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.EqualsExpression) && binary.Right.IsKind(SyntaxKind.FalseLiteralExpression) =>
            Holds(binary.Left, readsGate),
        _ => false
    };

    private static ExpressionSyntax Strip(ExpressionSyntax expression) =>
        expression is ParenthesizedExpressionSyntax parenthesized ? Strip(parenthesized.Expression) : expression;

    private static IfStatementSyntax? EarlyExitBefore(BlockSyntax block, SyntaxNode node, Func<ExpressionSyntax, bool> readsGate) =>
        block.Statements
            .TakeWhile(statement => statement.Span.End <= node.SpanStart)
            .OfType<IfStatementSyntax>()
            .FirstOrDefault(statement => statement.Else is null && AlwaysExits(statement.Statement) && Fails(statement.Condition, readsGate));

    private static bool AlwaysExits(StatementSyntax statement) => statement switch
    {
        ReturnStatementSyntax or ThrowStatementSyntax => true,
        BlockSyntax { Statements.Count: > 0 } block => AlwaysExits(block.Statements[^1]),
        _ => false
    };
}
