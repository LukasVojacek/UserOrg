using Microsoft.Extensions.Options;

namespace UserOrg.Gateway.Features.InternalToken;

/// <summary>
/// Extension methods for registering the Internal Token feature in the dependency injection container.
/// Configures KeyService and TokenService along with the IInternalTokenProvider abstraction.
/// </summary>
internal static class InternalTokenFeatureExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds the Internal Token feature to the service collection.
        /// Registers KeyService as a hosted service singleton, TokenService and IInternalTokenProvider as singletons.
        /// Loads configuration from "InternalToken" section in appsettings.
        /// </summary>
        /// <param name="services">The service collection to add services to</param>
        /// <param name="configuration">The configuration to bind options from</param>
        /// <returns>The service collection for method chaining</returns>
        public IServiceCollection AddInternalTokenFeature(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            services.AddOptions<InternalTokenOptions>()
                .Bind(configuration.GetSection(InternalTokenOptions.SectionName))
                .ValidateOnStart()
                .PostConfigure(options => options.Validate());

            services.AddSingleton(sp => sp.GetRequiredService<IOptions<InternalTokenOptions>>().Value);

            services.AddSingleton<KeyService>();
            services.AddHostedService(sp => sp.GetRequiredService<KeyService>());

            services.AddSingleton<TokenService>();

            services.AddSingleton<IInternalTokenProvider, InternalTokenProvider>();
            services.AddSingleton<IJwksProvider, JwksProvider>();

            return services;
        }
    }
}