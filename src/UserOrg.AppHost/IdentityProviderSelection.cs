using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace UserOrg.AppHost;

/// <summary>
/// The identity provider of a run — the local Keycloak container or the external authority — resolved
/// <em>after</em> <c>Build()</c>, when <see cref="RunSourcesDialog"/> has been answered.
/// </summary>
/// <remarks>
/// <para>
/// The same shape as <see cref="DatabaseSourceSelection"/> and for the same reason: a run-time answer cannot
/// decide which resources the model contains (T-071 E1). The Keycloak container and the external identity
/// provider's parameters are therefore both always registered, Keycloak is explicit-start, and every
/// <c>Idp__*</c> environment variable is written from a resource-start callback that awaits the answer.
/// </para>
/// <para>
/// The answer completes only once Keycloak is healthy, which is what the removed <c>gateway.WaitFor(keycloak)</c>
/// edge used to guarantee: the gateway's environment callback awaits this task, so the gateway cannot start
/// against a realm that has not been imported yet.
/// </para>
/// </remarks>
internal sealed class IdentityProviderSelection(RunSourcesDialog dialog)
{
    /// <summary>The Keycloak container in this app host.</summary>
    public const string LocalKeycloak = nameof(LocalKeycloak);

    /// <summary>The identity provider behind the <c>IdpAuthority</c> parameter.</summary>
    public const string ExternalAuthority = nameof(ExternalAuthority);

    private readonly TaskCompletionSource<bool> _resolved = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Whether this run authenticates against the local Keycloak container. Completes once the choice is in
    /// and, for the local container, once Keycloak is healthy.
    /// </summary>
    public Task<bool> UseLocalKeycloakAsync(CancellationToken cancellationToken) =>
        _resolved.Task.WaitAsync(cancellationToken);

    /// <summary>
    /// Resolves the choice in the background and starts Keycloak when it is the one chosen. Call after
    /// <c>Build()</c> and before <c>RunAsync()</c>.
    /// </summary>
    public void ApplyTo(DistributedApplication app, IResourceBuilder<ContainerResource> keycloak)
    {
        // `aspire publish` and `aspire deploy` build the model, ask every callback for its values and
        // exit; no orchestrator ever runs, so nothing publishes the resource states the starts below wait
        // for and a start would hang the publish forever. The answer still has to arrive, because the
        // environment callbacks the publisher runs are waiting on it.
        if (app.Services.GetRequiredService<DistributedApplicationExecutionContext>().IsPublishMode)
        {
            // The external authority, the same answer RunSourcesDialog gives without a dashboard:
            // the Keycloak container is excluded from the manifest, so a published topology has no local
            // identity provider to point at.
            _resolved.TrySetResult(false);
            return;
        }

        var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<IdentityProviderSelection>();

        _ = Task.Run(() => ResolveAndStartAsync(app, keycloak, logger, lifetime.ApplicationStopping));
    }

    private async Task ResolveAndStartAsync(
        DistributedApplication app,
        IResourceBuilder<ContainerResource> keycloak,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var useLocalKeycloak = (await dialog.AnswersAsync(app.Services, cancellationToken)).UseLocalKeycloak;

            if (useLocalKeycloak)
            {
                logger.LogInformation("Identity provider: the local Keycloak container. Starting it, then importing the realm.");

                var notifications = app.Services.GetRequiredService<ResourceNotificationService>();
                var commands = app.Services.GetRequiredService<ResourceCommandService>();

                await notifications.WaitForResourceAsync(keycloak.Resource.Name, KnownResourceStates.NotStarted, cancellationToken);

                var result = await commands.ExecuteCommandAsync(keycloak.Resource, KnownResourceCommands.StartCommand, cancellationToken);

                if (!result.Success)
                {
                    throw new InvalidOperationException($"Could not start '{keycloak.Resource.Name}': {result.Message}");
                }

                // Only now, so the gateway's environment callback - which awaits this task - cannot start the
                // gateway against a realm Keycloak has not finished importing.
                await notifications.WaitForResourceHealthyAsync(keycloak.Resource.Name, cancellationToken);
            }
            else
            {
                logger.LogInformation("Identity provider: the external authority. The Keycloak container stays stopped.");
            }

            _resolved.TrySetResult(useLocalKeycloak);
        }
        catch (OperationCanceledException)
        {
            // The app host is shutting down before the dialog was answered; nothing left to start.
        }
        catch (Exception exception)
        {
            _resolved.TrySetException(exception);
            logger.LogError(exception, "Could not resolve the identity provider; userorg and gateway cannot be wired to one.");
        }
    }
}