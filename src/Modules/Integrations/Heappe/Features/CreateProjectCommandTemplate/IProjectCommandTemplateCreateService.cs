using Lukdrasil.Error;
using Lukdrasil.Result;

namespace Heappe.Features.CreateProjectCommandTemplate;

internal interface IProjectCommandTemplateCreateService
{
    /// <summary>
    /// Creates command templates (on Heappe and/or locally) with automatic decision logic.
    /// Decision is based on null checks: creates Heappe if <see cref="CommandTemplateCreationRequest.ExistingHeappeTemplate"/> is null,
    /// local if <see cref="CommandTemplateCreationRequest.ExistingProjectTemplate"/> is null, or syncs state if both exist.
    /// </summary>
    Task<Result<Error>> CreateCommandTemplateAsync(CommandTemplateCreationRequest request, CancellationToken ct);
}