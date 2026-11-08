using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

using UserOrg.Shared.OpenApi;

using UserOrganizationManagement.API.OpenApi;

namespace UserOrganizationManagement.Application.Tests;

public sealed class OpenApiDocOperationTransformerTests
{
    private static OpenApiOperationTransformerContext ContextWith(params object[] endpointMetadata)
    {
        var description = new ApiDescription
        {
            ActionDescriptor = new ActionDescriptor { EndpointMetadata = [.. endpointMetadata] }
        };

        return new OpenApiOperationTransformerContext
        {
            DocumentName = "User",
            Description = description,
            ApplicationServices = new ServiceCollection().BuildServiceProvider()
        };
    }

    [Fact(DisplayName = "Should_Apply_The_WithOpenApiDoc_Delegate_To_The_Generated_Operation")]
    public async Task ShouldApplyTheWithOpenApiDocDelegateToTheGeneratedOperation()
    {
        var operation = new OpenApiOperation
        {
            Parameters = [new OpenApiParameter { Name = "Id", In = ParameterLocation.Path }]
        };

        var context = ContextWith(new OpenApiDocMetadata(generatedOperation =>
        {
            generatedOperation.Summary = "Get a provider";
            generatedOperation.Description = "Get a specific LEXIS provider";
            generatedOperation.Parameters![0].Description = "Provider Service Key";
            return generatedOperation;
        }));

        await new OpenApiDocOperationTransformer().TransformAsync(operation, context, CancellationToken.None);

        Assert.Equal("Get a provider", operation.Summary);
        Assert.Equal("Get a specific LEXIS provider", operation.Description);
        Assert.Equal("Provider Service Key", operation.Parameters![0].Description);
    }

    [Fact(DisplayName = "Should_Do_Nothing_Without_OpenApiDoc_Metadata")]
    public async Task ShouldDoNothingWithoutOpenApiDocMetadata()
    {
        var operation = new OpenApiOperation { Description = "untouched" };

        await new OpenApiDocOperationTransformer().TransformAsync(operation, ContextWith(), CancellationToken.None);

        Assert.Equal("untouched", operation.Description);
        Assert.Null(operation.Summary);
    }

    [Fact(DisplayName = "Should_Apply_Multiple_Delegates_In_Metadata_Order")]
    public async Task ShouldApplyMultipleDelegatesInMetadataOrder()
    {
        var operation = new OpenApiOperation();

        var context = ContextWith(
            new OpenApiDocMetadata(op => { op.Description = "first"; return op; }),
            new OpenApiDocMetadata(op => { op.Description = "second"; return op; }));

        await new OpenApiDocOperationTransformer().TransformAsync(operation, context, CancellationToken.None);

        Assert.Equal("second", operation.Description);
    }

    [Fact(DisplayName = "Should_Store_The_Delegate_As_Endpoint_Metadata")]
    public void ShouldStoreTheDelegateAsEndpointMetadata()
    {
        var builder = new RecordingConventionBuilder();

        var returned = builder.WithOpenApiDoc(op => { op.Summary = "documented"; return op; });

        Assert.Same(builder, returned);

        var endpointBuilder = new RouteEndpointBuilder(_ => Task.CompletedTask, RoutePatternFactory.Parse("/x"), 0);
        builder.ApplyTo(endpointBuilder);

        var metadata = Assert.IsType<OpenApiDocMetadata>(endpointBuilder.Metadata.Single());

        var operation = new OpenApiOperation();
        metadata.Configure(operation);
        Assert.Equal("documented", operation.Summary);
    }

    [Fact(DisplayName = "Should_Add_No_Metadata_For_The_Parameterless_Overload")]
    public void ShouldAddNoMetadataForTheParameterlessOverload()
    {
        var builder = new RecordingConventionBuilder();

        var returned = builder.WithOpenApiDoc();

        Assert.Same(builder, returned);

        var endpointBuilder = new RouteEndpointBuilder(_ => Task.CompletedTask, RoutePatternFactory.Parse("/x"), 0);
        builder.ApplyTo(endpointBuilder);

        Assert.Empty(endpointBuilder.Metadata);
    }

    private sealed class RecordingConventionBuilder : IEndpointConventionBuilder
    {
        private readonly List<Action<EndpointBuilder>> _conventions = [];

        public void Add(Action<EndpointBuilder> convention) => _conventions.Add(convention);

        public void ApplyTo(EndpointBuilder endpointBuilder)
        {
            foreach (var convention in _conventions)
            {
                convention(endpointBuilder);
            }
        }
    }
}
