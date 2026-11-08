using Lukdrasil.Error;

namespace Userorg.Adapter.Waldur.Base;

/// <summary>
/// Defines well-known error instances for the Waldur adapter layer.
/// Each factory method returns a descriptive <see cref="Error"/> built with <see cref="ErrorBuilder"/>.
/// </summary>
internal static class Errors
{
    internal static Error EnsureProjectFailed(string projectSlug, string reason) => new ErrorBuilder()
        .WithDescription("Failed to ensure project '{0}' in Userorg: {1}")
        .WithSeverity(ErrorSeverity.Error)
        .Build(projectSlug, reason);

    internal static Error EnsureProviderFailed(string providerName, string reason) => new ErrorBuilder()
        .WithDescription("Failed to ensure provider '{0}' in Userorg: {1}")
        .WithSeverity(ErrorSeverity.Error)
        .Build(providerName, reason);

    internal static Error EnsureUserFailed(string userName, string reason) => new ErrorBuilder()
        .WithDescription("Failed to ensure user '{0}' in Userorg: {1}")
        .WithSeverity(ErrorSeverity.Error)
        .Build(userName, reason);

    internal static Error ServiceAccountNotBootstrapped(string integrationName, string serviceName) => new ErrorBuilder()
        .WithDescription("{0}: service account '{1}' is not bootstrapped.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(integrationName, serviceName);

    internal static Error FailedToSaveWaldurServiceAccount(string serviceName) => new ErrorBuilder()
        .WithDescription("Failed to save Waldur service account '{0}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(serviceName);

    internal static Error ProjectPartiallyImported(string projectSlug, IList<Error> resourceFailures) => new ErrorBuilder()
        .WithDescription("Project '{0}' imported with {1} failed resource(s): {2}")
        .WithSeverity(ErrorSeverity.Warning)
        .Build(projectSlug, resourceFailures.Count, string.Join("; ", resourceFailures.Select(e => e.Description)));
}