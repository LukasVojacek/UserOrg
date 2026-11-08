using Forge.Core.Features.Role.Services;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

using UserOrg.Shared;

namespace Core.UnitTests;

/// <summary>
/// Shared setup for the database integration smoke tests. The connection string comes from the
/// <c>USERORG_TEST_DB</c> environment variable; when it is unset the tests skip instead of failing,
/// so they never break a clean <c>dotnet test</c> run. The test classes are tagged
/// <c>[Trait("Category", "Integration")]</c>.
/// </summary>
internal static class IntegrationDb
{
    internal const string EnvVar = "USERORG_TEST_DB";

    /// <summary>The role names every integration fixture serves, so a fixture never carries its own copy.</summary>
    // why: the ids the E2E seed happens to produce, not a contract — seed-data.sql resolves roles by name
    // see: Tests/E2E/E2E.Fixtures/seed-data.sql the lookup that proves role ids are not stable
    internal static readonly IReadOnlyDictionary<int, string> WellKnownRoleNames = new Dictionary<int, string>
    {
        [1] = "User",
        [2] = "Admin",
        [3] = "Owner",
        [4] = "Manager",
        [5] = "DataPublisher",
        [6] = "Member",
        [7] = "ProviderAdmin",
        [8] = "JobscriptOperator",
        [9] = "JobscriptWriter",
        [10] = "JobscriptAccessMember",
    }.AsReadOnly();

    /// <summary>The configured connection string, or <c>null</c> when the environment variable is unset.</summary>
    internal static string? ConnectionString => Environment.GetEnvironmentVariable(EnvVar);

    /// <summary>A harmless placeholder so the DbContext can be registered even when no database is configured.</summary>
    internal static string ConnectionStringOrPlaceholder =>
        ConnectionString ?? "Server=unavailable;Database=none;Trusted_Connection=True;";

    /// <summary>Skips the calling test when no integration database is configured.</summary>
    internal static void SkipIfUnavailable() =>
        Assert.SkipWhen(
            string.IsNullOrWhiteSpace(ConnectionString),
            $"Set the {EnvVar} environment variable to a SQL Server connection string to run this integration test.");

    /// <summary>
    /// A service collection carrying the two dependencies <c>AddCoreDataAccess</c> leaves to the host.
    /// <para>
    /// The <see cref="IHostEnvironment"/> is resolved inside the <c>AddDbContext</c> factory to decide
    /// <c>EnableDetailedErrors</c> / <c>EnableSensitiveDataLogging</c> / SQL command logging; without it
    /// every gated test throws before any SQL runs. It reports <see cref="Environments.Development"/>
    /// because neither flag changes SQL semantics, and this hand-run local diagnostic is worth more with
    /// readable translation errors.
    /// </para>
    /// <para>
    /// The <see cref="IRoleService"/> is a constructor argument of <c>DatabaseContext</c> itself. A fixture
    /// that registers its own after this call wins, because the last registration of a service type is the
    /// one resolved.
    /// </para>
    /// </summary>
    internal static ServiceCollection NewServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new DevelopmentHostEnvironment());
        services.AddScoped<IRoleService, WellKnownRoleService>();
        return services;
    }

    // IntegrationDb is linked into test projects that do not reference Moq, so the stub is hand-written.
    private sealed class DevelopmentHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = nameof(IntegrationDb);
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }

    /// <summary>
    /// Serves the role names the <c>User</c> aggregate needs to resolve its system role; every write
    /// operation throws, because an integration fixture that mutates roles is a test defect, not a scenario.
    /// </summary>
    private sealed class WellKnownRoleService : IRoleService
    {
        private static NotSupportedException NotSupported() =>
            new("The integration fixture role service serves role names only.");

        public Task<Result<IReadOnlyDictionary<int, string>, Error>> ListRoleNames(CancellationToken cancellationToken) =>
            Task.FromResult(Result<IReadOnlyDictionary<int, string>, Error>.Success(WellKnownRoleNames));

        public Task<Result<IList<RoleInfo>, Error>> ListRoles(CancellationToken cancellationToken) => throw NotSupported();

        public Task<Result<RoleInfo, Error>> CreateRole(string roleName, string description, string category, int level, HashSet<PermissionKey> permissions, CancellationToken cancellationToken) => throw NotSupported();

        public Task<Result<RoleInfo, Error>> GetRole(string roleName, CancellationToken cancellationToken) => throw NotSupported();

        public Task<Result<Error>> UpdateRolePermissions(string roleName, HashSet<PermissionKey> permissions, CancellationToken cancellationToken) => throw NotSupported();

        public Task<Result<bool, Error>> UpdateRoleState(string roleName, bool isActive, CancellationToken cancellationToken) => throw NotSupported();

        public Task<Result<Error>> UpdateRoleLevel(string roleName, int level, CancellationToken cancellationToken) => throw NotSupported();

        public Task<Result<Error>> UpdateRoleCategory(string roleName, string category, CancellationToken cancellationToken) => throw NotSupported();

        public Task<Result<Error>> UpdateRoleDescription(string roleName, string? description, CancellationToken cancellationToken) => throw NotSupported();

        public Task<Result<Error>> DeleteRole(string roleName, CancellationToken cancellationToken) => throw NotSupported();

        public Task InvalidateRoleListsAsync(CancellationToken cancellationToken) => throw NotSupported();
    }
}