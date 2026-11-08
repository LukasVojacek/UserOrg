using System.Text.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using UserOrganizationManagement.API.OpenApi;

namespace UserOrganizationManagement.Application.Tests;

/// <summary>
/// End-to-end proof that WithOpenApiDoc(...) reaches the AddOpenApi document pipeline, which is exactly
/// what ASP.NET Core's own WithOpenApi(...) fails to do under .NET 10.
/// </summary>
public sealed class OpenApiDocPipelineTests
{
    [Fact(DisplayName = "Should_Publish_Documentation_Written_Through_WithOpenApiDoc")]
    public async Task ShouldPublishDocumentationWrittenThroughWithOpenApiDoc()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddOpenApi("t", options =>
        {
            options.AddOperationTransformer<OpenApiDocOperationTransformer>();
            options.AddDocumentTransformer<OperationIdDocumentTransformer>();
        });

        await using var app = builder.Build();

        app.MapGet("/api/Widget/{Id}", ([FromRoute] string id) => Results.Ok(id))
           .WithOpenApiDoc(generatedOperation =>
           {
               generatedOperation.Summary = "Get a widget";
               generatedOperation.Description = "Gets one widget by id";
               generatedOperation.Parameters![0].Description = "The widget id";
               return generatedOperation;
           });

        // The parameterless overload must be an inert pass-through.
        app.MapGet("/api/Widget", () => Results.Ok()).WithOpenApiDoc();

        app.MapOpenApi("/openapi/{documentName}.json");
        await app.StartAsync();

        using var client = app.GetTestClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync(new Uri("/openapi/t.json", UriKind.Relative)));

        var byId = document.RootElement.GetProperty("paths").GetProperty("/api/Widget/{Id}").GetProperty("get");
        Assert.Equal("Get a widget", byId.GetProperty("summary").GetString());
        Assert.Equal("Gets one widget by id", byId.GetProperty("description").GetString());
        Assert.Equal("The widget id", byId.GetProperty("parameters")[0].GetProperty("description").GetString());
        Assert.Equal("GetApiWidgetById", byId.GetProperty("operationId").GetString());

        var list = document.RootElement.GetProperty("paths").GetProperty("/api/Widget").GetProperty("get");
        Assert.False(list.TryGetProperty("summary", out _));
        Assert.Equal("GetApiWidget", list.GetProperty("operationId").GetString());

        await app.StopAsync();
    }
}
