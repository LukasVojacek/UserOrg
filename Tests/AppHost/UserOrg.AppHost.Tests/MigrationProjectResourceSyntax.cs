using Microsoft.CodeAnalysis;

namespace UserOrg.AppHost.Tests;

static class MigrationProjectResourceSyntax
{
    public const string MigrationToolFile = "MigrationToolExtssensions.cs";
    public const string DatabaseSourceFile = "DatabaseSourceSelection.cs";
    public const string ResourceExtensionsFile = "ResourceExtensions.cs";

    public const string ApplyResource = "db-migrations-apply";
    public const string ScriptResource = "db-migrations-script";
    public const string SeedResource = "db-seed-e2e";

    public const string ProjectMetadataType = "Projects.DatabaseMigrations";

    private static readonly string[] ProjectCompatibleTypes = ["ProjectResource", "IResource", "Resource"];

    public static CompilationUnitSyntax MigrationTools => AppHostSource.MigrationToolExtensions;

    public static CompilationUnitSyntax DatabaseSourceSelection => AppHostSource.RootOf(DatabaseSourceFile);

    public static CompilationUnitSyntax ResourceExtensions => AppHostSource.RootOf(ResourceExtensionsFile);

    public static IReadOnlyList<InvocationExpressionSyntax> Registrations(string resourceName) =>
        [.. MigrationTools.DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Where(invocation => AppHostSyntax.NameOf(invocation) is { } name
                && name.StartsWith("Add", StringComparison.Ordinal))
            .Where(invocation => invocation.ArgumentList.Arguments
                .Any(argument => AppHostSyntax.StringValue(argument.Expression) == resourceName))];

    public static InvocationExpressionSyntax Registration(string resourceName)
    {
        var matches = Registrations(resourceName);

        Assert.True(
            matches.Count == 1,
            $"expected exactly one `Add…(\"{resourceName}\", …)` registration in {MigrationToolFile}, found {matches.Count}");

        return matches[0];
    }

    public static string FactoryName(this InvocationExpressionSyntax registration) =>
        AppHostSyntax.NameOf(registration) ?? string.Empty;

    public static string? GenericTypeArgument(this InvocationExpressionSyntax registration) =>
        registration.Expression switch
        {
            MemberAccessExpressionSyntax { Name: GenericNameSyntax member } => member.TypeArgumentList.Arguments.ToString(),
            GenericNameSyntax bare => bare.TypeArgumentList.Arguments.ToString(),
            _ => null
        };

    public static IReadOnlyList<string> ArgumentShape(this InvocationExpressionSyntax registration)
    {
        var fromCollectionArgument = registration.ArgumentList.Arguments
            .Select(argument => argument.Expression)
            .OfType<CollectionExpressionSyntax>()
            .SelectMany(collection => collection.Elements
                .OfType<ExpressionElementSyntax>()
                .Select(element => element.Expression));

        var fromWithArgs = registration.Chain().Calls("WithArgs")
            .SelectMany(call => call.ArgumentList.Arguments.Select(argument => argument.Expression))
            .Where(expression => expression is not AnonymousFunctionExpressionSyntax);

        return [.. fromCollectionArgument
            .Concat(fromWithArgs)
            .OrderBy(expression => expression.SpanStart)
            .Select(Rendered)];
    }

    public static string Rendered(ExpressionSyntax expression) =>
        AppHostSyntax.StringValue(expression) ?? $"<{expression}>";

    public static IReadOnlyList<string> EnvironmentVariableNames(this InvocationExpressionSyntax registration) =>
        [.. registration.Chain().Calls("WithEnvironment")
            .Select(call => AppHostSyntax.StringValue(call.ArgumentList.Arguments[0].Expression))
            .Where(name => name is not null)
            .Select(name => name!)];

    public static IReadOnlyList<string> ArgumentTextOf(this InvocationExpressionSyntax registration, string method) =>
        [.. registration.Chain().Calls(method)
            .Select(call => call.ArgumentList.Arguments.ToString())];

    public static MethodDeclarationSyntax Method(this CompilationUnitSyntax root, string name) =>
        Assert.Single(
            root.DescendantNodes().OfType<MethodDeclarationSyntax>(),
            method => method.Identifier.ValueText == name);

    public static string ParameterType(this MethodDeclarationSyntax method, string parameterName)
    {
        var parameter = Assert.Single(
            method.ParameterList.Parameters,
            candidate => candidate.Identifier.ValueText == parameterName);

        return parameter.Type?.ToString() ?? string.Empty;
    }

    public static string ReturnType(this MethodDeclarationSyntax method) => method.ReturnType.ToString();

    public static bool IsBoundToExecutableResource(string type) =>
        type.Contains("ExecutableResource", StringComparison.Ordinal);

    public static bool AcceptsAProjectResource(string type)
    {
        var compact = type.Replace(" ", string.Empty, StringComparison.Ordinal).TrimEnd('?');

        var inner = compact.StartsWith("IResourceBuilder<", StringComparison.Ordinal)
            ? compact["IResourceBuilder<".Length..].TrimEnd('>')
            : compact;

        return ProjectCompatibleTypes.Contains(inner, StringComparer.Ordinal);
    }
}
