using Heappe.Contracts.Entries.Sync;

using Lukdrasil.Result;

namespace Heappe.Features.CommandTemplateSynchronization;

public interface IProjectCommandTemplateSyncService
{
    Task<ValidationResult<CommandTemplateSyncResult>> SyncCommandTemplatesForResource(string projectShortName, string projectResourceName, CancellationToken ct);
}