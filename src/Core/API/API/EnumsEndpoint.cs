using UserOrg.Shared.Persistance.Abstractions.Enums;

using UserOrganizationManagement.API.API.AbstractTypes;
using UserOrganizationManagement.Domain.Contracts;
using UserOrganizationManagement.Domain.Enums;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Enums;

namespace UserOrganizationManagement.API.API;

internal sealed class EnumsEndpoint : IApiRoute
{
    public void Register(RouteGroupBuilder group)
    {
        group = group.MapGroup("Enums")
            .WithTags("ListEnums");

        group.MapGet("LocationTypes", async () =>
        {
            var values = Enum.GetValues<ELocationType>()
                .Select(e => new EnumValueModel((int)e, e.ToString()));

            return Results.Ok(values);
        }).Produces<IEnumerable<EnumValueModel>>(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .ProducesValidationProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
        .WithOpenApiDoc(generatedOperation =>
        {
            generatedOperation.Summary = """Get "LEXIS Location types enums" """;
            generatedOperation.Description = """Get list Location types enum values """;
            return generatedOperation;
        });

        group.MapGet("RequestStates", async () =>
        {
            var values = Enum.GetValues<ERequestState>()
                .Select(e => new EnumValueModel((int)e, e.ToString()));

            return Results.Ok(values);
        }).Produces<IEnumerable<EnumValueModel>>(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .ProducesValidationProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
        .WithOpenApiDoc(generatedOperation =>
        {
            generatedOperation.Summary = """Get "LEXIS request states enums" """;
            generatedOperation.Description = """Get list of request states enum values """;
            return generatedOperation;
        });

        group.MapGet("RequestTypes", async () =>
        {
            var values = Enum.GetValues<ERequestType>()
                .Select(e => new EnumValueModel((int)e, e.ToString()));

            return Results.Ok(values);
        }).Produces<IEnumerable<EnumValueModel>>(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .ProducesValidationProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
        .WithOpenApiDoc(generatedOperation =>
        {
            generatedOperation.Summary = """Get "LEXIS request types enums" """;
            generatedOperation.Description = """Get list of request types enum values """;
            return generatedOperation;
        });

        group.MapGet("OperationModeResponses", async () =>
        {
            var values = Enum.GetValues<EOperationModeResponse>()
                .Select(e => new EnumValueModel((int)e, e.ToString()));

            return Results.Ok(values);
        }).Produces<IEnumerable<EnumValueModel>>(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .ProducesValidationProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
        .WithOpenApiDoc(generatedOperation =>
        {
            generatedOperation.Summary = """Get "LEXIS operation mode responses enums" """;
            generatedOperation.Description = """Get list of operation mode responses enum values """;
            return generatedOperation;
        });
    }
}