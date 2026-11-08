using Microsoft.CodeAnalysis;

namespace UserOrg.AppHost.Tests;

public sealed class TrustedProxyEnvironmentTests
{
    private const string FileName = "ProjectConfigurationExtensions.cs";

    [Theory]
    [InlineData("WithApiEnvironment", "ForwardedHeaders__KnownProxies__0", "127.0.0.1")]
    [InlineData("WithApiEnvironment", "ForwardedHeaders__KnownProxies__1", "::1")]
    [InlineData("WithGatewayEnvironment", "ForwardedHeaders__KnownProxies__0", "127.0.0.1")]
    [InlineData("WithGatewayEnvironment", "ForwardedHeaders__KnownProxies__1", "::1")]
    public void Should_TrustTheLoopbackProxyOnlyInRunMode(string methodName, string key, string value)
    {
        var method = Root().Method(methodName);

        var calls = Helpers(method)
            .Prepend(method)
            .SelectMany(scope => scope.Calls("WithEnvironment", key))
            .Where(call => call.ArgumentList.Arguments.Count == 2
                && AppHostSyntax.StringValue(call.ArgumentList.Arguments[1].Expression) == value)
            .ToList();

        Assert.True(
            calls.Count > 0,
            $"{methodName} never reaches `WithEnvironment(\"{key}\", \"{value}\")`, neither in its own body nor in a helper of {FileName} it calls; under `aspire run` the resource trusts no loopback proxy and ForwardedHeaders drops the X-Forwarded-* headers the local dev proxy sends");

        foreach (var call in calls)
        {
            Assert.True(
                IsRunModeGated(call, method),
                $"{methodName} sets {key} outside a run-mode branch ({call.Describe()}); the loopback proxies belong to `aspire run` and must not reach a published deployment, so the call, or the helper call that reaches it, has to sit in a branch on ExecutionContext.IsRunMode");
        }
    }

    private static bool IsRunModeGated(InvocationExpressionSyntax call, MethodDeclarationSyntax method)
    {
        var declaring = call.Ancestors().OfType<MethodDeclarationSyntax>().First();

        if (AppHostSyntax.IsInsideRunModeBranch(call, declaring))
        {
            return true;
        }

        if (declaring == method)
        {
            return false;
        }

        var helperCalls = method.Calls(declaring.Identifier.ValueText);
        return helperCalls.Count > 0 && helperCalls.All(helperCall => AppHostSyntax.IsInsideRunModeBranch(helperCall, method));
    }

    private static IEnumerable<MethodDeclarationSyntax> Helpers(MethodDeclarationSyntax method)
    {
        var invoked = method.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Select(AppHostSyntax.NameOf)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        return Root()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Where(candidate => candidate != method && invoked.Contains(candidate.Identifier.ValueText));
    }

    private static CompilationUnitSyntax Root()
    {
        var key = Path.Combine("src", "UserOrg.AppHost", FileName);

        Assert.True(
            ResourceCommandSyntax.AllRoots.ContainsKey(key),
            $"expected AppHost source file `{key}` to exist");

        return ResourceCommandSyntax.AllRoots[key];
    }
}