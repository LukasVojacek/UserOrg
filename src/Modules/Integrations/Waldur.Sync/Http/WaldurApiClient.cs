using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using Waldur.Sync.Api;
using Waldur.Sync.Api.WaldurApiModels;
using Waldur.Sync.Configuration;

namespace Waldur.Sync.Http;

/// <summary>
/// Typed <see cref="HttpClient"/> implementation of <see cref="IWaldurApiClient"/> for one instance.
/// Detail pagination (<see cref="GetAllPagesAsync{T}"/>) collects every page into one list and throws
/// <see cref="WaldurApiException"/> on any page failure (never a silent truncation). The customer and
/// project list fetches use the resilient <see cref="GetAllPagesResilientAsync{T}"/>, which returns a
/// partial list flagged incomplete instead of throwing (ADR-0064).
/// </summary>
internal sealed class WaldurApiClient : IWaldurApiClient
{
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly WaldurInstanceOptions _options;

    public WaldurApiClient(HttpClient httpClient, WaldurInstanceOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        _httpClient = httpClient;
        _options = options;
    }

    public Task<PagedFetch<ApiCustomer>> GetCustomersAsync(CancellationToken ct) =>
        GetAllPagesResilientAsync<ApiCustomer>(
            Path(_options.CustomersEndpoint),
            Fields(WaldurApiFields.Customer),
            ct);

    public Task<IReadOnlyList<ApiPublicOffering>> GetCustomerOfferingsAsync(Guid customerUuid, CancellationToken ct) =>
        GetAllPagesAsync<ApiPublicOffering>(
            Path(_options.MarketplacePublicOfferingsEndpoint),
            [.. Fields(WaldurApiFields.PublicOffering), ("customer_uuid", customerUuid.ToString("N"))],
            ct);

    public Task<PagedFetch<ApiProject>> GetProjectsAsync(CancellationToken ct) =>
        GetAllPagesResilientAsync<ApiProject>(
            Path(_options.ProjectsEndpoint),
            Fields(WaldurApiFields.Project),
            ct);

    public Task<IReadOnlyList<ApiMarketplaceResource>> GetProjectResourcesAsync(Guid projectUuid, CancellationToken ct) =>
        GetAllPagesAsync<ApiMarketplaceResource>(
            Path(_options.MarketplaceResourcesEndpoint),
            [.. Fields(WaldurApiFields.MarketplaceResource), ("project_uuid", projectUuid.ToString("N"))],
            ct);

    public Task<IReadOnlyList<ApiProjectUser>> GetProjectUsersAsync(Guid projectUuid, CancellationToken ct) =>
        GetAllPagesAsync<ApiProjectUser>(
            $"{Path(_options.ProjectsEndpoint).TrimEnd('/')}/{projectUuid:N}/list_users/",
            [],
            ct);

    public async Task<ApiPublicOfferingDetail> GetOfferingDetailAsync(Guid offeringUuid, CancellationToken ct)
    {
        var path = $"{Path(_options.MarketplacePublicOfferingsEndpoint).TrimEnd('/')}/{offeringUuid:N}/";
        var uri = BuildUri(path, Fields(WaldurApiFields.PublicOfferingDetail));

        using var response = await Send(uri, page: null, ct).ConfigureAwait(false);
        var result = await response.Content
            .ReadFromJsonAsync<ApiPublicOfferingDetail>(_json, ct)
            .ConfigureAwait(false);

        return result ?? throw new WaldurApiException("Waldur returned an empty offering-detail body.")
        {
            RequestUri = uri
        };
    }

    /// <summary>
    /// The throwing detail paginator (offerings/resources/users): a page failure must NOT be silently
    /// truncated, so it delegates to <see cref="GetAllPagesResilientAsync{T}"/> and turns an incomplete
    /// result into a <see cref="WaldurApiException"/>. A successful (complete) fetch returns its items.
    /// </summary>
    private async Task<IReadOnlyList<T>> GetAllPagesAsync<T>(
        string path,
        IReadOnlyList<(string Key, string Value)> query,
        CancellationToken ct)
    {
        var fetch = await GetAllPagesResilientAsync<T>(path, query, ct).ConfigureAwait(false);
        if (!fetch.IsComplete)
        {
            throw new WaldurApiException($"Waldur detail fetch of '{path}' failed on a page.")
            {
                RequestUri = new Uri(path, UriKind.Relative)
            };
        }

        return fetch.Items;
    }

    /// <summary>
    /// The <b>resilient</b> paginator used only by the customer and project list fetches (ADR-0064):
    /// a failed page does not throw — it returns the pages collected so far flagged
    /// <see cref="PagedFetch{T}.IsComplete"/> <c>= false</c>, so the caller can suppress the absence
    /// sweep for that entity type instead of aborting the iteration. Requested cancellation still
    /// throws. Detail fetches keep using the throwing <see cref="GetAllPagesAsync{T}"/>.
    /// </summary>
    private async Task<PagedFetch<T>> GetAllPagesResilientAsync<T>(
        string path,
        IReadOnlyList<(string Key, string Value)> query,
        CancellationToken ct)
    {
        var results = new List<T>();
        var page = 1;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var pagedQuery = new List<(string, string)>(query)
            {
                ("page_size", _options.PageSizeLimit.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("page", page.ToString(System.Globalization.CultureInfo.InvariantCulture))
            };
            var uri = BuildUri(path, pagedQuery);

            List<T> pageItems;
            try
            {
                using var response = await Send(uri, page, ct).ConfigureAwait(false);
                pageItems = await response.Content
                    .ReadFromJsonAsync<List<T>>(_json, ct)
                    .ConfigureAwait(false) ?? [];
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // ADR-0064: a failed page returns a partial, incomplete list rather than throwing.
                return new PagedFetch<T>(results, IsComplete: false);
            }

            results.AddRange(pageItems);

            var isLastPage = pageItems.Count < _options.PageSizeLimit;
            if (isLastPage)
            {
                return new PagedFetch<T>(results, IsComplete: true);
            }

            page++;
        }
    }

    private async Task<HttpResponseMessage> Send(Uri uri, int? page, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync(uri, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            throw new WaldurApiException($"Waldur request to '{uri}' failed.", ex)
            {
                Page = page,
                RequestUri = uri
            };
        }

        if (!response.IsSuccessStatusCode)
        {
            var status = response.StatusCode;
            response.Dispose();
            throw new WaldurApiException($"Waldur request to '{uri}' returned {(int)status} ({status}).")
            {
                StatusCode = status,
                Page = page,
                RequestUri = uri
            };
        }

        return response;
    }

    private static Uri BuildUri(string path, IReadOnlyList<(string Key, string Value)> query)
    {
        if (query.Count == 0)
        {
            return new Uri(path, UriKind.Relative);
        }

        var sb = new StringBuilder(path);
        sb.Append('?');
        for (var i = 0; i < query.Count; i++)
        {
            if (i > 0)
            {
                sb.Append('&');
            }

            sb.Append(Uri.EscapeDataString(query[i].Key))
              .Append('=')
              .Append(Uri.EscapeDataString(query[i].Value));
        }

        return new Uri(sb.ToString(), UriKind.Relative);
    }

    private static string Path(Uri? endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return endpoint.IsAbsoluteUri ? endpoint.PathAndQuery : endpoint.OriginalString;
    }

    private static (string, string)[] Fields(IReadOnlyList<string> fields) =>
        [.. fields.Select(f => ("field", f))];
}