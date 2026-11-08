using System.Text.Json;

using Microsoft.AspNetCore.Mvc;

using UserOrg.Shared;

using UserOrganizationManagement.API.API.AbstractTypes;
using UserOrganizationManagement.Application.UserOrg.IServices;

namespace UserOrganizationManagement.API.API;

internal sealed class UserEndpoint : IApiRoute
{
    public void Register(RouteGroupBuilder group)
    {
        group = group.MapGroup("User")
            .WithTags("User");

        group.MapGet("Preferences", async ([FromServices] IUserService service, HttpContext context, CancellationToken ct) =>
        {
            if (!context.TryGetUserIdentityProviderId(out var idpUserId))
            {
                return Results.Unauthorized();
            }

            var result = await service.GetUserPreferences(idpUserId, ct)
                ;

            return Results.Ok(result);

        }).Produces<string>(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
          .WithOpenApiDoc(generatedOperation =>
          {
              generatedOperation.Summary = """Get "LEXIS User" preferences json object""";
              generatedOperation.Description = """Get "LEXIS User" preferences json object""";
              return generatedOperation;
          });

        group.MapPut("Preferences", async ([FromBody] JsonElement preferencesJson, [FromServices] IUserService service, HttpContext context, CancellationToken ct) =>
        {
            if (!context.TryGetUserIdentityProviderId(out var idpUserId))
            {
                return Results.Unauthorized();
            }

            await service.UpdateUserPreferences(idpUserId, preferencesJson.ToString(), ct)
                ;

            return Results.Ok();

        }).Produces<string>(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
          .WithOpenApiDoc(generatedOperation =>
          {
              generatedOperation.Summary = """Update "LEXIS User" preferences json object""";
              generatedOperation.Description = """Update "LEXIS User" preferences json object""";
              return generatedOperation;
          });
    }
}