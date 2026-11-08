using Heappe.Features.CommandTemplateManagement.GetAll.Response;

using Lukdrasil.Error;
using Lukdrasil.Result;

using UserOrg.Shared.Abstractions.Interfaces;
using UserOrg.Shared.Errors;

namespace Heappe.Features.CommandTemplateManagement.GetAll;

/// <summary>
/// Handles queries to retrieve command template permissions for a user within a specified HEAppE context.
/// </summary>
/// <param name="service">The service used to access and manage command template data for HEAppE projects. Cannot be null.</param>
internal sealed class GetCommandTemplateQueryHandler(CommandTemplateManagementService service) : IRequestHandler<GetCommandTemplateQuery, Result<IList<CommandTemplateResponse>, Error>>
{
    /// <summary>
    /// Handles a query to retrieve the command template permissions for a user within a specified HEAppE context.
    /// </summary>
    /// <param name="request">The query containing the user's identity provider ID and HEAppE identifier for which to retrieve permissions.
    /// Cannot be null.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a model describing the user's
    /// command template permissions for the specified HEAppE context.</returns>
    public async Task<Result<IList<CommandTemplateResponse>, Error>> Handle(GetCommandTemplateQuery request, CancellationToken ct = default)
    {
        if (request is null)
        {
            return Result<IList<CommandTemplateResponse>, Error>.Failure(CommonErrors.InvalidArgument(nameof(request)));
        }

        return await service.GetCommandTemplatesForProjectAsync(request, ct);
    }
}