using System.Collections.Frozen;

using Core.Domain.OnboardingRequest;

using Lukdrasil.Error;

using Microsoft.AspNetCore.Http;

namespace Core.Shared;

/// <summary>
/// Maps a domain <see cref="Error"/> to an HTTP problem response from its stable, aggregate-prefixed
/// error code (project.*, provider.*, request.*). One shared mapper keyed on the code keeps cross-aggregate
/// use-cases (e.g. self-onboarding accept, which settles a Request and grants a Project role) from reaching
/// into a sibling feature slice's presentation helper. The <c>Lukdrasil.Result</c> <c>State</c> enum has no
/// Conflict member, so 409 cannot come from <c>ToHttpResult()</c> — the status is derived here instead.
/// </summary>
internal static class DomainErrorResults
{
    private static readonly FrozenDictionary<string, int> StatusByCode = new Dictionary<string, int>
    {
        [ProjectErrorCodes.ReadOnly] = StatusCodes.Status409Conflict,
        [ProjectErrorCodes.UserAlreadyAssigned] = StatusCodes.Status409Conflict,
        [ProjectErrorCodes.UserNotAssigned] = StatusCodes.Status404NotFound,
        [ProjectErrorCodes.DuplicateProjectOwner] = StatusCodes.Status409Conflict,
        [ProjectErrorCodes.OwnerCannotBeUnassigned] = StatusCodes.Status409Conflict,
        [ProjectErrorCodes.CannotAssignEqualOrHigherRole] = StatusCodes.Status403Forbidden,
        [ProjectErrorCodes.ResourceAlreadyAssigned] = StatusCodes.Status409Conflict,
        [ProjectErrorCodes.ResourceNotAssigned] = StatusCodes.Status404NotFound,

        [ProjectTagErrorCodes.TagInUse] = StatusCodes.Status409Conflict,
        [ProjectTagErrorCodes.AlreadyDeleted] = StatusCodes.Status409Conflict,
        [ProjectTagErrorCodes.TagNotFound] = StatusCodes.Status404NotFound,

        [ProviderErrorCodes.ProviderReadOnly] = StatusCodes.Status409Conflict,
        [ProviderErrorCodes.AlreadyDeactivated] = StatusCodes.Status409Conflict,
        [ProviderErrorCodes.NotDeactivated] = StatusCodes.Status409Conflict,
        [ProviderErrorCodes.NameAlreadyExists] = StatusCodes.Status409Conflict,
        [ProviderErrorCodes.LocationAlreadyExists] = StatusCodes.Status409Conflict,
        [ProviderErrorCodes.LocationAlreadyDeleted] = StatusCodes.Status409Conflict,
        [ProviderErrorCodes.LocationNotFound] = StatusCodes.Status404NotFound,
        [ProviderErrorCodes.AssignedLocationNotFound] = StatusCodes.Status404NotFound,
        [ProviderErrorCodes.AssignedLocationAlreadyDeleted] = StatusCodes.Status409Conflict,

        [UserErrorCodes.AdminRoleImmutable] = StatusCodes.Status409Conflict,
        [UserErrorCodes.ProviderAdminRequiresProvider] = StatusCodes.Status422UnprocessableEntity,
        [UserErrorCodes.CannotDemoteWithProviders] = StatusCodes.Status409Conflict,
        [UserErrorCodes.AlreadyAssignedToProvider] = StatusCodes.Status409Conflict,
        [UserErrorCodes.NotAssignedToProvider] = StatusCodes.Status404NotFound,
        [UserErrorCodes.RoleNotResolved] = StatusCodes.Status502BadGateway,

        [RequestErrorCodes.AlreadyActiveMember] = StatusCodes.Status409Conflict,
        [RequestErrorCodes.DuplicatePendingRequest] = StatusCodes.Status409Conflict,
        [RequestErrorCodes.InvalidStateTransition] = StatusCodes.Status409Conflict,
        [RequestErrorCodes.AlreadyDeleted] = StatusCodes.Status409Conflict,
        [RequestErrorCodes.NotDeleted] = StatusCodes.Status409Conflict,
        [RequestErrorCodes.UnsupportedType] = StatusCodes.Status422UnprocessableEntity,

        [OnboardingRequestErrorCodes.InvalidState] = StatusCodes.Status409Conflict,
        [OnboardingRequestErrorCodes.AlreadyActiveMember] = StatusCodes.Status409Conflict,
        [OnboardingRequestErrorCodes.DuplicatePendingRequest] = StatusCodes.Status409Conflict,
        [OnboardingRequestErrorCodes.AlreadyDeleted] = StatusCodes.Status409Conflict,
        [OnboardingRequestErrorCodes.NotDeleted] = StatusCodes.Status409Conflict,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    internal static IResult ToProblem(this Error error)
    {
        var status = error.Code is { } code && StatusByCode.TryGetValue(code, out var mapped)
            ? mapped
            : StatusCodes.Status400BadRequest;

        return Results.Problem(
            detail: error.Description,
            statusCode: status,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }
}