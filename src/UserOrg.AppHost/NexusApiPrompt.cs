using Microsoft.Extensions.DependencyInjection;

namespace UserOrg.AppHost;

internal static class NexusApiPrompt
{
    /// <summary>
    /// Asks for the external NexusApi's base URL and, when <paramref name="askServiceAccount"/> is set, its
    /// service-account client and secret, prefilled with what user secrets already carry, and saves the answers there
    /// when "Save to user secrets" is checked. Only raised for the external NexusApi when an input it needs is missing,
    /// so a container run never sees these inputs.
    /// </summary>
    public static async Task<(string? BaseUrl, string? ClientId, string? Secret)> AskExternalAsync(
        IServiceProvider services,
        string? baseUrl,
        string? clientId,
        string? secret,
        bool askServiceAccount,
        CancellationToken cancellationToken)
    {
        var interactionService = services.GetRequiredService<IInteractionService>();

        if (!interactionService.IsAvailable)
        {
            throw new DistributedApplicationException(
                "The external NexusApi needs Parameters:NexusApiBaseUrl"
                + (askServiceAccount ? ", Parameters:NexusApiServiceAccountClient and Parameters:NexusApiServiceAccountSecret" : string.Empty)
                + " in user secrets (dotnet user-secrets set ... --project src/UserOrg.AppHost), "
                + "and there is no dashboard to ask for them.");
        }

        List<InteractionInput> inputs =
        [
            new InteractionInput
            {
                Name = "NexusApiBaseUrl",
                Label = "NexusApi Base URL",
                InputType = InputType.Text,
                Required = true,
                Value = baseUrl,
                Placeholder = "e.g. https://nexusapi.dev.lexis.tech"
            }
        ];

        if (askServiceAccount)
        {
            inputs.Add(new InteractionInput
            {
                Name = "NexusApiServiceAccountClient",
                Label = "NexusApi Service Account Client",
                InputType = InputType.Text,
                Required = true,
                Value = clientId
            });
            inputs.Add(new InteractionInput
            {
                Name = "NexusApiServiceAccountSecret",
                Label = "NexusApi Service Account Secret",
                InputType = InputType.SecretText,
                Required = true,
                Value = secret
            });
        }

        inputs.Add(new InteractionInput
        {
            Name = "SaveToUserSecrets",
            Label = "Save to user secrets",
            InputType = InputType.Boolean,
            Value = "false"
        });

        var result = await interactionService.PromptInputsAsync(
            title: "External NexusApi",
            message: "The NexusApi to reach and the service account on it. Check \"Save to user secrets\" to skip this dialog on the next run.",
            inputs: inputs,
            cancellationToken: cancellationToken);

        if (result.Canceled)
        {
            throw new DistributedApplicationException(
                "The external NexusApi dialog was dismissed; this run has no NexusApi. Restart the app host.");
        }

        var answers = (
            BaseUrl: result.Data["NexusApiBaseUrl"].Value,
            ClientId: askServiceAccount ? result.Data["NexusApiServiceAccountClient"].Value : clientId,
            Secret: askServiceAccount ? result.Data["NexusApiServiceAccountSecret"].Value : secret);

        var userSecrets = services.GetService<IUserSecretsManager>();
        if (userSecrets is { IsAvailable: true } && bool.TryParse(result.Data["SaveToUserSecrets"].Value, out var save) && save)
        {
            userSecrets.TrySetSecret("Parameters:NexusApiBaseUrl", answers.BaseUrl ?? string.Empty);
            if (askServiceAccount)
            {
                userSecrets.TrySetSecret("Parameters:NexusApiServiceAccountClient", answers.ClientId ?? string.Empty);
                userSecrets.TrySetSecret("Parameters:NexusApiServiceAccountSecret", answers.Secret ?? string.Empty);
            }
        }

        return answers;
    }
}
