using System.Diagnostics.CodeAnalysis;

namespace UserOrganizationManagement.Domain.Configuration;

public sealed record AuthenticationConfiguration()
{
    public static string SectionName => "AuthenticationConfiguration";
    public static string CustomHttpAuthHeaderName => "X-Api-Token";
    public static string DefaultHttpAuthHeaderName => "Authorization";
    public static string OriginalHttpAuthHeaderName => "X-Original-Authorization";

    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "<Pending>")]
    public string? MetadataAddress { get; set; }
}