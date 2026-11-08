using Microsoft.EntityFrameworkCore;

using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.ValueObjects;
using UserOrganizationManagement.Infrastructure.DataAccess.Contexts;

namespace Infrastructure.DataAccess.Tests;

/// <summary>
/// Pins ADR-0061 decision 2/3: the legacy <see cref="DatabaseContext"/> must client-generate a
/// UUID v7 <c>Id</c> for single-Guid-PK aggregates instead of relying on the (now-removed)
/// <c>DEFAULT NewId()</c> store default, which no longer exists on the modular-canonical schema.
/// </summary>
public sealed class DatabaseContextGuidKeyGenerationTests : IDisposable
{
    private readonly DatabaseContext _context;

    public DatabaseContextGuidKeyGenerationTests()
    {
        var options = new DbContextOptionsBuilder<DatabaseContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new DatabaseContext(options);
    }

    [Fact]
    public async Task Should_StampNonEmptyUuidV7Id_When_UserIsAdded()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = new User
        {
            IdentityProviderUserId = Guid.NewGuid().ToString(),
            Email = "stamped-user@example.com",
            UserName = "stamped-user",
        };

        _context.Add(user);
        await _context.SaveChangesAsync(ct);

        Assert.NotEqual(Guid.Empty, user.Id);
        AssertIsUuidV7(user.Id);
    }

    [Fact]
    public async Task Should_StampNonEmptyUuidV7Id_When_ProjectAggregateWithConvertedIdIsAdded()
    {
        var ct = TestContext.Current.CancellationToken;
        var domainType = new ProjectDomainType { Name = "stamped-domain" };
        var creator = new User
        {
            IdentityProviderUserId = Guid.NewGuid().ToString(),
            Email = "creator@example.com",
            UserName = "creator",
        };
        var project = new Project
        {
            Name = "stamped-project",
            ShortName = ProjectShortName.Create("stamped-proj"),
            StartDate = DateTime.UtcNow,
            EndDate = DateTime.UtcNow.AddDays(1),
            ProjectDomainType = domainType,
            CreatedByUser = creator,
        };

        _context.Add(project);
        await _context.SaveChangesAsync(ct);

        Assert.NotEqual(Guid.Empty, project.Id.Value);
        AssertIsUuidV7(project.Id.Value);
    }

    public void Dispose() => _context.Dispose();

    /// <summary>
    /// Guards against the (incidental) EF Core default Guid-key convention, which already stamps a
    /// non-empty random v4 <see cref="Guid"/> for raw-<see cref="Guid"/> keys once no store default is
    /// configured — a non-empty check alone would pass without <c>ApplyCodeGeneratedGuidKeys</c> wired.
    /// The version nibble pins the stamp to the shared <c>UuidV7ValueGenerator</c> specifically.
    /// </summary>
    private static void AssertIsUuidV7(Guid id)
    {
        var version = id.ToString("N")[12];
        Assert.Equal('7', version);
    }
}
