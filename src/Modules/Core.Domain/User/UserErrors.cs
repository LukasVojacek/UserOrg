using Lukdrasil.Error;

namespace Core.Domain.User;

internal static class UserErrors
{
    internal static Error AdminRoleCannotBeChanged() =>
        new ErrorBuilder()
            .WithCode(UserErrorCodes.AdminRoleImmutable)
            .WithDescription("Admin role cannot be changed via role promotion or demotion.")
            .WithSeverity(ErrorSeverity.Error)
            .Build();

    internal static Error ProviderAdminRequiresAtLeastOneProvider() =>
        new ErrorBuilder()
            .WithCode(UserErrorCodes.ProviderAdminRequiresProvider)
            .WithDescription("ProviderAdmin must be assigned to at least one provider.")
            .WithSeverity(ErrorSeverity.Error)
            .Build();

    internal static Error AlreadyAssignedToProvider(ProviderId providerId) =>
        new ErrorBuilder()
            .WithCode(UserErrorCodes.AlreadyAssignedToProvider)
            .WithDescription("User is already assigned to provider '{0}'.")
            .WithSeverity(ErrorSeverity.Error)
            .Build(providerId.Value);

    internal static Error NotAssignedToProvider(ProviderId providerId) =>
        new ErrorBuilder()
            .WithCode(UserErrorCodes.NotAssignedToProvider)
            .WithDescription("User is not assigned to provider '{0}'.")
            .WithSeverity(ErrorSeverity.Error)
            .Build(providerId.Value);

    internal static Error CannotDemoteWithActiveProviderAssignments() =>
        new ErrorBuilder()
            .WithCode(UserErrorCodes.CannotDemoteWithProviders)
            .WithDescription("Cannot demote ProviderAdmin while still assigned to one or more providers.")
            .WithSeverity(ErrorSeverity.Error)
            .Build();

    internal static Error RoleNotResolved(string roleName) =>
        new ErrorBuilder()
            .WithCode(UserErrorCodes.RoleNotResolved)
            .WithDescription("Role '{0}' could not be resolved from the role service.")
            .WithSeverity(ErrorSeverity.Error)
            .Build(roleName);
}