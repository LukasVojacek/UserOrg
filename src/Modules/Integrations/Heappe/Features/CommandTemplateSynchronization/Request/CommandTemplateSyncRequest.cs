using System.ComponentModel.DataAnnotations;

using Lukdrasil.Error;
using Lukdrasil.Result;

using UserOrg.Shared.Abstractions.Interfaces;

namespace Heappe.Features.CommandTemplateSynchronization.Request;

internal sealed record CommandTemplateSyncRequest
(
    [Required]
    [StringLength(50, MinimumLength = 1)]
    string projectShortName,

    [Required]
    [StringLength(100, MinimumLength = 1)]
    string resourceName
) : IRequest<Result<VoidResult, Error>>;