using Heappe.Features.CommandTemplateManagement.Request;

using Lukdrasil.Error;
using Lukdrasil.Result;

using UserOrg.Shared.Abstractions.Interfaces;
using UserOrg.Shared.Errors;

namespace Heappe.Features.CommandTemplateManagement.Activate;

/// <summary>
/// Handles requests to activate a command template by updating its state to active.
/// </summary>
/// <param name="service">The service used to manage command templates. Cannot be null.</param>
internal sealed class ActivateCommandTemplateHandler(CommandTemplateManagementService service) : IRequestHandler<CommandTemplateActivateRequest, Result<VoidResult, Error>>
{

    /// <summary>
    /// Activates the specified command template.
    /// </summary>
    /// <param name="request">The request containing the details of the command template to activate. Cannot be null.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A result indicating success or failure of the activation operation. On success, contains a void result; on
    /// failure, contains an error describing the reason.</returns>
    public async Task<Result<VoidResult, Error>> Handle(CommandTemplateActivateRequest request, CancellationToken ct = default)
    {
        if (request is null)
        {
            return Result<VoidResult, Error>.Failure(CommonErrors.InvalidArgument(nameof(request)));
        }

        return await service.UpdateCommandTemplateStateAsync(request, true, ct);
    }
}