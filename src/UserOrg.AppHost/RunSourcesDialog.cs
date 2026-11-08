using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace UserOrg.AppHost;

internal sealed class RunSourcesDialog(bool forceLocalContainer, bool forceLocalKeycloak)
{
    private const string DatabaseSourceInput = "DatabaseSource";

    private const string IdentityProviderInput = "IdentityProvider";

    private const string NexusApiInput = "NexusApiSource";

    private readonly Lock _gate = new();

    private Task<RunSources>? _answers;

    public Task<RunSources> AnswersAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return _answers ??= ResolveAsync(services, cancellationToken);
        }
    }

    private async Task<RunSources> ResolveAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var useLocalContainer = forceLocalContainer ? true : ParseDatabaseSource(configuration["Parameters:DatabaseSource"]);
        var useLocalKeycloak = forceLocalKeycloak ? true : ParseIdentityProvider(configuration["Parameters:UseLocalAuthority"]);
        var nexusApi = NexusApiModes.Parse(configuration["Parameters:NexusApiSource"]);

        List<InteractionInput> inputs = [];

        if (useLocalContainer is null)
        {
            inputs.Add(new InteractionInput
            {
                Name = DatabaseSourceInput,
                Label = "Database source",
                Description = "The local SQL container is restored from a backup and migrated by this app host. The external database is reached through the UserorgDev connection string.",
                InputType = InputType.Choice,
                Required = true,
                Value = DatabaseSourceSelection.LocalContainer,
                Options =
                [
                    new(DatabaseSourceSelection.LocalContainer, "Local SQL container"),
                    new(DatabaseSourceSelection.ExternalConnectionString, "External connection string")
                ]
            });
        }

        if (useLocalKeycloak is null)
        {
            inputs.Add(new InteractionInput
            {
                Name = IdentityProviderInput,
                Label = "Identity provider",
                Description = "The local Keycloak container imports the realm, with a seeded user for every role and the gateway's gettoken command. The external identity provider is reached through the IdpAuthority parameters.",
                InputType = InputType.Choice,
                Required = true,
                Value = IdentityProviderSelection.LocalKeycloak,
                Options =
                [
                    new(IdentityProviderSelection.LocalKeycloak, "Local Keycloak container"),
                    new(IdentityProviderSelection.ExternalAuthority, "External identity provider")
                ]
            });
        }

        if (nexusApi is null)
        {
            inputs.Add(new InteractionInput
            {
                Name = NexusApiInput,
                Label = "NexusApi",
                Description = "The local NexusApi container has its own Postgres and a registry imported by this app host, with the HEAppE mock as the heappe-mock instance. The external NexusApi is reached through the NexusApiBaseUrl, NexusApiServiceAccountClient and NexusApiServiceAccountSecret parameters.",
                InputType = InputType.Choice,
                Required = true,
                Value = nameof(NexusApiMode.Container),
                Options =
                [
                    new(nameof(NexusApiMode.Container), "Local NexusApi container"),
                    new(nameof(NexusApiMode.External), "External NexusApi (NexusApiBaseUrl)")
                ]
            });
        }

        var answers = await AskAsync(services, inputs, cancellationToken);

        return new RunSources(
            useLocalContainer ?? ParseDatabaseSource(answers?[DatabaseSourceInput].Value) ?? true,
            useLocalKeycloak ?? ParseIdentityProvider(answers?[IdentityProviderInput].Value) ?? false,
            nexusApi ?? NexusApiModes.Parse(answers?[NexusApiInput].Value) ?? NexusApiMode.Container);
    }

    private static async Task<InteractionInputCollection?> AskAsync(
        IServiceProvider services,
        List<InteractionInput> inputs,
        CancellationToken cancellationToken)
    {
        var interactionService = services.GetRequiredService<IInteractionService>();

        if (inputs.Count == 0 || !interactionService.IsAvailable)
        {
            return null;
        }

        var result = await interactionService.PromptInputsAsync(
            title: "Run sources",
            message: "Choose what this run starts locally and what it reaches externally. A value in user secrets or on the command line skips its question.",
            inputs: inputs,
            cancellationToken: cancellationToken);

        return result.Canceled
            ? throw new DistributedApplicationException("The run sources dialog was dismissed; this run has no database, identity provider or NexusApi. Restart the app host.")
            : result.Data;
    }

    private static bool? ParseDatabaseSource(string? value) => value switch
    {
        null or "" => null,
        DatabaseSourceSelection.LocalContainer => true,
        DatabaseSourceSelection.ExternalConnectionString => false,
        _ => throw new InvalidOperationException(
            $"Parameters:DatabaseSource is '{value}'; expected '{DatabaseSourceSelection.LocalContainer}' or '{DatabaseSourceSelection.ExternalConnectionString}'.")
    };

    private static bool? ParseIdentityProvider(string? value) => value switch
    {
        null or "" => null,
        _ when bool.TryParse(value, out var useLocal) => useLocal,
        IdentityProviderSelection.LocalKeycloak => true,
        IdentityProviderSelection.ExternalAuthority => false,
        _ => throw new InvalidOperationException(
            $"Parameters:UseLocalAuthority is '{value}'; expected true, false, '{IdentityProviderSelection.LocalKeycloak}' or '{IdentityProviderSelection.ExternalAuthority}'.")
    };
}

internal sealed record RunSources(bool UseLocalContainer, bool UseLocalKeycloak, NexusApiMode NexusApi);