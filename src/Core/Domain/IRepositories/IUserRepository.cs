using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Others;
using UserOrganizationManagement.Domain.IRepositories.AbstractTypes;

namespace UserOrganizationManagement.Domain.IRepositories;

public interface IUserRepository : IRepository<User, Guid>
{
    Task<User> GetBySidWithProjectsAsync(string idpUserId, CancellationToken ct);
    Task<User> GetBySidWithProviderAsync(string idpUserId, CancellationToken ct);
    Task<User?> GetByUsernameAsync(string username, CancellationToken ct);
    Task<Guid?> GetUserIdByIdpUserIdAsync(string idpUserId, CancellationToken ct);
    Task<Guid?> GetProviderIdByIdpUserIdAsync(string idpUserId, CancellationToken ct);
    Task<IEnumerable<User>> GetAllAsync(ValueObjects.ProjectShortName? projectShortName, string? idpUserId, CancellationToken ct);
    Task<IEnumerable<User>> GetAllWithoutProjectAsync(CancellationToken ct);
    Task<IReadOnlyList<string>> GetAllIdpUserIdsIncludingDeletedAsync(CancellationToken ct);

    Task<IList<WeekCount>> CreatedFrom(DateOnly fromDate, DateOnly toDate, CancellationToken ct);
    Task<int> TotalCount(CancellationToken ct);
}