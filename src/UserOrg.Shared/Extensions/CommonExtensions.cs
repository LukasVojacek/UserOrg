using System.Collections.ObjectModel;
using System.Globalization;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Extensions;

using UserOrg.Shared.FeatureManagement;

namespace UserOrg.Shared.Extensions;

/// <summary>
/// Provides extension methods for common data types.
/// </summary>
public static class CommonExtensions
{
    /// <summary>
    /// Remove specific character from beginning or end of string
    /// </summary>
    /// <param name="text">Text</param>
    /// <param name="character">Character</param>
    /// <returns></returns>
    public static string RemoveCharacterFromBeginAndEnd(this string text, char character)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }
        else
        {
            var tempValue = text;
            if (tempValue.ElementAt(0) == character)
            {
                tempValue = tempValue[1..];
            }

            var lastCharPosition = tempValue.Length - 1;
            if (tempValue.ElementAt(lastCharPosition) == character)
            {
                tempValue = tempValue[..lastCharPosition];
            }

            return tempValue;
        }
    }

    /// <summary>
    /// Removes control and format characters (C0/C1, BOM, zero-width and bidi marks) from the text. Those are
    /// never part of a legitimate name, yet they break text consumers downstream - HTTP header values, logs,
    /// exports. Printable non-ASCII characters (diacritics, dashes) are preserved.
    /// </summary>
    /// <param name="text">Text</param>
    /// <returns>The text without control and format characters, or the same instance when it holds none.</returns>
    public static string RemoveControlCharacters(this string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var printableCount = 0;
        foreach (var character in text)
        {
            if (IsPrintable(character))
            {
                printableCount++;
            }
        }

        return printableCount == text.Length
            ? text
            : string.Create(printableCount, text, static (destination, source) =>
            {
                var written = 0;
                foreach (var character in source)
                {
                    if (IsPrintable(character))
                    {
                        destination[written++] = character;
                    }
                }
            });
    }

    private static bool IsPrintable(char character)
        => !char.IsControl(character) && char.GetUnicodeCategory(character) != UnicodeCategory.Format;

    /// <summary>
    /// Determines whether the specified string is <see langword="null"/>, empty, or represents an empty GUID.
    /// </summary>
    /// <param name="value">The string to evaluate.</param>
    /// <returns><see langword="true"/> if the string is <see langword="null"/>, empty, or equals the string representation of an
    /// empty GUID; otherwise, <see langword="false"/>.</returns>
    public static bool IsNullOrEmptyOrEmptyGuid(this string? value) => string.IsNullOrEmpty(value) || value == Guid.Empty.ToString();

    /// <summary>
    /// Determines whether the specified <see cref="Guid"/> value is null or represents an empty GUID.
    /// </summary>
    /// <param name="value">The nullable <see cref="Guid"/> to evaluate.</param>
    /// <returns><see langword="true"/> if <paramref name="value"/> is <see langword="null"/> or equals <see cref="Guid.Empty"/>;
    /// otherwise, <see langword="false"/>.</returns>
    public static bool IsNullOrEmpty(this Guid? value) => !value.HasValue || value.Value == Guid.Empty;

    /// <summary>
    /// Converts the specified string to a normalized absolute HEAppE URL, ensuring it ends with a trailing slash.
    /// </summary>
    /// <param name="url">The string representation of the HEAppE URL to normalize. Must be a valid absolute URI.</param>
    /// <returns>A <see cref="Uri"/> instance representing the normalized absolute HEAppE URL with a trailing slash.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="url"/> is not a valid absolute URI.</exception>
    public static Uri ToNormalizeUrl(this string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? ToNormalizeUrl(uri)
            : throw new ArgumentException("The provided string is not a valid absolute URI.", nameof(url));
    }

    /// <summary>
    /// Returns a normalized URI with any trailing slash removed from the absolute URI.
    /// </summary>
    /// <remarks>This method does not modify the original Uri instance. If the input URI does not end with a
    /// trailing slash, the original instance is returned.</remarks>
    /// <param name="url">The URI to normalize. Cannot be null.</param>
    /// <returns>
    /// A new Uri instance with the trailing slash removed from the absolute URI if present; otherwise, the original Uri.
    /// </returns>
    public static Uri ToNormalizeUrl(this Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return url.AbsoluteUri.EndsWith('/')
            ? url
            : new Uri($"{url.AbsoluteUri}/");
    }

    /// <summary>
    /// Constructs a complete request URI by combining a base address, a relative endpoint, and optional query parameters.
    /// </summary>
    /// <remarks>The base address is normalized before constructing the final URI. Ensure that the base address is a
    /// valid URI. Query parameters with empty or null values are ignored.</remarks>
    /// <param name="baseAddress">The base URI to which the endpoint will be appended. This parameter cannot be null.</param>
    /// <param name="endpoint">The relative URI that will be appended to the base address. This parameter must not be an absolute URI and cannot be
    /// null.</param>
    /// <param name="queryParameters">An optional collection of key-value pairs representing query parameters to include in the final URI. If null or
    /// empty, no query parameters are added.</param>
    /// <returns>A Uri instance representing the constructed request URI, including the base address, endpoint, and any specified
    /// query parameters.</returns>
    /// <exception cref="ArgumentException">Thrown if the endpoint parameter is an absolute URI.</exception>
    public static Uri BuildRequestUri(this Uri baseAddress, Uri endpoint, IReadOnlyList<KeyValuePair<string, IReadOnlyList<string>>>? queryParameters = default)
    {
        ArgumentNullException.ThrowIfNull(baseAddress, nameof(baseAddress));
        ArgumentNullException.ThrowIfNull(endpoint, nameof(endpoint));

        if (endpoint.IsAbsoluteUri)
        {
            throw new ArgumentException("Endpoint must be a relative URI.", nameof(endpoint));
        }

        Uri baseUri = new Uri(baseAddress.ToNormalizeUrl(), endpoint);
        if (queryParameters == null || queryParameters.Count == 0)
        {
            return baseUri;
        }

        var queryBuilder = new QueryBuilder();
        foreach (var kvp in queryParameters)
        {
            var key = kvp.Key;
            var values = kvp.Value;
            if (values is null || values.Count == 0)
            {
                continue;
            }

            foreach (var value in values)
            {
                queryBuilder.Add(key, value);
            }
        }

        var uriBuilder = new UriBuilder(baseUri);
        uriBuilder.Query = queryBuilder.ToQueryString().Value;
        return uriBuilder.Uri;
    }



    public static bool TryExtractVersion(this string? versionString, out Version? version)
    {
        version = null;
        if (!string.IsNullOrWhiteSpace(versionString))
        {
            if (versionString.ToUpperInvariant().StartsWith('V'))
            {
                versionString = versionString[1..];
            }

            if (Version.TryParse(versionString, out var parsed))
            {
                version = parsed;
                return true;
            }
        }
        return false;
    }


    /// <summary>
    /// Request size limit for endpoint method
    /// </summary>
    /// <typeparam name="TBuilder"></typeparam>
    /// <param name="builder">Builder</param>
    /// <param name="size">Size in Bytes</param>
    /// <returns></returns>
    public static TBuilder RequestSizeLimit<TBuilder>(this TBuilder builder, long size) where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Add(endpointBuilder =>
        {
            endpointBuilder.Metadata.Add(new SizeAttribute(size));
        });
        return builder;
    }

    /// <summary>
    /// Marks an endpoint as disabled when a specific feature flag is enabled.
    /// </summary>
    /// <remarks>
    /// When the specified feature flag is enabled, the endpoint will return HTTP 404 Not Found.
    /// This is enforced by the <see cref="FeatureFlagMiddleware"/>.
    /// 
    /// Example:
    /// <code>
    /// app.MapPost("/api/Requests", CreateRequestHandler)
    ///    .DisabledWhen("ReadOnlyMode")
    ///    .WithName("CreateRequest");
    /// </code>
    /// </remarks>
    /// <typeparam name="TBuilder">The endpoint convention builder type</typeparam>
    /// <param name="builder">The endpoint convention builder</param>
    /// <param name="featureFlagName">The name of the feature flag that disables this endpoint</param>
    /// <returns>The endpoint convention builder for method chaining</returns>
    public static TBuilder DisabledWhen<TBuilder>(this TBuilder builder, string featureFlagName)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(featureFlagName);

        return builder.WithMetadata(new DisabledByFeatureFlagMetadata(featureFlagName));
    }

    /// <summary>
    /// Marks an endpoint as enabled only when a specific feature flag is enabled.
    /// </summary>
    /// <remarks>
    /// When the specified feature flag is disabled (OFF), the endpoint will return HTTP 404 Not Found.
    /// This is enforced by the <see cref="FeatureFlagMiddleware"/>.
    ///
    /// Example:
    /// <code>
    /// app.MapPost("/api/Requests", CreateRequestHandler)
    ///    .EnabledWhen("NewRequestFlow")
    ///    .WithName("CreateRequest");
    /// </code>
    /// </remarks>
    /// <typeparam name="TBuilder">The endpoint convention builder type</typeparam>
    /// <param name="builder">The endpoint convention builder</param>
    /// <param name="featureFlagName">The name of the feature flag that enables this endpoint</param>
    /// <returns>The endpoint convention builder for method chaining</returns>
    public static TBuilder EnabledWhen<TBuilder>(this TBuilder builder, string featureFlagName)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(featureFlagName);

        return builder.WithMetadata(new EnabledByFeatureFlagMetadata(featureFlagName));
    }

}