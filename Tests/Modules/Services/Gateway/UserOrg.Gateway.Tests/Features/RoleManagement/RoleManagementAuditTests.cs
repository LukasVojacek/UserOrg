using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

using Forge.Core.Features.EndpointMap.Services;
using Forge.Core.Features.ImportExport;
using Forge.Core.Features.Permission.Services;
using Forge.Core.Features.Role.Services;

using Lukdrasil.Error;
using Lukdrasil.Result;
using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using UserOrg.Gateway.Features.RoleManagement;
using UserOrg.Gateway.Features.RoleManagement.Services;

using ForgePermissionInfo = Forge.Core.Features.Permission.Services.PermissionInfo;
using PermissionKey = UserOrg.Shared.PermissionKey;

namespace UserOrg.Gateway.Tests.Features.RoleManagement;

/// <summary>
/// Pins the RBAC administration audit (T-208-06): every mutating member of <see cref="IRoleManagementService"/>
/// and the export write one fail-closed record naming the console operator, an unauthenticated scope is refused
/// before the mutation runs, only the seed's scope acts as the service actor, and the seed writes
/// <c>gateway.rbac.seeded</c> fail-open. The service is resolved through the real
/// <c>AddRoleManagementFeature</c> registration over mocked Forge services, so the tests compile before the
/// decorator exists and fail on an assertion.
/// </summary>
public sealed class RoleManagementAuditTests
{
    private const string OperatorSub = "operator-sub-7f3a";

    private const string AccessId = "access-id-91c2";

    private const string RoleName = "role-audit-alpha";

    private const string PermissionName = "perm:audit:alpha";

    private const string EndpointName = "endpoint-audit-alpha";

    private const string FreeTextDescription = "free-text-description-5521";

    private const string SeedFilePath = "/test/seed.json";

    private const string SeedJson = """{"formatVersion":"1.0","features":{"permissions":[]}}""";

    private const int SeedCreated = 7;

    private const int SeedUpdated = 5;

    private static readonly string[] SeedErrors = ["seed-error-a", "seed-error-b"];

    private sealed class Harness
    {
        public Mock<IRoleService> RoleService { get; } = new();

        public Mock<IPermissionService> PermissionService { get; } = new();

        public Mock<IEndpointPermissionService> EndpointPermissionService { get; } = new();

        public Mock<IForgeImportExport> PermissionsFeature { get; } = Feature("permissions");

        public RecordingAuditEventSink Sink { get; } = new(new HttpContextAccessor());

        public Func<AuditEvent, Exception?> ThrowFor { get; set; } = _ => null;

        public ServiceProvider Build(ClaimsPrincipal? principal)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddRoleManagementFeature(new ConfigurationBuilder().Build());
            services.AddSingleton(RoleService.Object);
            services.AddSingleton(PermissionService.Object);
            services.AddSingleton(EndpointPermissionService.Object);
            services.AddSingleton(PermissionsFeature.Object);
            services.AddSingleton(Feature("roles").Object);
            services.AddSingleton(Feature("endpointMappings").Object);
            services.AddSingleton<IAuditEventSink>(new SelectiveSink(Sink, this));
            services.AddSingleton(typeof(IAuditLogger<>), typeof(SinkAuditLogger<>));
            if (principal is not null)
            {
                services.AddScoped<AuthenticationStateProvider>(_ => new FixedAuthenticationStateProvider(principal));
            }

            return services.BuildServiceProvider();
        }

        public IReadOnlyList<AuditEvent> Records => [.. Sink.Writes.Select(write => write.Event)];

        private static Mock<IForgeImportExport> Feature(string name)
        {
            var feature = new Mock<IForgeImportExport>();
            feature.SetupGet(f => f.FeatureName).Returns(name);
            feature.Setup(f => f.ExportAsync(It.IsAny<Utf8JsonWriter>(), It.IsAny<ExportOptions>(), It.IsAny<CancellationToken>()))
                .Returns<Utf8JsonWriter, ExportOptions, CancellationToken>((writer, _, _) =>
                {
                    writer.WriteStartArray();
                    writer.WriteEndArray();
                    return Task.CompletedTask;
                });
            feature.Setup(f => f.ImportAsync(It.IsAny<JsonElement>(), It.IsAny<ImportOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ImportResult(true, SeedCreated, SeedUpdated, Errors: SeedErrors));
            return feature;
        }
    }

    private sealed class SelectiveSink(RecordingAuditEventSink inner, Harness harness) : IAuditEventSink
    {
        public ValueTask<AuditWriteResult> WriteAsync(AuditEvent auditEvent)
            => harness.ThrowFor(auditEvent) is { } exception ? throw exception : inner.WriteAsync(auditEvent);
    }

    private sealed class SinkAuditLogger<T>(IAuditEventSink sink) : IAuditLogger<T>
    {
        public async ValueTask AuditAsync(AuditEvent auditEvent) => await sink.WriteAsync(auditEvent);

        public async ValueTask AuditAsync(AuditEvent auditEvent, Action<ILogger> log) => await sink.WriteAsync(auditEvent);
    }

    private sealed class FixedAuthenticationStateProvider(ClaimsPrincipal principal) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(principal));
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
    }

    private static ClaimsPrincipal Operator() => new(new ClaimsIdentity(
        [
            new Claim(JwtRegisteredClaimNames.Sub, OperatorSub),
            new Claim(JwtRegisteredClaimNames.PreferredUsername, "operator-name"),
            new Claim(Constants.RoleManagementAccessIdClaim, AccessId),
        ],
        Constants.RoleManagementCookieSchemeId,
        JwtRegisteredClaimNames.PreferredUsername,
        ClaimTypes.Role));

    private static ClaimsPrincipal Unauthenticated() => new(new ClaimsIdentity(
        [
            new Claim(JwtRegisteredClaimNames.Sub, OperatorSub),
            new Claim(Constants.RoleManagementAccessIdClaim, AccessId),
        ]));

    private static PermissionKey Key(string value) => PermissionKey.TryParse(value, out var key) ? key : throw new ArgumentException(value);

    private static void SetUpSuccessfulForge(Harness harness)
    {
        var role = new RoleInfo(1, RoleName, "role description", "Project", Array.Empty<ForgePermissionInfo>(), true, 100);
        harness.RoleService.Setup(s => s.CreateRole(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<HashSet<PermissionKey>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RoleInfo, Error>.Success(role));
        harness.RoleService.Setup(s => s.GetRole(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RoleInfo, Error>.Success(role));
        harness.RoleService.Setup(s => s.ListRoles(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<RoleInfo>, Error>.Success([role]));
        harness.RoleService.Setup(s => s.UpdateRoleState(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Success(true));
        harness.RoleService.Setup(s => s.UpdateRolePermissions(It.IsAny<string>(), It.IsAny<HashSet<PermissionKey>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Error>.Success());
        harness.RoleService.Setup(s => s.UpdateRoleDescription(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Error>.Success());
        harness.RoleService.Setup(s => s.UpdateRoleLevel(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Error>.Success());
        harness.RoleService.Setup(s => s.UpdateRoleCategory(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Error>.Success());

        var permission = new ForgePermissionInfo(Key(PermissionName), "permission description");
        harness.PermissionService.Setup(s => s.CreatePermission(It.IsAny<PermissionKey>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ForgePermissionInfo, Error>.Success(permission));
        harness.PermissionService.Setup(s => s.DeletePermission(It.IsAny<PermissionKey>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Error>.Success());
        harness.PermissionService.Setup(s => s.GetPermission(It.IsAny<PermissionKey>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ForgePermissionInfo, Error>.Success(permission));
        harness.PermissionService.Setup(s => s.ListPermissions(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<ForgePermissionInfo>, Error>.Success([permission]));
        harness.PermissionService.Setup(s => s.IsPermissionAssignedToRoles(It.IsAny<PermissionKey>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Success(true));

        var mapping = new EndpointPermissionMap(EndpointName, [PermissionName]) { PathTemplate = "/api/audit", HttpMethod = "GET" };
        harness.EndpointPermissionService.Setup(s => s.SetEndpointPermissionAsync(It.IsAny<EndpointPermissionMap>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Error>.Success());
        harness.EndpointPermissionService.Setup(s => s.CreateEndpointMappingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EndpointPermissionMap, Error>.Success(mapping));
        harness.EndpointPermissionService.Setup(s => s.DeleteEndpointMappingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Error>.Success());
        harness.EndpointPermissionService.Setup(s => s.GetEndpointMappingsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([mapping]);
        harness.EndpointPermissionService.Setup(s => s.InvalidateSnapshotAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private static async Task<bool> InvokeAsync(IRoleManagementService service, string member)
    {
        var ct = CancellationToken.None;
        return member switch
        {
            nameof(IRoleManagementService.CreateRoleAsync) => (await service.CreateRoleAsync(new CreateRoleRequest { Name = RoleName, Description = FreeTextDescription, Category = "Project", Level = 100, Permissions = [PermissionName] }, ct)).IsSuccess,
            nameof(IRoleManagementService.UpdateRoleStatusAsync) => (await service.UpdateRoleStatusAsync(RoleName, false, ct)).IsSuccess,
            nameof(IRoleManagementService.UpdateRolePermissionsAsync) => (await service.UpdateRolePermissionsAsync(RoleName, [PermissionName], ct)).IsSuccess,
            nameof(IRoleManagementService.UpdateRoleDescriptionAsync) => (await service.UpdateRoleDescriptionAsync(RoleName, FreeTextDescription, ct)).IsSuccess,
            nameof(IRoleManagementService.UpdateRoleLevelAsync) => (await service.UpdateRoleLevelAsync(RoleName, 200, ct)).IsSuccess,
            nameof(IRoleManagementService.UpdateRoleCategoryAsync) => (await service.UpdateRoleCategoryAsync(RoleName, "Platform", ct)).IsSuccess,
            nameof(IRoleManagementService.CreatePermissionAsync) => (await service.CreatePermissionAsync(PermissionName, FreeTextDescription, ct)).IsSuccess,
            nameof(IRoleManagementService.DeletePermissionAsync) => (await service.DeletePermissionAsync(PermissionName, ct)).IsSuccess,
            nameof(IRoleManagementService.SetEndpointPermissionsAsync) => (await service.SetEndpointPermissionsAsync(EndpointName, [PermissionName], ct)).IsSuccess,
            nameof(IRoleManagementService.CreateEndpointMappingAsync) => (await service.CreateEndpointMappingAsync(EndpointName, "/api/audit", "GET", FreeTextDescription, ct)).IsSuccess,
            nameof(IRoleManagementService.DeleteEndpointMappingAsync) => (await service.DeleteEndpointMappingAsync(EndpointName, ct)).IsSuccess,
            nameof(IRoleManagementService.ImportRolesAsync) => (await service.ImportRolesAsync(new MemoryStream(Encoding.UTF8.GetBytes(SeedJson)), overwriteExisting: true, ct)).IsSuccess,
            nameof(IRoleManagementService.ExportRolesAsync) => (await service.ExportRolesAsync(new MemoryStream(), ct)).IsSuccess,
            _ => throw new ArgumentOutOfRangeException(nameof(member), member, null),
        };
    }

    private static RbacSeedHostedService Seed(ServiceProvider provider, ILogger<RbacSeedHostedService> logger, string seedJson = SeedJson)
        => new(provider.GetRequiredService<IServiceScopeFactory>(), new RoleManagementOptions { SeedFilePath = SeedFilePath }, logger)
        {
            FileExists = _ => true,
            OpenRead = _ => new MemoryStream(Encoding.UTF8.GetBytes(seedJson)),
        };

    private static bool HasValue(AuditEvent record, int expected)
        => record.Data?.Values.Any(value => value?.ToString() == expected.ToString(System.Globalization.CultureInfo.InvariantCulture)) == true;

    private static bool IsD10Warning(LogEntry entry, AuditOutcome outcome)
        => entry.Level == LogLevel.Warning
            && entry.Message.Contains("gateway.rbac.seeded", StringComparison.Ordinal)
            && entry.Message.Contains($"({outcome})", StringComparison.Ordinal)
            && entry.Message.Contains(nameof(InvalidOperationException), StringComparison.Ordinal);

    private static AuditEvent SingleRecord(Harness harness, string action)
    {
        var matching = harness.Records.Where(record => record.Action == action).ToList();
        Assert.True(matching.Count == 1, $"expected one {action} record, found {matching.Count} among [{string.Join(", ", harness.Records.Select(r => r.Action))}]");
        return matching[0];
    }

    [Theory]
    [InlineData(nameof(IRoleManagementService.CreateRoleAsync), "gateway.rbac_role.created", RoleName)]
    [InlineData(nameof(IRoleManagementService.UpdateRoleStatusAsync), "gateway.rbac_role.updated", RoleName)]
    [InlineData(nameof(IRoleManagementService.UpdateRolePermissionsAsync), "gateway.rbac_role.updated", RoleName)]
    [InlineData(nameof(IRoleManagementService.UpdateRoleDescriptionAsync), "gateway.rbac_role.updated", RoleName)]
    [InlineData(nameof(IRoleManagementService.UpdateRoleLevelAsync), "gateway.rbac_role.updated", RoleName)]
    [InlineData(nameof(IRoleManagementService.UpdateRoleCategoryAsync), "gateway.rbac_role.updated", RoleName)]
    [InlineData(nameof(IRoleManagementService.CreatePermissionAsync), "gateway.rbac_permission.created", PermissionName)]
    [InlineData(nameof(IRoleManagementService.DeletePermissionAsync), "gateway.rbac_permission.deleted", PermissionName)]
    [InlineData(nameof(IRoleManagementService.SetEndpointPermissionsAsync), "gateway.endpoint_mapping.updated", EndpointName)]
    [InlineData(nameof(IRoleManagementService.CreateEndpointMappingAsync), "gateway.endpoint_mapping.created", EndpointName)]
    [InlineData(nameof(IRoleManagementService.DeleteEndpointMappingAsync), "gateway.endpoint_mapping.deleted", EndpointName)]
    [InlineData(nameof(IRoleManagementService.ImportRolesAsync), "gateway.rbac.imported", null)]
    [InlineData(nameof(IRoleManagementService.ExportRolesAsync), "gateway.rbac.exported", null)]
    public async Task Should_WriteOneSuccessRecordNamingTheOperatorAndTarget_When_AnOperatorCallsAnAuditedMember(string member, string action, string? targetId)
    {
        var harness = new Harness();
        SetUpSuccessfulForge(harness);
        await using var provider = harness.Build(Operator());
        await using var scope = provider.CreateAsyncScope();

        var succeeded = await InvokeAsync(scope.ServiceProvider.GetRequiredService<IRoleManagementService>(), member);

        Assert.True(succeeded);
        var record = Assert.Single(harness.Records);
        Assert.Equal(action, record.Action);
        Assert.Equal(AuditOutcome.Success, record.Outcome);
        Assert.Equal(OperatorSub, record.ActorId);
        Assert.Equal("user", record.ActorType);
        Assert.NotNull(record.Data);
        Assert.Equal(AccessId, record.Data[Constants.RoleManagementAccessIdClaim]);
        if (targetId is not null)
        {
            Assert.Equal(targetId, record.TargetId);
        }
    }

    [Theory]
    [InlineData(nameof(IRoleManagementService.CreateRoleAsync))]
    [InlineData(nameof(IRoleManagementService.UpdateRoleDescriptionAsync))]
    [InlineData(nameof(IRoleManagementService.CreatePermissionAsync))]
    [InlineData(nameof(IRoleManagementService.CreateEndpointMappingAsync))]
    public async Task Should_KeepTheFreeTextDescriptionOutOfTheRecord_When_AMemberTakesADescription(string member)
    {
        var harness = new Harness();
        SetUpSuccessfulForge(harness);
        await using var provider = harness.Build(Operator());
        await using var scope = provider.CreateAsyncScope();

        await InvokeAsync(scope.ServiceProvider.GetRequiredService<IRoleManagementService>(), member);

        var record = Assert.Single(harness.Records);
        Assert.DoesNotContain(FreeTextDescription, JsonSerializer.Serialize(record), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_WriteNothing_When_AReadOrTheCacheInvalidationIsCalled()
    {
        var harness = new Harness();
        SetUpSuccessfulForge(harness);
        await using var provider = harness.Build(Operator());
        await using var scope = provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IRoleManagementService>();

        Assert.True((await service.GetRolesAsync(CancellationToken.None)).IsSuccess);
        Assert.True((await service.GetRoleAsync(RoleName, CancellationToken.None)).IsSuccess);
        Assert.True((await service.GetPermissionsAsync(CancellationToken.None)).IsSuccess);
        Assert.True((await service.GetPermissionAsync(PermissionName, CancellationToken.None)).IsSuccess);
        Assert.True((await service.IsPermissionAssignedAsync(PermissionName, CancellationToken.None)).IsSuccess);
        Assert.True((await service.GetEndpointMappingsAsync(CancellationToken.None)).IsSuccess);
        await service.InvalidateEndpointPermissionCacheAsync(CancellationToken.None);

        Assert.Empty(harness.Records);
        harness.EndpointPermissionService.Verify(s => s.InvalidateSnapshotAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_ReturnTheInnerFailureAndWriteNothing_When_TheMutationFails()
    {
        var harness = new Harness();
        SetUpSuccessfulForge(harness);
        var failure = new Error("role.duplicate", ErrorSeverity.Error, "duplicate");
        harness.RoleService.Setup(s => s.CreateRole(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<HashSet<PermissionKey>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RoleInfo, Error>.Failure(failure));
        await using var provider = harness.Build(Operator());
        await using var scope = provider.CreateAsyncScope();

        var result = await scope.ServiceProvider.GetRequiredService<IRoleManagementService>()
            .CreateRoleAsync(new CreateRoleRequest { Name = RoleName, Category = "Project", Level = 100, Permissions = [] }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("role.duplicate", result.Error.Code);
        Assert.Empty(harness.Records);
    }

    [Fact]
    public async Task Should_RefuseTheMutationBeforeItRuns_When_TheCircuitPrincipalIsUnauthenticated()
    {
        var harness = new Harness();
        SetUpSuccessfulForge(harness);
        await using var provider = harness.Build(Unauthenticated());
        await using var scope = provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IRoleManagementService>();

        var exception = await Record.ExceptionAsync(() => service.DeleteEndpointMappingAsync(EndpointName, CancellationToken.None));

        Assert.NotNull(exception);
        harness.EndpointPermissionService.Verify(s => s.DeleteEndpointMappingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(harness.Records);
    }

    [Fact]
    public async Task Should_RefuseTheMutationRatherThanActAsTheGateway_When_TheScopeHasNoAuthenticationState()
    {
        var harness = new Harness();
        SetUpSuccessfulForge(harness);
        await using var provider = harness.Build(principal: null);
        await using var scope = provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IRoleManagementService>();

        var exception = await Record.ExceptionAsync(() => service.DeletePermissionAsync(PermissionName, CancellationToken.None));

        Assert.NotNull(exception);
        harness.PermissionService.Verify(s => s.DeletePermission(It.IsAny<PermissionKey>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.DoesNotContain(harness.Records, record => record.ActorId == "gateway");
    }

    [Fact]
    public async Task Should_PropagateTheSinkFailureAfterTheMutation_When_TheAuditWriteThrows()
    {
        var harness = new Harness();
        SetUpSuccessfulForge(harness);
        var sinkFailure = new InvalidOperationException("sink down");
        harness.ThrowFor = _ => sinkFailure;
        await using var provider = harness.Build(Operator());
        await using var scope = provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IRoleManagementService>();

        var exception = await Record.ExceptionAsync(() => service.UpdateRoleLevelAsync(RoleName, 200, CancellationToken.None));

        Assert.Same(sinkFailure, exception);
        harness.RoleService.Verify(s => s.UpdateRoleLevel(RoleName, 200, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_RecordTheSeedImportAsTheGatewayServiceActor_When_TheSeedReconciles()
    {
        var harness = new Harness();
        await using var provider = harness.Build(principal: null);

        await Seed(provider, new RecordingLogger<RbacSeedHostedService>()).StartAsync(CancellationToken.None);

        var imported = SingleRecord(harness, "gateway.rbac.imported");
        Assert.Equal(AuditOutcome.Success, imported.Outcome);
        Assert.Equal("gateway", imported.ActorId);
        Assert.Equal("service", imported.ActorType);
    }

    [Fact]
    public async Task Should_WriteSeededSuccessWithTheCounts_When_TheSeedImportSucceeds()
    {
        var harness = new Harness();
        SetUpSuccessfulForge(harness);
        await using var provider = harness.Build(principal: null);

        await Seed(provider, new RecordingLogger<RbacSeedHostedService>()).StartAsync(CancellationToken.None);

        var seeded = SingleRecord(harness, "gateway.rbac.seeded");
        Assert.Equal(AuditOutcome.Success, seeded.Outcome);
        Assert.Equal("gateway", seeded.ActorId);
        Assert.Equal("service", seeded.ActorType);
        Assert.True(HasValue(seeded, SeedCreated), "the seeded record carries no created count");
        Assert.True(HasValue(seeded, SeedUpdated), "the seeded record carries no updated count");
        Assert.True(HasValue(seeded, SeedErrors.Length), "the seeded record carries no error count");
        harness.EndpointPermissionService.Verify(s => s.InvalidateSnapshotAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_WriteSeededFailure_When_TheSeedImportReturnsAFailure()
    {
        var harness = new Harness();
        await using var provider = harness.Build(principal: null);

        await Seed(provider, new RecordingLogger<RbacSeedHostedService>(), seedJson: """{"features":{}}""").StartAsync(CancellationToken.None);

        var seeded = SingleRecord(harness, "gateway.rbac.seeded");
        Assert.Equal(AuditOutcome.Failure, seeded.Outcome);
        Assert.Equal("gateway", seeded.ActorId);
        Assert.Equal("service", seeded.ActorType);
        Assert.DoesNotContain(harness.Records, record => record.Action == "gateway.rbac.imported");
    }

    [Fact]
    public async Task Should_WriteSeededFailure_When_TheSeedFailsUnexpectedly()
    {
        var harness = new Harness();
        SetUpSuccessfulForge(harness);
        harness.ThrowFor = record => record.Action == "gateway.rbac.imported" ? new InvalidOperationException("imported write refused") : null;
        await using var provider = harness.Build(principal: null);

        var exception = await Record.ExceptionAsync(() => Seed(provider, new RecordingLogger<RbacSeedHostedService>()).StartAsync(CancellationToken.None));

        Assert.Null(exception);
        var seeded = SingleRecord(harness, "gateway.rbac.seeded");
        Assert.Equal(AuditOutcome.Failure, seeded.Outcome);
        harness.PermissionsFeature.Verify(f => f.ImportAsync(It.IsAny<JsonElement>(), It.IsAny<ImportOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_FinishStartupAndLogTheD10Warning_When_TheSeededWriteThrows()
    {
        var harness = new Harness();
        SetUpSuccessfulForge(harness);
        harness.ThrowFor = record => record.Action == "gateway.rbac.seeded" ? new InvalidOperationException("seeded write refused") : null;
        await using var provider = harness.Build(principal: null);
        var logger = new RecordingLogger<RbacSeedHostedService>();

        var exception = await Record.ExceptionAsync(() => Seed(provider, logger).StartAsync(CancellationToken.None));

        Assert.Null(exception);
        harness.EndpointPermissionService.Verify(s => s.InvalidateSnapshotAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains(logger.Entries, entry => IsD10Warning(entry, AuditOutcome.Success));
    }

    [Fact]
    public async Task Should_FinishStartupAndLogTheD10Warning_When_EverySeedAuditWriteThrows()
    {
        var harness = new Harness();
        SetUpSuccessfulForge(harness);
        harness.ThrowFor = _ => new InvalidOperationException("sink down");
        await using var provider = harness.Build(principal: null);
        var logger = new RecordingLogger<RbacSeedHostedService>();

        var exception = await Record.ExceptionAsync(() => Seed(provider, logger).StartAsync(CancellationToken.None));

        Assert.Null(exception);
        Assert.Contains(logger.Entries, entry => IsD10Warning(entry, AuditOutcome.Failure));
    }
}
