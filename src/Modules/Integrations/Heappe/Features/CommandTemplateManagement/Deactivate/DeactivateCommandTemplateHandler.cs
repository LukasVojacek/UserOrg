using Heappe.Features.CommandTemplateManagement.Request;

using Lukdrasil.Error;
using Lukdrasil.Result;

using UserOrg.Shared.Abstractions.Interfaces;
using UserOrg.Shared.Errors;

namespace Heappe.Features.CommandTemplateManagement.Deactivate;

/// <summary>
/// Handles requests to deactivate a command template by updating its state.
/// </summary>
/// <param name="service">The service used to manage command template state. Cannot be null.</param>
internal sealed class DeactivateCommandTemplateHandler(CommandTemplateManagementService service) : IRequestHandler<CommandTemplateActivateRequest, Result<VoidResult, Error>>
{

    /// <summary>
    /// Activates a command template by updating its state based on the specified request.
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

        return await service.UpdateCommandTemplateStateAsync(request, false, ct);
    }
}