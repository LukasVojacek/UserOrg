using Core.Domain.OnboardingRequest;
using Core.Domain.Request;

using Lukdrasil.Error;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Core.UnitTests.Shared;

public sealed class DomainErrorResultsStatusTableTests
{
    public static TheoryData<string, int> MappedCodes => new()
    {
        { ProjectErrorCodes.ReadOnly, StatusCodes.Status409Conflict },
        { ProjectErrorCodes.UserAlreadyAssigned, StatusCodes.Status409Conflict },
        { ProjectErrorCodes.UserNotAssigned, StatusCodes.Status404NotFound },
        { ProjectErrorCodes.DuplicateProjectOwner, StatusCodes.Status409Conflict },
        { ProjectErrorCodes.OwnerCannotBeUnassigned, StatusCodes.Status409Conflict },
        { ProjectErrorCodes.CannotAssignEqualOrHigherRole, StatusCodes.Status403Forbidden },
        { ProjectErrorCodes.ResourceAlreadyAssigned, StatusCodes.Status409Conflict },
        { ProjectErrorCodes.ResourceNotAssigned, StatusCodes.Status404NotFound },

        { ProjectTagErrorCodes.TagInUse, StatusCodes.Status409Conflict },
        { ProjectTagErrorCodes.AlreadyDeleted, StatusCodes.Status409Conflict },
        { ProjectTagErrorCodes.TagNotFound, StatusCodes.Status404NotFound },

        { ProviderErrorCodes.ProviderReadOnly, StatusCodes.Status409Conflict },
        { ProviderErrorCodes.AlreadyDeactivated, StatusCodes.Status409Conflict },
        { ProviderErrorCodes.NotDeactivated, StatusCodes.Status409Conflict },
        { ProviderErrorCodes.NameAlreadyExists, StatusCodes.Status409Conflict },
        { ProviderErrorCodes.LocationAlreadyExists, StatusCodes.Status409Conflict },
        { ProviderErrorCodes.LocationAlreadyDeleted, StatusCodes.Status409Conflict },
        { ProviderErrorCodes.LocationNotFound, StatusCodes.Status404NotFound },
        { ProviderErrorCodes.AssignedLocationNotFound, StatusCodes.Status404NotFound },
        { ProviderErrorCodes.AssignedLocationAlreadyDeleted, StatusCodes.Status409Conflict },

        { UserErrorCodes.AdminRoleImmutable, StatusCodes.Status409Conflict },
        { UserErrorCodes.ProviderAdminRequiresProvider, StatusCodes.Status422UnprocessableEntity },
        { UserErrorCodes.CannotDemoteWithProviders, StatusCodes.Status409Conflict },
        { UserErrorCodes.AlreadyAssignedToProvider, StatusCodes.Status409Conflict },
        { UserErrorCodes.NotAssignedToProvider, StatusCodes.Status404NotFound },
        { UserErrorCodes.RoleNotResolved, StatusCodes.Status502BadGateway },

        { RequestErrorCodes.AlreadyActiveMember, StatusCodes.Status409Conflict },
        { RequestErrorCodes.DuplicatePendingRequest, StatusCodes.Status409Conflict },
        { RequestErrorCodes.InvalidStateTransition, StatusCodes.Status409Conflict },
        { RequestErrorCodes.AlreadyDeleted, StatusCodes.Status409Conflict },
        { RequestErrorCodes.NotDeleted, StatusCodes.Status409Conflict },
        { RequestErrorCodes.UnsupportedType, StatusCodes.Status422UnprocessableEntity },

        { OnboardingRequestErrorCodes.InvalidState, StatusCodes.Status409Conflict },
        { OnboardingRequestErrorCodes.AlreadyActiveMember, StatusCodes.Status409Conflict },
        { OnboardingRequestErrorCodes.DuplicatePendingRequest, StatusCodes.Status409Conflict },
        { OnboardingRequestErrorCodes.AlreadyDeleted, StatusCodes.Status409Conflict },
        { OnboardingRequestErrorCodes.NotDeleted, StatusCodes.Status409Conflict },
    };

    public static TheoryData<string> UnmappedCodes =>
    [
        ProjectErrorCodes.EndDateBeforeStartDate,
        ProjectErrorCodes.ShortNameInvalid,
        ProjectErrorCodes.NameInvalid,
        ProjectErrorCodes.DescriptionTooLong,
        ProjectErrorCodes.ContactEmailInvalid,
        ProjectErrorCodes.DomainTypeNameInvalid,
        ProjectErrorCodes.DomainTypeNotFound,
        ProjectErrorCodes.TagNameInvalid,
        ProjectErrorCodes.UnknownRole,
        ProviderErrorCodes.LocationReadOnly,
        ProviderErrorCodes.AssignmentReadOnly,
        ProviderErrorCodes.ResourceAlreadyHasNonSharedLocation,
        ProviderErrorCodes.NonSharedLocationAlreadyHasResource,
        ProviderErrorCodes.ResourceNotUnderProvider,
        ProviderErrorCodes.NoLocationAssignments,
        ProviderErrorCodes.InvalidName,
        ProviderErrorCodes.InvalidLocationName,
        ProviderErrorCodes.InvalidEndpoint,
        ProviderErrorCodes.CountryNotFound,
        ProviderErrorCodes.InvalidLocationType,
        UserErrorCodes.Deactivated,
        RequestErrorCodes.InvalidType,
        RequestErrorCodes.InvalidState,
        RequestErrorCodes.NotFound,
        RequestErrorCodes.NotPending,
        RequestErrorCodes.Expired,
        RequestErrorCodes.InvalidActionLink,
        OnboardingRequestErrorCodes.NotFound,
        "not.a_domain_code",
        "",
    ];

    [Theory]
    [MemberData(nameof(MappedCodes))]
    public void Should_MapToItsStatusAndKeepTheCode_When_CodeIsMapped(string code, int expectedStatus)
    {
        var problem = Assert.IsType<ProblemHttpResult>(ErrorWith(code).ToProblem());

        Assert.Equal(expectedStatus, problem.StatusCode);
        Assert.Equal(code, problem.ProblemDetails.Extensions["code"]);
        Assert.Equal("The description.", problem.ProblemDetails.Detail);
    }

    [Theory]
    [MemberData(nameof(UnmappedCodes))]
    public void Should_MapTo400AndKeepTheCode_When_CodeIsNotMapped(string code)
    {
        var problem = Assert.IsType<ProblemHttpResult>(ErrorWith(code).ToProblem());

        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(code, problem.ProblemDetails.Extensions["code"]);
        Assert.Equal("The description.", problem.ProblemDetails.Detail);
    }

    [Fact]
    public void Should_MapTo400WithANullCode_When_CodeIsNull()
    {
        var problem = Assert.IsType<ProblemHttpResult>(ErrorWith(null!).ToProblem());

        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.True(problem.ProblemDetails.Extensions.ContainsKey("code"));
        Assert.Null(problem.ProblemDetails.Extensions["code"]);
        Assert.Equal("The description.", problem.ProblemDetails.Detail);
    }

    private static Error ErrorWith(string code) => new(code, ErrorSeverity.Error, "The description.");
}
