namespace UserOrg.AppHost;

internal static class HashiVaultExtensions
{
    public static (IResourceBuilder<ContainerResource> proxy, VaultSetup setup) AddHashiVault(this IDistributedApplicationBuilder builder)
    {
        var vaultDevRootToken = builder.AddParameter("VaultDevRootToken", secret: true)
            .WithCustomInput(parameter => new()
            {
                Name = "Vault Dev Root Token",
                Description = "The root token for the Vault development instance.",
                InputType = InputType.SecretText,
                Required = true,
                Value = "root",
                Placeholder = "e.g. root"
            });

        var vault = builder.AddContainer("Vault", "hashicorp/vault")
            .WithImageTag("2.1.0")
            .WithLifetime(ContainerLifetime.Persistent)
            .WithEnvironment("VAULT_DEV_ROOT_TOKEN_ID", vaultDevRootToken)
            .WithContainerRuntimeArgs("--cap-add", "IPC_LOCK")
            .WithHttpEndpoint(targetPort: 8200)
            .WithUrlForEndpoint("http", static url =>
            {
                url.DisplayText = "🗝️ Vault";
                url.DisplayLocation = UrlDisplayLocation.SummaryAndDetails;
            })
            .WithExternalHttpEndpoints();

        vault.WithEnvironment("VAULT_ADDR", vault.GetEndpoint("http"))
            .WithHttpHealthCheck($"{vault.GetEndpoint("http")}/v1/sys/health");

        // The bind mount source has to exist before the container is created, or the container runtime
        // creates it as root and the app host process can no longer write the credentials into it.
        VaultSetup.EnsureCredentialsDirectory();

        var vaultProxy = builder.AddContainer("VaultProxy", "hashicorp/vault")
            .WithImageTag("2.1.0")
            // The agent reads role_id/secret_id once, at auto-auth. Starting it before the app host has
            // issued them means AppRole auth fails, the API gets no secrets and the gateway 500s for the
            // whole readiness window. Explicit start plus the ApplyTo call after Build() replaces the
            // WaitForCompletion(VaultSeed) edge that used to carry that guarantee: the credentials are
            // written first, the proxy is started afterwards, from the app host process.
            .WithExplicitStart()
            .WithContainerFiles(
                "/vault/config",
                [
                    new ContainerFile
                    {
                        Name = "vault-agent.hcl",
                        Contents = VaultSetup.AgentConfig.AsContainerFileContents(builder)
                    }
                ])
            .WithBindMount(VaultSetup.CredentialsDirectory, "/vault/creds", isReadOnly: true)
            .WithArgs("proxy", "-config=/vault/config/vault-agent.hcl")
            .WithContainerRuntimeArgs("--cap-add", "IPC_LOCK")
            .WithHttpEndpoint(targetPort: 8100)
            .WithUrlForEndpoint("http", static url =>
            {
                url.DisplayText = "🛡️ Vault agent proxy";
                url.DisplayLocation = UrlDisplayLocation.DetailsOnly;
            })
            .WithExternalHttpEndpoints()
            .WithLifetime(ContainerLifetime.Persistent)
            // Running is not ready. `vault proxy` serves its listeners before auto-auth has even been
            // started (command/proxy.go starts the listener goroutines, then the auth handler), and
            // `use_auto_auth_token = "force"` does not hold a request back: ProxyHandler only strips the
            // caller's own X-Vault-Token header and then reads the auto-auth sink without blocking,
            // forwarding with an empty token when auto-auth has not produced one yet. So the proxy answers
            // long before AppRole auth completes, and every unauthenticated endpoint answers 200 with it -
            // /v1/sys/health included. This probe is therefore an *authenticated* path: the AppRole token
            // carries Vault's `default` policy, which grants exactly `auth/token/lookup-self`, while an
            // empty token gets 400 "missing client token". A 200 here means a token exists, which means
            // auto-auth finished.
            //
            // That is what `api.WaitFor(vaultProxy)` needs: the API reads Vault once, eagerly,
            // at host build, and VaultConfigurationProvider.Load() writes every failure to stderr and
            // carries on - so releasing that edge early is an API running with no
            // Vault secrets at all and nothing but one stderr line to say so.
            //
            // Path-only overload, like audit-proxy's: VaultProxy declares a single endpoint, the "http"
            // one from WithHttpEndpoint(targetPort: 8100), which is the one this resolves against. The
            // Vault server above builds a full URL instead because it also feeds VAULT_ADDR from the same
            // expression.
            .WithHttpHealthCheck("/v1/auth/token/lookup-self");

        var setup = new VaultSetup(vault, vaultDevRootToken, vaultProxy);
        vault.WithVaultSetupCommand(setup);

        return (vaultProxy, setup);
    }
}