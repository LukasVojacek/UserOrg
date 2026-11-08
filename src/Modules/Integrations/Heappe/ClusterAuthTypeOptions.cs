using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Heappe;

/// <summary>
/// Configuration options for filtering allowed cluster authentication credential authentication types.
/// Only authentication type names matching those configured in this options class will be returned
/// by <see cref="IHeappeService.GetClusterAuthenticationCredentialsAuthTypesAsync"/>.
/// </summary>
internal sealed class ClusterAuthTypeOptions
{
    internal static string SectionName = "Heappe:ClusterAuthTypes";

    /// <summary>
    /// Gets or sets the collection of allowed authentication type names.
    /// Authentication types not in this list will be filtered out.
    /// Names are matched case-insensitively.
    /// At least one authentication type name must be configured.
    /// </summary>
    [MinLength(1, ErrorMessage = "At least one authentication type name must be configured.")]
    public ICollection<string> AllowedAuthenticationTypeNames { get; set; } = [];
}