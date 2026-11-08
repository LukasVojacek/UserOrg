using System.Diagnostics.CodeAnalysis;

using UserOrg.Shared;
using UserOrg.Shared.Persistance;

namespace Forge.Core.Features.Permission.Persistence;

/// <summary>
/// Aggregate root representing a permission definition in the system.
/// Encapsulates invariants: a non-empty, validated <see cref="PermissionKey"/> and a creation timestamp.
/// </summary>
internal sealed class Permission
{
    // Parameterless ctor for EF Core
    [ExcludeFromCodeCoverage]
    private Permission()
    {
    }

    /// <summary>
    /// Creates a new permission with the specified key and optional description.
    /// </summary>
    /// <param name="key">Validated permission key value object.</param>
    /// <param name="description">Optional description (max 1000 chars).</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is default.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="description"/> exceeds allowed length.</exception>
    internal Permission(PermissionKey key, string? description = null)
    {
        if (key.Value is null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        ValidateDescription(description);

        Key = key;
        Description = description;
    }

    /// <summary>
    /// Database identity key.
    /// </summary>
    internal long Id { get; private set; }

    /// <summary>
    /// Permission key value object that enforces format rules.
    /// </summary>
    internal PermissionKey Key { get; private set; }

    /// <summary>
    /// Human-friendly description of the permission. Optional, up to 1000 characters.
    /// </summary>
    internal string? Description { get; private set; }

    // CreatedAt is a shadow property configured in EF Core; it is not part of the CLR model.

    /// <summary>
    /// Updates the description for this permission.
    /// </summary>
    /// <param name="description">New description or null to clear.</param>
    /// <exception cref="ArgumentException">If description exceeds allowed length.</exception>
    internal void UpdateDescription(string? description)
    {
        ValidateDescription(description);
        Description = description;
    }

    private static void ValidateDescription(string? description)
    {
        if (description is not null && description.Length > DbConstants.ExtendedDescriptionMaxLength)
        {
            throw new ArgumentException($"Description cannot exceed {DbConstants.ExtendedDescriptionMaxLength} characters.", nameof(description));
        }
    }
}