using Core.Features.ResourceRequest;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Contracts.Ports;

namespace Core.Features.Resource.GetResourceRequestStatus;

/// <summary>
/// Poll endpoint for an async resource-creation request (ADR-0057). The create-and-allocate submit returns
/// <c>202 Accepted</c> with a <c>Location</c> pointing here; callers poll this route until the flow reaches a
/// terminal <see cref="RequestStatus"/>. Projects the workflow engine's <see cref="StatusResponse"/> to a
/// deliberately minimal view — flow status plus a per-step name/state summary (YAGNI; richer projections are
/// added when a consumer needs them).
/// </summary>
internal static class GetResourceRequestStatusEndpoint
{
    internal sealed record Request([FromRoute] Guid RequestId);

    internal sealed record StepSummary(string StateName, StepExecutionStatus ExecutionStatus);

    internal sealed record Response(Guid RequestId, RequestStatus Status, IReadOnlyList<StepSummary> Steps);

    internal static async Task<IResult> HandleAsync(
        [AsParameters] Request request,
        [FromServices] IRequestApplicationService requestApplicationService,
        CancellationToken ct)
    {
        var status = await requestApplicationService.GetStatusAsync(request.RequestId, ct);

        // An unknown flow id is indistinguishable from a never-submitted one — 404, no existence leak.
        if (status is null)
            return Problems.NotFound(ResourceRequestErrorCodes.NotFound, "Resource request not found.");

        return TypedResults.Ok(new Response(
            status.RequestId,
            status.Status,
            [.. status.Steps.Select(s => new StepSummary(s.StateName, s.ExecutionStatus))]));
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapGetResourceRequestStatusEndpoint()
        {
            app.MapGet("ResourceRequests/{requestId}", HandleAsync)
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("GetResourceRequestStatusV2")
            .WithSummary("Get the status of an async resource-creation request")
            .WithDescription("Returns the flow status and per-step name/state summary for a resource-creation request submitted via the create-and-allocate endpoint (ADR-0057). Poll this URL — supplied as the 202 Accepted Location — until the flow reaches a terminal state (Completed or Failed). 404 when the request id is unknown.")
            .WithTags("Resource")
            .Produces<Response>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}