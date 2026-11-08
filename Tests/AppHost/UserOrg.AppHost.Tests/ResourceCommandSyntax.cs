using System.Text.RegularExpressions;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace UserOrg.AppHost.Tests;

/// <summary>
/// Lookup for the dashboard commands the AppHost registers with <c>WithCommand</c>, written over the
/// AppHost's own source syntax — see <see cref="AppHostSource"/> for why the resource model itself is
/// out of reach here.
/// </summary>
/// <remarks>
/// A command is found by its registration name wherever it is declared: inline in <c>Program.cs</c>, or
/// inside an <c>extension(IResourceBuilder&lt;…&gt;)</c> block the way <c>ResourceExtensions</c> declares
/// the SQL Server ones. Everything a test asserts against is comment-free token text, so a mention in a
/// comment can neither satisfy a positive assertion nor break a negative one.
/// </remarks>
static class ResourceCommandSyntax
{
    /// <summary>Every AppHost source file, parsed, by repo-relative path.</summary>
    public static IReadOnlyDictionary<string, CompilationUnitSyntax> AllRoots { get; } =
        AppHostSource.AllFiles.ToDictionary(
            file => file.Key,
            file => (CompilationUnitSyntax)AppHostSource.Parse(file.Value).GetRoot());

    /// <summary>The single <c>WithCommand("<paramref name="name"/>", …)</c> registration in the AppHost.</summary>
    public static ResourceCommand Command(string name)
    {
        var matches = AllRoots
            .SelectMany(file => file.Value.Calls("WithCommand", name)
                .Select(registration => new ResourceCommand(file.Key, registration)))
            .ToList();

        Assert.True(
            matches.Count == 1,
            $"expected exactly one `WithCommand(\"{name}\", …)` registration in the AppHost, found {matches.Count}");

        return matches[0];
    }

    /// <summary>Every <c>WithCommand</c> registration in the AppHost, by repo-relative file.</summary>
    public static IReadOnlyList<ResourceCommand> AllCommands { get; } =
        [.. AllRoots.SelectMany(file => file.Value.Calls("WithCommand")
            .Select(registration => new ResourceCommand(file.Key, registration)))];

    /// <summary>Every invocation of <paramref name="method"/> in the AppHost, by repo-relative file.</summary>
    public static IReadOnlyList<(string File, InvocationExpressionSyntax Call)> CallsEverywhere(string method) =>
        [.. AllRoots.SelectMany(file => file.Value.Calls(method).Select(call => (file.Key, call)))];

    /// <summary>
    /// The comment-free code of a node: its tokens, joined, with the whitespace around member access
    /// removed so that <c>HealthStatus . Healthy</c> reads as <c>HealthStatus.Healthy</c>.
    /// </summary>
    public static string Code(this SyntaxNode node) =>
        Regex.Replace(
            string.Join(' ', node.DescendantTokens().Select(token => token.ToString())),
            @"\s*\.\s*",
            ".");

    /// <summary>Whether <paramref name="code"/> mentions <paramref name="word"/> as a whole word.</summary>
    /// <remarks><c>Reset</c> does not match inside <c>ResetAndSeed</c>.</remarks>
    public static bool MentionsWord(this string code, string word) =>
        Regex.IsMatch(code, $@"\b{Regex.Escape(word)}\b");

    /// <summary>
    /// Whether an interaction call is guarded by <c>IInteractionService.IsAvailable</c> — either wrapped
    /// in such an <c>if</c>, or preceded by one in the same callback, which is the early-return shape.
    /// </summary>
    public static bool IsGuardedOnInteractionAvailability(this InvocationExpressionSyntax invocation)
    {
        SyntaxNode? scope = invocation.Ancestors().OfType<AnonymousFunctionExpressionSyntax>().LastOrDefault();
        scope ??= invocation.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        scope ??= invocation.SyntaxTree.GetRoot();

        return scope.DescendantNodes()
            .OfType<IfStatementSyntax>()
            .Any(statement => statement.Condition.Code().Contains("IsAvailable", StringComparison.Ordinal));
    }

    /// <summary>A <c>WithCommand</c> registration and the code around it.</summary>
    /// <param name="File">The repo-relative file the registration lives in.</param>
    /// <param name="Registration">The <c>WithCommand</c> invocation itself.</param>
    public sealed record ResourceCommand(string File, InvocationExpressionSyntax Registration)
    {
        /// <summary>The method that registers the command, or <see langword="null"/> for a top-level registration.</summary>
        public MethodDeclarationSyntax? DeclaringMethod =>
            Registration.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();

        /// <summary>
        /// The class the command's implementation lives in — or the whole file, for <c>Program.cs</c>'s
        /// top-level statements. This is the scope a command's helpers are looked for in.
        /// </summary>
        public SyntaxNode DeclaringScope =>
            Registration.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault()
            ?? Registration.SyntaxTree.GetRoot();

        /// <summary>The comment-free code of <see cref="DeclaringScope"/>.</summary>
        public string ImplementationCode => DeclaringScope.Code();

        /// <summary>
        /// The comment-free code of the command's execute callback, falling back to the declaring method
        /// and then to the declaring scope when the callback is not a lambda argument.
        /// </summary>
        public string CallbackCode
        {
            get
            {
                var lambdas = Registration.LambdaArguments();
                return lambdas.Count > 0
                    ? string.Join('\n', lambdas.Select(lambda => lambda.Code()))
                    : (DeclaringMethod as SyntaxNode ?? DeclaringScope).Code();
            }
        }

        /// <summary>
        /// The receiver types the command is registered on: the parameters of the enclosing
        /// <c>extension(…)</c> block and of the declaring method.
        /// </summary>
        public IReadOnlyList<string> ReceiverTypes =>
            [.. Registration.Ancestors()
                .OfType<TypeDeclarationSyntax>()
                .SelectMany(type => type.ParameterList is { } list ? list.Parameters.AsEnumerable() : [])
                .Concat(DeclaringMethod?.ParameterList.Parameters.AsEnumerable() ?? [])
                .Select(parameter => parameter.Type?.ToString() ?? string.Empty)];

        /// <summary>The <c>CommandOptions</c> object creations the registration carries.</summary>
        public IReadOnlyList<BaseObjectCreationExpressionSyntax> OptionsCreations
        {
            get
            {
                // A direct `commandOptions:` argument, whether spelled `new CommandOptions { … }` or `new() { … }`,
                // plus any explicitly typed CommandOptions nested deeper in the argument list.
                var direct = Registration.ArgumentList.Arguments
                    .Select(argument => argument.Expression)
                    .OfType<BaseObjectCreationExpressionSyntax>()
                    .Where(creation => creation is not ObjectCreationExpressionSyntax explicitly
                        || explicitly.Type.ToString().Contains("CommandOptions", StringComparison.Ordinal));

                var nested = Registration.ArgumentList.DescendantNodes()
                    .OfType<ObjectCreationExpressionSyntax>()
                    .Where(creation => creation.Type.ToString().Contains("CommandOptions", StringComparison.Ordinal));

                var inline = direct.Concat<BaseObjectCreationExpressionSyntax>(nested).Distinct().ToList();

                return inline.Count > 0
                    ? inline
                    : [.. DeclaringScope.DescendantNodes()
                        .OfType<ObjectCreationExpressionSyntax>()
                        .Where(creation => creation.Type.ToString().Contains("CommandOptions", StringComparison.Ordinal))];
            }
        }

        /// <summary>
        /// The comment-free code of the command's <c>CommandOptions</c>. Fails the test when the
        /// registration carries none, so that a "must not appear" assertion cannot pass vacuously.
        /// </summary>
        public string OptionsCode
        {
            get
            {
                var creations = OptionsCreations;
                Assert.True(creations.Count > 0, $"the `{File}` command registration carries no CommandOptions");

                return string.Join('\n', creations.Select(creation => creation.Code()));
            }
        }

        /// <summary>The values assigned to <paramref name="property"/> in the command's <c>CommandOptions</c>.</summary>
        public IReadOnlyList<string> OptionValues(string property) =>
            [.. OptionsCreations
                .SelectMany(creation => creation.Initializer is { } initializer
                    ? initializer.Expressions.AsEnumerable()
                    : [])
                .OfType<AssignmentExpressionSyntax>()
                .Where(assignment => assignment.Left.ToString() == property)
                .Select(assignment => assignment.Right.Code())];

        /// <summary>The text of every string literal and interpolated string in <see cref="DeclaringScope"/>.</summary>
        public IReadOnlyList<string> Scripts =>
            [.. DeclaringScope.DescendantNodes()
                .Where(node => node is InterpolatedStringExpressionSyntax
                    || (node is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression)))
                .Select(node => node.ToString())];
    }
}
