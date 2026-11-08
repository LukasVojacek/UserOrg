using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace UserOrg.AppHost.Tests;

/// <summary>
/// The Emailsender 2.4.0 container gets every setting it needs from the AppHost, and shares one
/// generated API key with the userorg API that calls it.
/// </summary>
/// <remarks>
/// 2.4.0 rejects any request without the configured <c>X-Api-Key</c>, so the key has to reach the
/// container and userorg from the same parameter.
/// </remarks>
public sealed class EmailSenderCompositionTests
{
    private const string EmailSenderFile = "EmailSenderExtssensions.cs";

    [Fact]
    public void Should_DeclareTheEmailSenderApiKeyAsAGeneratedSecret() =>
        AssertGeneratedSecretParameter("EmailSenderApiKey");

    [Fact]
    public void Should_DeclareTheEmailSenderBrowserTokenAsAGeneratedSecret() =>
        AssertGeneratedSecretParameter("EmailSenderBrowserToken");

    [Theory]
    [InlineData("ConnectionStrings__Default")]
    [InlineData("ApplicationSettings__TemplatePath")]
    [InlineData("Authorization__ApiKey")]
    [InlineData("Authorization__BrowserToken")]
    [InlineData("ApiDocumentation__Version")]
    [InlineData("ApiDocumentation__Title")]
    [InlineData("ApiDocumentation__TermOfUsageUrl")]
    [InlineData("ApiDocumentation__LicenseUrl")]
    [InlineData("ApiDocumentation__ContactUrl")]
    [InlineData("MailSettings__UseSsl")]
    [InlineData("MailSettings__UseAuthenticationToSmtp")]
    public void Should_SetTheStartupSettingOnTheEmailSenderContainer(string key)
    {
        var calls = ContainerEnvironment(key);

        Assert.True(
            calls.Count == 1,
            $"expected exactly one WithEnvironment(\"{key}\", …) on the Emailsender container, found {calls.Count}; emailsender 2.4.0 does not start without it");
    }

    [Fact]
    public void Should_GiveTheEmailSenderContainerTheApiKeyParameter()
    {
        var call = SingleContainerEnvironment("Authorization__ApiKey");
        var value = call.ArgumentList.Arguments[^1].Expression.Code();

        Assert.True(
            value.MentionsWord("EmailSenderApiKey"),
            $"the Emailsender container takes Authorization__ApiKey from `{value}`, not from the EmailSenderApiKey parameter userorg sends");
    }

    [Fact]
    public void Should_GiveTheUserorgApiTheEmailSenderApiKey()
    {
        var methods = Root("ProjectConfigurationExtensions.cs")
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Where(method => method.Identifier.ValueText == "WithApiEnvironment")
            .ToList();

        Assert.True(methods.Count == 1, $"expected exactly one WithApiEnvironment declaration, found {methods.Count}");

        var calls = methods[0].Calls("WithEnvironment", "MailConfiguration__MailSenderApiKey");

        Assert.True(
            calls.Count == 1,
            $"expected exactly one WithEnvironment(\"MailConfiguration__MailSenderApiKey\", …) in WithApiEnvironment, found {calls.Count}; userorg would call the Emailsender without a key and get 401");

        var value = calls[0].ArgumentList.Arguments[^1].Expression.Code();

        Assert.True(
            value.MentionsWord("EmailSenderApiKey"),
            $"userorg takes MailConfiguration__MailSenderApiKey from `{value}`, not from the EmailSenderApiKey parameter the container checks");
    }

    [Fact]
    public void Should_LeaveTheApiDocumentationHostRelative()
    {
        Assert.Equal([""], ContainerEnvironmentValues("ApiDocumentation__Host"));
    }

    [Fact]
    public void Should_KeepTheSendingDatabaseOnTheExistingVolumePath()
    {
        Assert.Equal(
            ["Data Source=/opt/database/emailsQueueDev.db"],
            ContainerEnvironmentValues("ConnectionStrings__Default"));
    }

    [Fact]
    public void Should_WriteTheEmailSenderLogsToTheConsole()
    {
        var values = Root(EmailSenderFile)
            .Calls("WithEnvironment", "SerilogStepUp__EnableConsoleLogging")
            .Select(call => AppHostSyntax.StringValue(call.ArgumentList.Arguments[^1].Expression))
            .ToList();

        Assert.Equal(["true"], values);
    }

    private static void AssertGeneratedSecretParameter(string name)
    {
        var calls = AppHostSource.ParameterExtensions.Calls("AddParameter", name);

        Assert.True(calls.Count == 1, $"expected exactly one AddParameter(\"{name}\", …) in AddParameters, found {calls.Count}");

        var arguments = calls[0].ArgumentList;

        Assert.True(
            arguments.Code().MentionsWord("GenerateParameterDefault"),
            $"the {name} parameter has no GenerateParameterDefault ({calls[0].Describe()}); a developer would have to type a key before the Emailsender starts");
        Assert.True(
            arguments.Arguments.Any(argument =>
                argument.NameColon?.Name.Identifier.ValueText == "secret"
                && argument.Expression.IsKind(SyntaxKind.TrueLiteralExpression)),
            $"the {name} parameter is not declared with `secret: true` ({calls[0].Describe()}); the dashboard would show the key in clear");
    }

    private static List<string?> ContainerEnvironmentValues(string key) =>
        [.. ContainerEnvironment(key).Select(call => AppHostSyntax.StringValue(call.ArgumentList.Arguments[^1].Expression))];

    private static InvocationExpressionSyntax SingleContainerEnvironment(string key)
    {
        var calls = ContainerEnvironment(key);

        Assert.True(calls.Count == 1, $"expected exactly one WithEnvironment(\"{key}\", …) on the Emailsender container, found {calls.Count}");

        return calls[0];
    }

    private static List<InvocationExpressionSyntax> ContainerEnvironment(string key) =>
        [.. Root(EmailSenderFile)
            .Call("AddContainer", "Emailsender")
            .AppliedCalls("WithEnvironment")
            .Where(call => AppHostSyntax.StringValue(call.ArgumentList.Arguments[0].Expression) == key)];

    private static CompilationUnitSyntax Root(string fileName)
    {
        var key = Path.Combine("src", "UserOrg.AppHost", fileName);

        Assert.True(
            ResourceCommandSyntax.AllRoots.ContainsKey(key),
            $"expected AppHost source file `{key}` to exist");

        return ResourceCommandSyntax.AllRoots[key];
    }
}
