using System.ComponentModel.DataAnnotations;

using Lukdrasil.Error;
using Lukdrasil.Result;

using UserOrg.Shared.Abstractions.Interfaces;

namespace Heappe.Features.CommandTemplateManagement.Request;

/// <summary>
/// Represents a request to activate a command template for a specified project and resource.
/// </summary>
/// <param name="Id">The unique identifier of the command template to activate. Must not be empty.</param>
/// <param name="projectShortName">The short name of the project associated with the command template. Must be between 1 and 50 characters in length.</param>
/// <param name="resourceName">The name of the resource for which the command template is being activated. Must be between 1 and 100 characters in
/// length.</param>
internal sealed record CommandTemplateActivateRequest
(
    [Required]
    Guid Id,

    [Required]
    [StringLength(50, MinimumLength = 1)]
    string projectShortName,

    [Required]
    [StringLength(100, MinimumLength = 1)]
    string resourceName
) : IRequest<Result<VoidResult, Error>>;