using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace UserOrg.Shared;

/// <summary>
/// Represents a permission key used throughout the access control system.
/// </summary>
/// <remarks>
/// Format enforced by the regex: <c>^perm:(?:\{[a-zA-Z0-9_]+\}|[a-zA-Z0-9_\-]+)(?::[a-zA-Z0-9_\-]+)*$</c>
/// Rules:
/// - Must start with the literal prefix <c>"perm:"</c>.
/// - The first segment (resource) may be either:
///   - a braced identifier: <c>{identifier}</c> where <c>identifier</c> contains only letters, digits or underscore (no hyphen), or
///   - an unbraced token containing letters, digits, underscore or hyphen.
/// - Any additional segments are optional and must be tokens of letters, digits, underscore or hyphen, separated by <c>":"</c>.
///
/// Examples:
/// <list type="bullet">
/// <item>
/// <description>Valid: <c>perm:read</c></description>
/// </item>
/// <item>
/// <description>Valid: <c>perm:users-create</c> (hyphen allowed in unbraced tokens)</description>
/// </item>
/// <item>
/// <description>Valid: <c>perm:{users}:read</c> (braced first segment, subsequent segments unbraced)</description>
/// </item>
/// <item>
/// <description>Valid: <c>perm:{User_123}:create:sub-action</c></description>
/// </item>
/// <item>
/// <description>Invalid: <c>perm:</c> (empty resource)</description>
/// </item>
/// <item>
/// <description>Invalid: <c>perm:{user-name}:read</c> (hyphen not allowed inside braces)</description>
/// </item>
/// <item>
/// <description>Invalid: <c>permission:read</c> (wrong prefix)</description>
/// </item>
/// <item>
/// <description>Invalid: <c>perm:users:</c> (trailing empty segment)</description>
/// </item>
/// </list>
/// </remarks>
/// <summary>
/// Always serialize/deserialize as a plain string (the <c>Value</c>) via <see cref="PermissionKeyJsonConverter"/>.
/// Without this attribute, System.Text.Json would emit <c>{"Value":"perm:…"}</c> on write but fail to
/// reconstruct the struct on read (constructor expects <c>KeyValue</c>, JSON only has <c>Value</c>).
/// </summary>
[JsonConverter(typeof(PermissionKeyJsonConverter))]
public partial record struct PermissionKey(string KeyValue)
{
    internal string KeyValue { get; init; } = IsValidKeyRegex(KeyValue) ? KeyValue : throw new ArgumentException($"Permission key '{KeyValue}' does not match required format.", nameof(KeyValue));

    public string Value => KeyValue;

    public bool HasPlaceholder => KeyValue.Contains('{', StringComparison.Ordinal);
    public static bool TryParse(string? key, out PermissionKey permissionKey)
    {
        permissionKey = default;
        if (!IsValidKeyRegex(key))
        {
            return false;
        }

        permissionKey = new(key!);
        return true;
    }

    public static PermissionKey Parse(string s)
    {
        if (!TryParse(s, out var key))
            throw new FormatException($"Permission key '{s}' is not in a valid format.");

        return key;
    }

    private static bool IsValidKeyRegex(string? key) => !string.IsNullOrEmpty(key) && ValidKeyRegex().IsMatch(key);

    [GeneratedRegex(@"^perm:(?:\{[a-zA-Z0-9_]+\}|[a-zA-Z0-9_\-]+)(?::[a-zA-Z0-9_\-]+)*$")]
    private static partial Regex ValidKeyRegex();

}