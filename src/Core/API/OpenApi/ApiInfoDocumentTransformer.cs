using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

using UserOrganizationManagement.Domain.Configuration;

namespace UserOrganizationManagement.API.OpenApi;

internal sealed class ApiInfoDocumentTransformer(IOptions<ScalarUiOptions> options) : IOpenApiDocumentTransformer
{
    private readonly ScalarUiOptions _scalar = options.Value;

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        var version = Environment.GetEnvironmentVariable("APP_VERSION") ?? "0.0.0.1";
        var publicPathBase = string.IsNullOrEmpty(_scalar.PublicPathBase) ? string.Empty : $"/{_scalar.PublicPathBase}";

        document.Info = new OpenApiInfo
        {
            Version = version,
            Title = $"{_scalar.Title} v{version}",
            Description = _scalar.Description,
            Contact = BuildContact(),
            License = BuildLicense()
        };

        if (!string.IsNullOrEmpty(_scalar.ServerUrl))
        {
            document.Servers = [new OpenApiServer { Url = $"{_scalar.ServerUrl}{publicPathBase}" }];
        }

        return Task.CompletedTask;
    }

    // info.contact / info.license are errors under .spectral.yml. Both values come from the repository
    // itself: the README "Contact" section (IT4Innovations / support@lexis.tech / project home) and the
    // Apache License 2.0 in the root LICENSE file, which the README also states in prose and in its badge.
    private OpenApiContact? BuildContact()
    {
        if (string.IsNullOrWhiteSpace(_scalar.ContactName)
            && string.IsNullOrWhiteSpace(_scalar.ContactEmail)
            && string.IsNullOrWhiteSpace(_scalar.ContactUrl))
        {
            return null;
        }

        return new OpenApiContact
        {
            Name = string.IsNullOrWhiteSpace(_scalar.ContactName) ? null : _scalar.ContactName,
            Email = string.IsNullOrWhiteSpace(_scalar.ContactEmail) ? null : _scalar.ContactEmail,
            Url = TryCreateUri(_scalar.ContactUrl)
        };
    }

    private OpenApiLicense? BuildLicense()
    {
        if (string.IsNullOrWhiteSpace(_scalar.LicenseName))
        {
            return null;
        }

        // OAS 3.1 makes license.identifier and license.url mutually exclusive; name + url is valid under
        // both 3.0 and 3.1, so it is the safer pair to emit.
        return new OpenApiLicense
        {
            Name = _scalar.LicenseName,
            Url = TryCreateUri(_scalar.LicenseUrl)
        };
    }

    private static Uri? TryCreateUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;
}