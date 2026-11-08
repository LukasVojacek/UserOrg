namespace Waldur.Sync.Configuration;

/// <summary>A single provider's HEAppE instance coordinates (legacy <c>IdentifierWithUri</c>).</summary>
public sealed class WaldurHeappeInstanceOptions
{
    public Uri? Uri { get; set; }
    public string? Identifier { get; set; }
}