using Core.Shared.Http;
using Core.Shared.Paging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Core.Features.Country.GetCountries;

internal static class GetCountriesEndpoint
{
    internal sealed record Response(string Code, string? Name);

    internal static async Task<IResult> HandleAsync(
        HttpContext http,
        [FromQuery] string? countryCode,
        [FromServices] ICountryQuery countries,
        CancellationToken ct)
    {
        var result = await countries.GetCountriesAsync(countryCode, ct);
        var response = result.Select(c => new Response(c.Code, c.Name)).ToList();
        return PagedResponse.Complete(http, response);
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapGetCountriesEndpoint()
        {
            app.MapGet("Countries", HandleAsync)
                .CacheOutput(CountryCache.PolicyName)
                .WithETag()
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("GetCountriesV2")
            .WithSummary("List countries")
            .WithDescription("Returns reference country data, optionally filtered by country code (substring match).")
            .WithTags("Country")
            .Produces<IReadOnlyList<Response>>(StatusCodes.Status200OK);
    }
}