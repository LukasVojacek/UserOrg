namespace Userorg.Adapter.Waldur.Sync;

/// <summary>
/// Per-instance service-account identity (plan §Options / D18). This account is the
/// CreatedBy/AssignedBy identity for all sync writes of a given instance.
/// </summary>
/// <param name="ServiceName">The upsert discriminator ("EFP-Waldur" / "Puhuri-Waldur"); also the account's IdentityProviderUserId.</param>
/// <param name="ServiceAccountUserName">The service-account user name.</param>
/// <param name="ServiceAccountEmail">The service-account email.</param>
internal sealed record WaldurServiceAccountIdentity(
    string ServiceName,
    string ServiceAccountUserName,
    string ServiceAccountEmail);