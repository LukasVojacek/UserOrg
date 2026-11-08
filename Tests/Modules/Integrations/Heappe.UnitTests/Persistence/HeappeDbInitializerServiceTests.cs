using Heappe.Persistence;
using Heappe.Persistence.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

namespace Heappe.UnitTests.Persistence;

/// <summary>
/// Tests for the static SeedAsync method in HeappeIntegrationPersistenceExtensions.
/// </summary>
public sealed class SeedAsyncTests
{
    [Fact(DisplayName = "Inserts 5 default command templates when database is empty")]
    public async Task Should_InsertFiveTemplates_When_DatabaseIsEmpty()
    {
        using var ctx = CreateInMemoryDbContext();

        await HeappeIntegrationPersistenceExtensions.SeedAsync(ctx, CancellationToken.None);

        Assert.Equal(5, await ctx.Set<CommandTemplateDefinition>().CountAsync().ConfigureAwait(false));
    }

    [Theory(DisplayName = "Inserts all expected default template names")]
    [InlineData("JobScript")]
    [InlineData("TestTemplate")]
    [InlineData("Container")]
    [InlineData("GenericCommandTemplate")]
    [InlineData("BulkCopy")]
    public async Task Should_ContainExpectedTemplateName_When_DatabaseIsEmpty(string expectedName)
    {
        using var ctx = CreateInMemoryDbContext();

        await HeappeIntegrationPersistenceExtensions.SeedAsync(ctx, CancellationToken.None);

        Assert.True(await ctx.Set<CommandTemplateDefinition>().AnyAsync(t => t.Name == expectedName).ConfigureAwait(false));
    }

    [Fact(DisplayName = "Does not insert duplicate templates when called twice (idempotent)")]
    public async Task Should_SkipSeeding_When_TemplatesAlreadyExist()
    {
        using var ctx = CreateInMemoryDbContext();
        await HeappeIntegrationPersistenceExtensions.SeedAsync(ctx, CancellationToken.None);

        await HeappeIntegrationPersistenceExtensions.SeedAsync(ctx, CancellationToken.None);

        Assert.Equal(5, await ctx.Set<CommandTemplateDefinition>().CountAsync().ConfigureAwait(false));
    }

    private static HeappeIntegrationDbContext CreateInMemoryDbContext() =>
        new(new DbContextOptionsBuilder<HeappeIntegrationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}

/// <summary>
/// Tests for HeappeDbInitializerService lifecycle and its integration with the factory and seeding.
/// </summary>
public sealed class HeappeDbInitializerServiceTests
{
    private readonly Mock<IDbContextFactory<HeappeIntegrationDbContext>> _factory = new(MockBehavior.Strict);
    private readonly Mock<ILogger<HeappeDbInitializerService>> _logger = new(MockBehavior.Loose);

    [Fact(DisplayName = "Seeds database via factory context on StartAsync")]
    public async Task Should_SeedDatabase_When_StartAsyncCalled()
    {
        string dbName = Guid.NewGuid().ToString();
        _factory
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken ct) => Task.FromResult(CreateInMemoryDbContext(dbName)));

        var sut = new HeappeDbInitializerService(_factory.Object, _logger.Object);
        await sut.StartAsync(CancellationToken.None);

        using var verify = CreateInMemoryDbContext(dbName);
        Assert.Equal(5, await verify.Set<CommandTemplateDefinition>().CountAsync().ConfigureAwait(false));
    }

    [Fact(DisplayName = "Calls CreateDbContextAsync exactly once on StartAsync")]
    public async Task Should_CallCreateDbContextAsyncOnce_When_StartAsyncCalled()
    {
        _factory
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken ct) => Task.FromResult(CreateInMemoryDbContext(Guid.NewGuid().ToString())));

        var sut = new HeappeDbInitializerService(_factory.Object, _logger.Object);
        await sut.StartAsync(CancellationToken.None);

        _factory.Verify(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "Does not access factory on StopAsync")]
    public async Task Should_NotCallFactory_When_StopAsyncCalled()
    {
        var sut = new HeappeDbInitializerService(_factory.Object, _logger.Object);

        await sut.StopAsync(CancellationToken.None);

        _factory.VerifyNoOtherCalls();
    }

    [Fact(DisplayName = "Does not insert duplicate templates when StartAsync called twice")]
    public async Task Should_NotDuplicateTemplates_When_StartAsyncCalledTwice()
    {
        string dbName = Guid.NewGuid().ToString();
        _factory
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken ct) => Task.FromResult(CreateInMemoryDbContext(dbName)));

        var sut = new HeappeDbInitializerService(_factory.Object, _logger.Object);
        await sut.StartAsync(CancellationToken.None);
        await sut.StartAsync(CancellationToken.None);

        using var verify = CreateInMemoryDbContext(dbName);
        Assert.Equal(5, await verify.Set<CommandTemplateDefinition>().CountAsync().ConfigureAwait(false));
    }

    private static HeappeIntegrationDbContext CreateInMemoryDbContext(string dbName) =>
        new(new DbContextOptionsBuilder<HeappeIntegrationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);
}
