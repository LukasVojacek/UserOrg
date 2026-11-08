using Heappe.Contracts;
using Heappe.Contracts.CommandTemplate;

using Lukdrasil.Error;
using Lukdrasil.Result;

using UserOrg.Shared;

using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.Application.UserOrg.IServices;

public interface IHeappeConnectionService
{
    /// <summary>
    /// Reads the project's active command templates (the Heappe integration DB). A project-level precondition
    /// for a TemplatesInfo build: Core fetches it once up front so a DB failure fast-fails before the HEAppE
    /// fan-out instead of masquerading as HEAppE degradation (ADR 0074).
    /// </summary>
    Task<Result<HashSet<CommandTemplatePartitionResponse>, Error>> GetActiveCommandTemplatesAsync(ProjectId projectId, CancellationToken ct);

    ValueTask<Result<IList<Domain.ExtModels.UserOrg.OutputModels.Project.ClusterInfoModel>, Error>> GetListAvailableClusters(ProjectShortName projectShortName, ProjectId projectId, string resourceName, HeappeIdentifier identifier, string bearerToken, IReadOnlyCollection<string> locationNames, IReadOnlyCollection<CommandTemplatePartitionResponse> activeCommandTemplates, CancellationToken ct, bool noCache = false);

    ValueTask<Result<Dictionary<string, DownloadedFileModel>, Error>> DownloadChangedFilesAsync(HeappeIdentifier identifier, string bearerToken, long submittedJobInfoId, CancellationToken ct = default);

}