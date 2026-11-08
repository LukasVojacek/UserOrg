using E2E.Fixtures;

using ParityTests.Spec;

namespace ParityTests;

/// <summary>
/// Maps a <see cref="Role"/> to its dev-auth <c>GatewayAttachFixture</c> client factory. Shared by
/// B06's by-name and by-id parity theories so the role→client wiring lives once, not duplicated
/// per test class (B05's <c>RegressedListEndpointsParityTests</c> predates this helper and keeps
/// its own copy — out of this block's scope to touch).
/// </summary>
internal static class RoleClientFactory
{
    public static async Task<HttpClient> CreateClientAsync(this GatewayAttachFixture fixture, Role role, CancellationToken ct) => role switch
    {
        Role.Admin => fixture.CreateAdminGatewayClient(),
        Role.Owner => await fixture.CreateProjectOwnerClientAsync(ct),
        Role.Manager => await fixture.CreateProjectManagerClientAsync(ct),
        Role.Member => await fixture.CreateProjectMemberClientAsync(ct),
        Role.Anonymous => fixture.CreateAnonymousGatewayClient(),
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unsupported parity role."),
    };
}
