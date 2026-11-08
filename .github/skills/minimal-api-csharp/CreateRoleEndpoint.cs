
internal static class CreateRoleEndpoint
{
    internal sealed record Request
    {
        [Required]
        [StringLength(100, MinimumLength = 1)]
        public string Name { get; init; }

        [StringLength(500, MinimumLength = 1)]
        public string? Description { get; init; }
        public string[]? Permissions { get; init; }
    }
    internal sealed record Response(string Name);

    internal static async Task<IResult> HandleAsync(
        Request request,
        [FromServices] ILoggerFactory loggerFac,
        CancellationToken cancellationToken)
    {
        var logger = loggerFac.CreateLogger("CreateRoleEndpoint");

        // Logic to create the role would go here. For this example, we'll just return a successful response.
        var response = new Response(request.Name);
        return Results.Created($"roles/{response.Name}", response);
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapCreateRoleEndpoint()
        {
            app.MapPost("roles", HandleAsync)
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation()
        {
            return builder
                .WithName("CreateRole")
                .WithSummary("Create a new role")
                .WithDescription("Creates a new role in the system. Requires admin privileges.")
                .WithTags("Roles")
                .WithOpenApi()
                .Produces<Response>(StatusCodes.Status201Created)
                .Produces(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status401Unauthorized)
                .Produces(StatusCodes.Status403Forbidden);
        }
    }
}