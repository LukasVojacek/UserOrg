using System.Reflection;

using Xunit;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// Pins that the gateway catalog declares every action and reason any gateway block writes (D16), so the
/// blocks that write them only reference the names. Read by value, because the names are not fixed.
/// </summary>
public sealed class GatewayAuditActionsCatalogTests
{
    private static HashSet<string> ConstantValuesOf(Type type) =>
        type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(field => field is { IsLiteral: true } && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void Should_DeclareEveryGatewayAction()
    {
        string[] expected =
        [
            "gateway.authorization.denied",
            "gateway.authorization.bypassed",
            "gateway.role_management.signed_in",
            "gateway.role_management.signed_out",
            "gateway.rbac_role.created",
            "gateway.rbac_role.updated",
            "gateway.rbac_permission.created",
            "gateway.rbac_permission.deleted",
            "gateway.endpoint_mapping.created",
            "gateway.endpoint_mapping.updated",
            "gateway.endpoint_mapping.deleted",
            "gateway.rbac.imported",
            "gateway.rbac.exported",
            "gateway.rbac.seeded",
            "gateway.signing_key.generated",
            "gateway.signing_key.rotated",
            "gateway.api_key.authenticated",
            "gateway.dev_override.authenticated",
        ];

        var declared = ConstantValuesOf(typeof(GatewayAuditActions));

        Assert.Empty(expected.Except(declared));
    }

    [Fact]
    public void Should_DeclareEveryGatewayReason()
    {
        string[] expected =
        [
            "gateway.no_permission_mapping",
            "gateway.permission_lookup_failed",
            "gateway.required_ddi_permission",
            "gateway.required_provider_permission",
            "gateway.required_service_permission",
            "gateway.insufficient_permissions",
            "gateway.insufficient_project_permissions",
            "gateway.permission_service_missing",
            "gateway.role_management_sign_in_failed",
            "gateway.api_key_missing",
            "gateway.api_key_lookup_timed_out",
            "gateway.api_key_invalid",
        ];

        var declared = ConstantValuesOf(typeof(GatewayAuditActions.DenialReasons));

        Assert.Empty(expected.Except(declared));
    }
}
