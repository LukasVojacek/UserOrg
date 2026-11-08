namespace UserOrganizationManagement.API;

internal class DefaultUserAgentHandler : DelegatingHandler
{
    private static string? Version => Environment.GetEnvironmentVariable("APP_VERSION") ?? default;

    private readonly string _userAgent = $"UserOrg{(!string.IsNullOrEmpty(Version) ? $"/{Version}" : string.Empty)}";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.UserAgent.ParseAdd(_userAgent);
        return base.SendAsync(request, cancellationToken);
    }
}