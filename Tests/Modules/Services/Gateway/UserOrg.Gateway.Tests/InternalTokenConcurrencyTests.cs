using Lukdrasil.StepUpLogging;

using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using UserOrg.Gateway.Features.InternalToken;
using UserOrg.Shared;

namespace UserOrg.Gateway.Tests
{
    public class InternalTokenConcurrencyTests
    {
        /// <summary>
        /// Verifies that 50 concurrent token generations all succeed and produce valid, distinct tokens.
        /// </summary>
        [Fact]
        public async Task Should_AllSucceed_WhenGeneratingInternalTokensConcurrentlyWithSameSubject()
        {
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole());
            services.AddHttpContextAccessor();
            services.AddSingleton<RecordingAuditEventSink>();
            services.AddSingleton(typeof(IAuditLogger<>), typeof(RecordingAuditLogger<>));
            services.AddDistributedMemoryCache();
            services.AddHybridCache(options =>
            {
                options.DefaultEntryOptions = new HybridCacheEntryOptions
                {
                    LocalCacheExpiration = TimeSpan.FromMinutes(5)
                };
            });

            var internalOptions = new InternalTokenOptions
            {
                Issuer = "https://test.local",
                Audience = "gateway-internal",
                ExpirationMinutes = 5,
                KeyRotationIntervalHours = 24,
                KeyGracePeriodHours = 48,
                JwksCacheExpirationSeconds = 30
            };

            services.AddSingleton(internalOptions);
            services.AddSingleton<KeyService>();
            services.AddSingleton<TokenService>();

            using var sp = services.BuildServiceProvider();
            var keyService = sp.GetRequiredService<KeyService>();
            await keyService.StartAsync(CancellationToken.None);

            var tokenService = sp.GetRequiredService<TokenService>();

            const int concurrency = 50;
            var tasks = Enumerable.Range(0, concurrency)
                .Select(_ => tokenService.GenerateTokenAsync(
                    EAuthorizationMethod.JwtBearerToken,
                    "sub-user-1",
                    "user-1",
                    null,
                    "User",
                    email: null,
                    projectRoles: null,
                    originalJti: null))
                .ToArray();

            var tokens = await Task.WhenAll(tasks);

            Assert.Equal(concurrency, tokens.Length);
            Assert.All(tokens, t => Assert.False(string.IsNullOrEmpty(t), "Token should not be null or empty"));

            // All tokens should be distinct (each call generates a new JWT with new jti claim)
            var uniqueTokens = tokens.Distinct().Count();
            Assert.Equal(concurrency, uniqueTokens);

            var validateTasks = tokens.Select(t => tokenService.ValidateTokenAsync(t)).ToArray();
            var principals = await Task.WhenAll(validateTasks);

            Assert.All(principals, p => Assert.NotNull(p));

            await keyService.StopAsync(CancellationToken.None);
        }
    }
}
