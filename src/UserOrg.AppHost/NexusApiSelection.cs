using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace UserOrg.AppHost;

/// <summary>
/// The NexusApi of a run (the local container or an external NexusApi), resolved
/// <em>after</em> <c>Build()</c>, when <see cref="RunSourcesDialog"/> has been answered.
/// </summary>
/// <remarks>
/// The same shape as <see cref="DatabaseSourceSelection"/> and <see cref="IdentityProviderSelection"/>: a run-time
/// answer cannot decide which resources the model contains (T-071 E1), so both modes are always registered,
/// the container chain is explicit-start, and the API's <c>nexusapi</c> variables are written from a resource-start
/// callback that awaits the answer. The answer completes before the container starts: the API does not wait for
/// NexusApi (Q1).
/// </remarks>
internal sealed class NexusApiSelection(RunSourcesDialog dialog)
{
    private readonly TaskCompletionSource<NexusApiChoice> _resolved = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly List<ParameterResource> _containerSecrets = [];

    public Task<NexusApiChoice> ChoiceAsync(CancellationToken cancellationToken) =>
        _resolved.Task.WaitAsync(cancellationToken);

    /// <summary>Saves <paramref name="parameter"/> to user secrets once the local container is chosen, and never for the external NexusApi.</summary>
    public void SaveWhenContainerChosen(ParameterResource parameter) => _containerSecrets.Add(parameter);

    /// <summary>
    /// Resolves the choice in the background and starts <paramref name="containerChain"/>, in order, when the
    /// container is the one chosen. Call after <c>Build()</c> and before <c>RunAsync()</c>.
    /// </summary>
    public void ApplyTo(DistributedApplication app, IReadOnlyList<IResource> containerChain, ParameterResource tokenExchangeEnabled)
    {
        // Publish mode builds the external service at model-build time and registers no callback awaiting this.
        if (app.Services.GetRequiredService<DistributedApplicationExecutionContext>().IsPublishMode)
        {
            _resolved.TrySetResult(new NexusApiChoice(NexusApiMode.External));
            return;
        }

        var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<NexusApiSelection>();

        _ = Task.Run(() => ResolveAndStartAsync(app, containerChain, tokenExchangeEnabled, logger, lifetime.ApplicationStopping));
    }

    private async Task ResolveAndStartAsync(
        DistributedApplication app,
        IReadOnlyList<IResource> containerChain,
        ParameterResource tokenExchangeEnabled,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var configuration = app.Services.GetRequiredService<IConfiguration>();
            var mode = (await dialog.AnswersAsync(app.Services, cancellationToken)).NexusApi;

            if (mode != NexusApiMode.Container)
            {
                var serviceAccount = NexusApiModes.ParseTokenExchangeEnabled(await tokenExchangeEnabled.GetValueAsync(cancellationToken));
                var (baseUrl, clientId, secret) = (
                    configuration["Parameters:NexusApiBaseUrl"],
                    configuration["Parameters:NexusApiServiceAccountClient"],
                    configuration["Parameters:NexusApiServiceAccountSecret"]);

                if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out _) || (serviceAccount && (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(secret))))
                {
                    (baseUrl, clientId, secret) = await NexusApiPrompt.AskExternalAsync(app.Services, baseUrl, clientId, secret, serviceAccount, cancellationToken);
                }

                if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
                {
                    throw new InvalidOperationException(
                        $"The external NexusApi needs an absolute Parameters:NexusApiBaseUrl; '{baseUrl}' is not one.");
                }

                if (serviceAccount && (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(secret)))
                {
                    throw new InvalidOperationException(
                        "The external NexusApi needs Parameters:NexusApiServiceAccountClient and Parameters:NexusApiServiceAccountSecret.");
                }

                _resolved.TrySetResult(new NexusApiChoice(mode, uri, serviceAccount ? clientId : null, serviceAccount ? secret : null));
                logger.LogInformation("NexusApi: the external one. The NexusApi container stays stopped.");
                return;
            }

            _resolved.TrySetResult(new NexusApiChoice(mode));

            var userSecrets = app.Services.GetService<IUserSecretsManager>();
            foreach (var parameter in _containerSecrets.Where(p => configuration[$"Parameters:{p.Name}"] is null))
            {
                userSecrets?.TrySetSecret($"Parameters:{parameter.Name}", await parameter.GetValueAsync(cancellationToken) ?? string.Empty);
            }

            logger.LogInformation("NexusApi: the local container. Starting its Postgres, migrate, import and server.");

            var notifications = app.Services.GetRequiredService<ResourceNotificationService>();
            var commands = app.Services.GetRequiredService<ResourceCommandService>();

            foreach (var resource in containerChain)
            {
                await notifications.WaitForResourceAsync(resource.Name, KnownResourceStates.NotStarted, cancellationToken);

                var result = await commands.ExecuteCommandAsync(resource, KnownResourceCommands.StartCommand, cancellationToken);

                if (!result.Success)
                {
                    throw new InvalidOperationException($"Could not start '{resource.Name}': {result.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The app host is shutting down before the dialog was answered; nothing left to start.
        }
        catch (Exception exception)
        {
            _resolved.TrySetException(exception);
            logger.LogError(exception, "Could not resolve or start NexusApi; the API's HEAppE calls answer 503.");
        }
    }
}

/// <summary>The resolved NexusApi; <see cref="BaseUrl"/>, <see cref="ClientId"/> and <see cref="ServiceAccountSecret"/> are set only for the external one in run mode.</summary>
internal sealed record NexusApiChoice(NexusApiMode Mode, Uri? BaseUrl = null, string? ClientId = null, string? ServiceAccountSecret = null);
