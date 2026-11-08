using Microsoft.CodeAnalysis;

using UserOrg.AppHost;

namespace UserOrg.AppHost.Tests;

/// <summary>
/// Publish mode has no container to start, so NexusApi there is always the external
/// service behind the required <c>NexusApiBaseUrl</c> parameter (#349, Q2).
/// </summary>
public sealed class NexusApiPublishModeTests
{
    [Fact]
    public void Publish_mode_registers_the_external_service_with_a_required_base_url()
    {
        var text = AppHostSource.TextOf("NexusApiExtensions.cs");

        Assert.Contains("if (!builder.ExecutionContext.IsRunMode)", text, StringComparison.Ordinal);
        Assert.Contains("AddNexusExternal(builder.AddNexusBaseUrl())", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ImportDocument_has_no_instance_when_there_is_no_upstream()
    {
        using var document = System.Text.Json.JsonDocument.Parse(NexusApiModes.ImportDocument("userorg", "account-secret", upstreamUrl: null));

        Assert.Equal(0, document.RootElement.GetProperty("proxyInstances").GetArrayLength());
    }

    [Theory]
    [InlineData("TokenExchange__Nexus__BaseUrl", "NexusBaseUrl")]
    [InlineData("TokenExchange__Nexus__ClientId", "ServiceAccountClient")]
    [InlineData("TokenExchange__Nexus__ClientSecret", "ServiceAccountSecret")]
    [InlineData("TokenExchange__ClientId", "TokenExchangeClientId")]
    [InlineData("TokenExchange__ClientSecret", "TokenExchangeClientSecret")]
    [InlineData("TokenExchange__Authority", "Authority")]
    public void Publish_mode_writes_every_token_exchange_variable_from_its_parameter(string name, string parameter)
    {
        var published = ExternalServiceSyntax.AppHost.Environment(name)
            .Where(assignment => assignment.Node.IsInsidePublishModeBranch())
            .ToList();

        Assert.True(
            published.Any(assignment => assignment.Node.TokenExchangeGateOf() is null && assignment.Value.ReadsText(parameter)),
            $"no assignment in a publish-mode branch writes `{name}` from the {parameter} parameter whatever the flag; publish cannot read the flag and writes it always");
    }

    [Theory]
    [InlineData("NexusApiServiceAccountClient")]
    [InlineData("NexusApiServiceAccountSecret")]
    public void Publish_mode_service_account_parameters_default_to_empty_so_a_flag_off_publish_needs_only_the_base_url(string parameter)
    {
        var declarations = ResourceCommandSyntax.CallsEverywhere("AddParameter")
            .Where(declaration => declaration.Call.ArgumentList.Arguments.Any(argument => AppHostSyntax.StringValue(argument.Expression) == parameter))
            .Select(declaration => Compact(declaration.Call.Chain()))
            .ToList();

        Assert.True(
            declarations.Any(code => code.Contains("newRuntimeDefault(\"\")", StringComparison.Ordinal) && code.Contains("Required=false", StringComparison.Ordinal)),
            $"no `{parameter}` declaration carries `new RuntimeDefault(\"\")` with `Required = false`: {string.Join(" | ", declarations)}");
    }

    [Fact]
    public void Publish_mode_completes_the_selection_before_the_flag_is_touched()
    {
        var applyTo = AppHostSource.RootOf("NexusApiSelection.cs").DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "ApplyTo");

        var publish = applyTo.DescendantNodes()
            .OfType<IfStatementSyntax>()
            .First(statement => statement.Condition.Code().Contains("IsPublishMode", StringComparison.Ordinal));

        var flagUses = applyTo.Body!.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Where(identifier => identifier.Identifier.ValueText.Contains(AppHostSyntax.TokenExchangeFlag, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(flagUses.Count > 0, "NexusApiSelection.ApplyTo does not hand the token exchange flag to the run-mode resolution");
        Assert.All(flagUses, use => Assert.True(
            use.SpanStart > publish.Span.End,
            $"the token exchange flag is touched before the publish-mode early return: {use.Describe()}"));
    }

    private static string Compact(SyntaxNode node) => System.Text.RegularExpressions.Regex.Replace(node.Code(), @"\s+", string.Empty);
}
