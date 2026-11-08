using HashiCorpVault.Contracts;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HashiCorpVault;

public static class HashiCorpVaultExtensions
{
    public static IHostApplicationBuilder AddHashiCorpVaultIntegration(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        // Forward to IServiceCollection-based registration so hosts can call either API
        builder.Services.AddHashiCorpVault(builder.Configuration);
        return builder;
    }

    private static IServiceCollection AddHashiCorpVault(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<VaultOptions>(configuration.GetSection(VaultOptions.Section));

        // Named HttpClient "Vault"; platform/host should set BaseAddress where appropriate
        services.AddHttpClient("Vault", cfg =>
        {
            cfg.BaseAddress = new("https+http://vault");
        });

        services.AddTransient<IHashiCorpVaultClient, HashiCorpVaultClient>();

        return services;
    }
}