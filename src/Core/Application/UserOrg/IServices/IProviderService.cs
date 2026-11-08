using UserOrganizationManagement.Domain.ExtModels.UserOrg.InputModels.Provider;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Provider;

namespace UserOrganizationManagement.Application.UserOrg.IServices;

public interface IProviderService
{
    Task<IList<ProviderModel>> GetRelevantProvidersAsync(Guid userId, CancellationToken ct = default);
    Task<IList<ProviderModel>> GetPublicProvidersAsync(CancellationToken ct = default);
    Task<IList<ProviderModel>> GetProvidersAsync(string? countryCode, bool onlyPublic, CancellationToken ct = default);
    Task<ProviderModel> GetProviderAsync(Guid id, bool onlyPublic, CancellationToken ct = default);
    Task<Guid> InsertProviderAsync(ProviderCreate obj, CancellationToken ct);
    Task UpdateProviderAsync(ProviderUpdate obj, CancellationToken ct);
    Task DeleteProviderAsync(Guid id, CancellationToken ct);
}