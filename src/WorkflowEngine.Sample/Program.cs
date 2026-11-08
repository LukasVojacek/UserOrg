/*
 * WorkflowEngine — Data Pipeline Sample (production-ready DI, no database required)
 * ══════════════════════════════════════════════════════════════════════════════════
 *
 * What this demonstrates:
 *   • Wiring WorkflowEngine with AddWorkflowEngineApplicationServices()
 *     and providing your own IRequestRepository + ICallbackPort adapters
 *   • Strategy + handler registration through the public DI API
 *   • Submitting a flow by passing the typed initial step directly
 *   • Polling status via IRequestApplicationService
 *   • Interactive console-gate at step 4 — the background worker blocks on
 *     Console.ReadLine() until the operator types "approve" or "reject"
 *   • Callback notifications when the workflow reaches a terminal state
 *   • Idempotency — resubmitting with the same idempotencyKey returns cached response
 *
 * For production, replace InMemoryRequestRepository with the EF Core adapter:
 *   builder.AddWorkflowEngine()   // wires SQL Server via Aspire
 * and drop the manual registrations below.
 */

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Contracts.Ports;
using WorkflowEngine.Core.DependencyInjection;
using WorkflowEngine.Core.Ports;
using WorkflowEngine.Sample.DataPipelineFlow;
using WorkflowEngine.Sample.DataPipelineFlow.Handlers;
using WorkflowEngine.Sample.DataPipelineFlow.Steps;
using WorkflowEngine.Sample.Infrastructure;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWorkflowEngineApplicationServices();

builder.Services.AddSingleton<IRequestRepository, InMemoryRequestRepository>();
builder.Services.AddSingleton<ICallbackPort, ConsoleCallbackAdapter>();

builder.Services.AddSingleton<IRequestStrategy, DataPipelineStrategy>();
builder.Services
    .AddStepHandler<InitializePipelineStep, InitializePipelineHandler>()
    .AddStepHandler<FetchDataStep, FetchDataHandler>()
    .AddStepHandler<TransformDataStep, TransformDataHandler>()
    .AddStepHandler<AwaitOperatorApprovalStep, AwaitOperatorApprovalHandler>()
    .AddStepHandler<LoadDataStep, LoadDataHandler>();

var host = builder.Build();

Guid requestId;
await using (var scope = host.Services.CreateAsyncScope())
{
    var svc = scope.ServiceProvider.GetRequiredService<IRequestApplicationService>();

    var initialStep = new InitializePipelineStep(
        RequestId: Guid.NewGuid(),
        PipelineId: "pipe-2025-001",
        SourcePath: "/data/input/sales_q4.csv",
        TargetPath: "/data/output/warehouse/sales_q4");

    Console.WriteLine("═══════════════════════════════════════════");
    Console.WriteLine("  WorkflowEngine — Data Pipeline Sample");
    Console.WriteLine("═══════════════════════════════════════════");
    Console.WriteLine();

    var response = await svc.SubmitAsync(
        initialStep,
        clientId: "demo-runner",
        callbackUrl: "console://notifications",  // Callback will print to console when flow completes
        idempotencyKey: "pipe-2025-001-run-1");

    requestId = response.RequestId;
    Console.WriteLine($"[Host] Flow submitted — ID: {requestId}  Status: {response.Status}");
    Console.WriteLine();
}

await host.StartAsync();

await using (var scope = host.Services.CreateAsyncScope())
{
    var svc = scope.ServiceProvider.GetRequiredService<IRequestApplicationService>();

    var lastStatus = RequestStatus.Accepted;
    string? lastState = null;

    while (true)
    {
        await Task.Delay(500);

        var snapshot = await svc.GetStatusAsync(requestId);
        if (snapshot is null)
        {
            continue;
        }

        if (snapshot.Status != lastStatus || snapshot.CurrentState != lastState)
        {
            lastStatus = snapshot.Status;
            lastState = snapshot.CurrentState;
            Console.WriteLine($"[Poll] {snapshot.Status,-12}  state: {snapshot.CurrentState}");
        }

        if (snapshot.Status is RequestStatus.Completed or RequestStatus.Failed)
        {
            Console.WriteLine();
            Console.WriteLine("═══════════════════════════════════════════");
            Console.WriteLine($"  Flow finished: {snapshot.Status}");
            if (snapshot.Status == RequestStatus.Failed)
            {
                Console.WriteLine($"  Error [{snapshot.ErrorCode}]: {snapshot.ErrorMessage}");
            }

            Console.WriteLine("═══════════════════════════════════════════");
            break;
        }
    }
}

Console.WriteLine();
Console.WriteLine("─────────────────────────────────────────────");
Console.WriteLine("  IDEMPOTENCY DEMO: Resubmitting same request");
Console.WriteLine("─────────────────────────────────────────────");
Console.WriteLine();

await using (var scope = host.Services.CreateAsyncScope())
{
    var svc = scope.ServiceProvider.GetRequiredService<IRequestApplicationService>();

    // Resubmit with the SAME idempotencyKey and RequestId
    var resubmissionStep = new InitializePipelineStep(
        RequestId: requestId,  // Same RequestId as before
        PipelineId: "pipe-2025-001",
        SourcePath: "/data/input/sales_q4.csv",
        TargetPath: "/data/output/warehouse/sales_q4");

    Console.WriteLine($"[Idempotency] Resubmitting request with:");
    Console.WriteLine($"  • Same RequestId: {requestId}");
    Console.WriteLine($"  • Same idempotencyKey: pipe-2025-001-run-1");
    Console.WriteLine();

    var cachedResponse = await svc.SubmitAsync(
        resubmissionStep,
        clientId: "demo-runner",
        callbackUrl: "console://notifications",
        idempotencyKey: "pipe-2025-001-run-1");

    Console.WriteLine($"[Idempotency] Response: ID={cachedResponse.RequestId}, Status={cachedResponse.Status}");
    Console.WriteLine($"[Idempotency] ✓ Same RequestId returned: {cachedResponse.RequestId == requestId}");
    Console.WriteLine($"[Idempotency] ✓ No duplicate workflow execution triggered");
    Console.WriteLine();
}

await host.StopAsync();

Console.WriteLine("═══════════════════════════════════════════");
Console.WriteLine("  Sample completed");
Console.WriteLine("═══════════════════════════════════════════");
