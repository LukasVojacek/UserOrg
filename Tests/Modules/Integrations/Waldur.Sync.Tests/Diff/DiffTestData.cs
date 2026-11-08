using UserOrg.Shared.Hashing;

using Waldur.Contracts.Payloads;
using Waldur.Sync.Diff;

namespace Waldur.Sync.Tests.Diff;

/// <summary>Builders + constants shared by the <see cref="DiffEngine"/> matrix tests.</summary>
internal static class DiffTestData
{
    public const string Instance = "EFP";
    public const string ServiceName = "EFP-Waldur";

    public const string ProviderId = "11111111-1111-1111-1111-111111111111";
    public const string ProjectId = "22222222-2222-2222-2222-222222222222";

    public static DiffEngine Engine() => new(Instance, ServiceName);

    public static ProviderPayload Provider(string externalId = ProviderId, string name = "Provider A") =>
        new(externalId, name, "Czechia", null, []);

    public static ProjectPayload Project(
        string externalId = ProjectId,
        string name = "Project A",
        bool isRemoved = false) =>
        new(externalId, name, "proj-a", null, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            DateTime.MaxValue, isRemoved, []);

    public static ProjectUserPayload Member(
        string userName = "alice",
        string role = "Member",
        string projectExternalId = ProjectId) =>
        new(projectExternalId, userName, role, HasAccess: true);

    public static StorageConfigPayload StorageConfig(string efpCoreServiceName = "EFP-Core") =>
        new(efpCoreServiceName, UseSharedIrodsForAllEntities: false, SharedIrods: null,
            IrodsInstances: [], StagingAreaInstances: []);

    public static ShaBreadcrumbMap MapWith(params (ShaKey Key, IIdentityHashed Payload)[] entries)
    {
        var map = new ShaBreadcrumbMap();
        foreach (var (key, payload) in entries)
        {
            map.Apply(ShaMutation.Set(key, payload.ComputeIdentityHash()));
        }

        return map;
    }
}
