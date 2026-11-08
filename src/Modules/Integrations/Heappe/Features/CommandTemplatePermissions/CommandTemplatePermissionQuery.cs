using Heappe.Features.CommandTemplatePermissions.Response;

using Lukdrasil.Error;
using Lukdrasil.Result;

using UserOrg.Shared.Abstractions.Interfaces;

namespace Heappe.Features.CommandTemplatePermissions;

/// <summary>
/// Represents a query to retrieve a user's permissions for a specific command template.
/// </summary>
/// <param name="IdpUserId">The unique identifier of the user in the identity provider. Cannot be null or empty.</param>
/// <param name="HeappeIdentifier">The identifier of the command template for which permissions are being queried. Cannot be null or empty.</param>
internal sealed record CommandTemplatePermissionQuery
(
    string IdpUserId,
    string HeappeIdentifier
) : IRequest<Result<UserCommandTemplatePermissionModel, Error>>;