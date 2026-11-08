using FluentValidation;

using Microsoft.AspNetCore.Mvc;

using UserOrg.Shared.Extensions;

using UserOrganizationManagement.API.API.AbstractTypes;
using UserOrganizationManagement.API.Authentication;
using UserOrganizationManagement.API.Validations.AbstractTypes;
using UserOrganizationManagement.Application.UserOrg.IServices;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.General.Model;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.InputModels.Provider;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Provider;

namespace UserOrganizationManagement.API.API;

internal sealed class ProviderEndpoint : IApiRoute
{
    public void Register(RouteGroupBuilder group)
    {
        group = group.MapGroup("Provider")
            .WithTags("ProviderManagement");

        group.MapGet("", async ([FromQuery(Name = "CountryCode")] string? countryCode, [FromServices] IProviderService service, [FromServices] IValidator<CountryRequestModel> validator, HttpContext httpContext, CancellationToken ct) =>
        {
            var model = new CountryRequestModel(countryCode);
            validator.ValidateAndThrow(model);

            return !httpContext.TryGetProviderId(out var providerId)
                ? Results.Ok(await service.GetPublicProvidersAsync(ct))
                : Results.Ok(new List<ProviderModel>() { await service.GetProviderAsync(providerId.GetValueOrDefault(), false, ct) });

        }).Produces<IEnumerable<ProviderModel>>(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .ProducesValidationProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
          .WithOpenApiDoc(generatedOperation =>
          {
              generatedOperation.Summary = """List "LEXIS Providers" """;
              generatedOperation.Description = """Lists all "LEXIS Providers" """;
              var parameter = generatedOperation.Parameters[0];
              parameter.Description = "Provider Service Key";
              //var parameter2 = generatedOperation.Parameters[1];
              //parameter2.Description = "CountryCode";
              return generatedOperation;
          });

        group.MapGet("{Id:GUID}", async ([FromRoute(Name = "Id")] Guid id, [FromServices] IProviderService service, HttpContext httpContext, CancellationToken ct) =>
        {
            if (!httpContext.TryGetProviderId(out var providerId))
            {
                return Results.Ok(await service.GetProviderAsync(id, true, ct));
            }

            return Results.Ok(await service.GetProviderAsync(providerId.GetValueOrDefault(), false, ct));

        }).WithName("GetProvider")
          .Produces<ProviderModel>(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .ProducesValidationProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
          .WithOpenApiDoc(generatedOperation =>
          {
              generatedOperation.Summary = """Get "LEXIS Provider" """;
              generatedOperation.Description = """Get specific "LEXIS Provider" """;
              var parameter = generatedOperation.Parameters[0];
              parameter.Description = "Provider Service Key";
              //var parameter2 = generatedOperation.Parameters[1];
              //parameter2.Description = "LEXIS Provider Id";
              return generatedOperation;
          });

        group.MapPost("", async ([Validate] ProviderCreate provider, [FromServices] IProviderService service, CancellationToken ct) =>
        {
            var providerId = await service.InsertProviderAsync(provider, ct);
            return Results.CreatedAtRoute("GetProvider", new { Id = providerId });

        }).DisabledWhen("ReadOnlyMode")
          .Produces(StatusCodes.Status201Created)
          .ProducesProblem(StatusCodes.Status400BadRequest)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .ProducesValidationProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
          .WithOpenApiDoc(generatedOperation =>
          {
              generatedOperation.Summary = """Create "LEXIS Provider" """;
              generatedOperation.Description = """Create specific "LEXIS Provider" """;
              return generatedOperation;
          });

        group.MapPut("", async ([Validate] ProviderUpdate provider, [FromServices] IProviderService service, HttpContext httpContext, CancellationToken ct) =>
        {
            // why: the update targets the body-named provider, so the caller's own provider_id has to be compared to it - a presence check alone let a service key for one provider update another (ADR-0105)
            if (!httpContext.TryGetProviderId(out var providerId) || provider?.Id != providerId)
            {
                return Results.Forbid();
            }

            await service.UpdateProviderAsync(provider, ct);
            return Results.Ok("LEXIS provider was modified.");

        }).DisabledWhen("ReadOnlyMode")
          .Produces(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status400BadRequest)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .ProducesValidationProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status409Conflict)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
          .WithOpenApiDoc(generatedOperation =>
          {
              generatedOperation.Summary = """Update "LEXIS Provider" """;
              generatedOperation.Description = """Update specific "LEXIS Provider" """;
              //var parameter = generatedOperation.Parameters[0];
              //parameter.Description = "Provider Service Key";
              return generatedOperation;
          });

        group.MapDelete("{Id:GUID}", async ([FromRoute(Name = "Id")] Guid id, [FromServices] IProviderService service, HttpContext httpContext, CancellationToken ct) =>
        {
            // why: a caller carrying a provider_id may only delete that provider - a caller carrying none is the platform admin the Gateway's perm:provider:remove already gates, and keeps its reach (ADR-0105)
            if (httpContext.TryGetProviderId(out var providerId) && providerId != id)
            {
                return Results.Forbid();
            }

            await service.DeleteProviderAsync(id, ct);
            return Results.NoContent();

        }).DisabledWhen("ReadOnlyMode")
          .Produces(StatusCodes.Status204NoContent)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .ProducesValidationProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status409Conflict)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
          .WithOpenApiDoc(generatedOperation =>
          {
              generatedOperation.Summary = """Remove "LEXIS Provider" """;
              generatedOperation.Description = """Remove specific "LEXIS Provider" """;
              var parameter = generatedOperation.Parameters[0];
              parameter.Description = "LEXIS Provider Id";
              return generatedOperation;
          });
    }
}