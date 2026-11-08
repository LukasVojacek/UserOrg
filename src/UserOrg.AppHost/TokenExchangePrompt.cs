using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace UserOrg.AppHost;

/// <summary>The exchange client the api uses against the external identity provider with FeatureTokenExchangeEnabled on.</summary>
internal sealed record TokenExchangeClient(string Authority, string TokenExchangeClientId, string TokenExchangeClientSecret);

internal static class TokenExchangePrompt
{
    /// <summary>
    /// Reads <c>Parameters:TokenExchangeAuthority</c>, <c>Parameters:TokenExchangeClientId</c> and
    /// <c>Parameters:TokenExchangeClientSecret</c>; when one is missing, asks for all three, prefilled with what is
    /// configured and the authority defaulting to <paramref name="idpAuthority"/>, and saves the answers to user
    /// secrets when "Save to user secrets" is checked. Only called with the token exchange flag on.
    /// </summary>
    public static async Task<TokenExchangeClient> ResolveAsync(IServiceProvider services, string? idpAuthority, CancellationToken cancellationToken)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var authority = configuration["Parameters:TokenExchangeAuthority"];
        var clientId = configuration["Parameters:TokenExchangeClientId"];
        var secret = configuration["Parameters:TokenExchangeClientSecret"];

        if (Uri.TryCreate(authority, UriKind.Absolute, out _) && !string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(secret))
        {
            return new TokenExchangeClient(authority, clientId, secret);
        }

        var interactionService = services.GetRequiredService<IInteractionService>();
        if (!interactionService.IsAvailable)
        {
            throw new DistributedApplicationException(
                "FeatureTokenExchangeEnabled needs Parameters:TokenExchangeAuthority, Parameters:TokenExchangeClientId and "
                + "Parameters:TokenExchangeClientSecret in user secrets (dotnet user-secrets set ... --project src/UserOrg.AppHost), "
                + "and there is no dashboard to ask for them.");
        }

        var result = await interactionService.PromptInputsAsync(
            title: "Token exchange client",
            message: "The realm and the confidential client the api exchanges the caller's token with. Check \"Save to user secrets\" to skip this dialog on the next run.",
            inputs:
            [
                new InteractionInput
                {
                    Name = "TokenExchangeAuthority",
                    Label = "Token Exchange Authority",
                    InputType = InputType.Text,
                    Required = true,
                    Value = string.IsNullOrWhiteSpace(authority) ? idpAuthority : authority,
                    Placeholder = "e.g. https://aai.dev.lexis.tech/auth/realms/LEXIS_AAI_v2_DEV"
                },
                new InteractionInput
                {
                    Name = "TokenExchangeClientId",
                    Label = "Token Exchange Client ID",
                    InputType = InputType.Text,
                    Required = true,
                    Value = clientId,
                    Placeholder = "e.g. userorg-token-exchange"
                },
                new InteractionInput
                {
                    Name = "TokenExchangeClientSecret",
                    Label = "Token Exchange Client Secret",
                    InputType = InputType.SecretText,
                    Required = true,
                    Value = secret
                },
                new InteractionInput
                {
                    Name = "SaveToUserSecrets",
                    Label = "Save to user secrets",
                    InputType = InputType.Boolean,
                    Value = "false"
                }
            ],
            cancellationToken: cancellationToken);

        if (result.Canceled)
        {
            throw new DistributedApplicationException(
                "The token exchange dialog was dismissed; the api cannot exchange tokens. Restart the app host.");
        }

        var answer = new TokenExchangeClient(
            result.Data["TokenExchangeAuthority"].Value ?? string.Empty,
            result.Data["TokenExchangeClientId"].Value ?? string.Empty,
            result.Data["TokenExchangeClientSecret"].Value ?? string.Empty);

        var userSecrets = services.GetService<IUserSecretsManager>();
        if (userSecrets is { IsAvailable: true } && bool.TryParse(result.Data["SaveToUserSecrets"].Value, out var save) && save)
        {
            userSecrets.TrySetSecret("Parameters:TokenExchangeAuthority", answer.Authority);
            userSecrets.TrySetSecret("Parameters:TokenExchangeClientId", answer.TokenExchangeClientId);
            userSecrets.TrySetSecret("Parameters:TokenExchangeClientSecret", answer.TokenExchangeClientSecret);
        }

        return answer;
    }
}
