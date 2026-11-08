using System.Text.RegularExpressions;

using Microsoft.CodeAnalysis;

namespace UserOrg.AppHost.Tests;

/// <summary>
/// Under <c>aspire run</c> the gateway's strict token audience check (ADR 0121) comes from the
/// <c>FeatureStrictJwtAudience</c> dashboard parameter, a required boolean prefilled on.
/// </summary>
public sealed partial class StrictJwtAudienceParameterTests
{
    [Fact]
    public void Gateway_reads_the_strict_audience_flag_from_its_parameter()
    {
        var assignment = ExternalServiceSyntax.AppHost.SingleEnvironment("FeatureManagement__StrictJwtAudience");
        var method = assignment.Node.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText;

        Assert.True(
            assignment.ValueCode.MentionsWord("FeatureStrictJwtAudience"),
            $"the gateway's flag does not come from the FeatureStrictJwtAudience parameter: {assignment.Label} = {assignment.ValueCode}");
        Assert.Equal("WithGatewayEnvironment", method);
    }

    [Fact]
    public void Strict_audience_parameter_is_a_required_boolean_prefilled_true()
    {
        var code = WhiteSpace().Replace(AppHostSource.ParameterExtensions.Call("AddParameter", "FeatureStrictJwtAudience").Chain().Code(), string.Empty);

        Assert.Contains("InputType=InputType.Boolean", code, StringComparison.Ordinal);
        Assert.Contains("Required=true", code, StringComparison.Ordinal);
        Assert.Contains("Value=\"true\"", code, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpace();
}
