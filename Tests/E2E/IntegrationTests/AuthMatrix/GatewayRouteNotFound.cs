using System.Text.Json;

namespace IntegrationTests.AuthMatrix;

/// <summary>
/// Tells the gateway's route-existence 404 (ADR-0106) apart from an upstream 404 for a missing child.
/// The matrix needs the distinction twice over: to assert the new outcome, and to stop an oracle that
/// wrongly 404s a real route from passing as <see cref="ExpectedOutcome.Allowed"/> - most Allowed-tier
/// writes here land on a legitimate upstream 404 by design (see <see cref="AuthMatrixData.BuildRequest"/>),
/// so "not 404" alone would be wrong.
/// </summary>
internal static class GatewayRouteNotFound
{
    internal const string Code = "route.not_found";

    internal static bool Matches(int status, string body)
    {
        if (status != 404 || string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        try
        {
            using var problem = JsonDocument.Parse(body);
            return problem.RootElement.ValueKind == JsonValueKind.Object
                && problem.RootElement.TryGetProperty("code", out var code)
                && code.ValueKind == JsonValueKind.String
                && code.ValueEquals(Code);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}