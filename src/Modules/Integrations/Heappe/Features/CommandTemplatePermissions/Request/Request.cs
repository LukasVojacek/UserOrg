using System.ComponentModel.DataAnnotations;

namespace Heappe.Features.CommandTemplatePermissions.Request;

internal record Request(
    [property: Required]
    [property: StringLength(100, MinimumLength = 1)]
        string Name,
    [property: StringLength(1000)]
       string? Description,
       string[]? Permissions
);