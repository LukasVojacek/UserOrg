# WorkflowEngine — Strategy Implementation Guide

> ⚠️ **Partially stale — read with care.** This guide documents the current low-level strategy/handler API
> (`IRequestStrategy`, `IStepHandler<T>`, manual `AddStepHandler<,>` registration). Two things here are
> superseded or wrong: the recommended **retry-as-poll** approval pattern (§12) is broken under the
> default `MaxStepRetries = 1` and is replaced by the now-implemented first-class Gate/Signal/Resume; and
> `CreateInitialMessage` (§2/§4) does not exist on the current interface. For the **current public API**
> (fluent builder DSL, Start/Resume — now implemented) see [`PublicApiDesign.md`](./PublicApiDesign.md). Canonical terms:
> [`WorkflowEngine.Core/CONTEXT.md`](../../../src/Modules/Libraries/WorkflowEngine.Core/CONTEXT.md).

## Table of Contents

1. [Overview](#1-overview)
2. [Core contracts](#2-core-contracts)
3. [Step 1 — Define step request records](#3-step-1--define-step-request-records)
4. [Step 2 — Implement the strategy](#4-step-2--implement-the-strategy)
5. [Step 3 — Implement step handlers](#5-step-3--implement-step-handlers)
6. [Step 4 — Driven ports](#6-step-4--driven-ports)
7. [Step 5 — DI registration](#7-step-5--di-registration)
8. [Registration variants](#8-registration-variants)
9. [Controlling the flow — IRequestApplicationService](#9-controlling-the-flow--irequestapplicationservice)
10. [Error handling](#10-error-handling)
11. [Configuration reference](#11-configuration-reference)
12. [Complete example](#12-complete-example)

---

## 1. Overview

A **strategy** is a self-contained typed state machine registered in the WorkflowEngine module. It
owns three things:

| Concept | Interface / type | Responsibility |
|---|---|---|
| Strategy definition | `IRequestStrategy` | State names, initial message creation, transition table |
| Step messages | `StepRequest` subclasses | Per-state typed input data (one record per state) |
| Step handlers | `IStepHandler<TRequest>` | Business logic for a single state |

The engine drives the flow: load aggregate → resolve handler → execute → persist → enqueue next.
Strategies and handlers have no knowledge of the queue, workers, or persistence.

---

## 2. Core contracts

### `IRequestStrategy`

```csharp
public interface IRequestStrategy
{
    string Name { get; }  // unique key, e.g. "provision-vm"
    StepRequest  CreateInitialMessage(IRequestSnapshot request);
    StepRequest? Transition(StepRequest completedStep, StepResult? stepResult, IRequestSnapshot request);
}
```

- `Name` — must be globally unique; used in `SubmitCommand.Strategy`.
- `CreateInitialMessage` — called synchronously inside `SubmitAsync`; reads `request.Payload` to
  build the first typed message.
- `Transition` — pure function; return the next `StepRequest` or `null` to close the flow.

### `IStepHandler<TRequest>`

```csharp
public interface IStepHandler<in TRequest> where TRequest : StepRequest
{
    Task<StepResult> HandleAsync(TRequest request, CancellationToken ct);
}
```

- One handler per state.
- Return `new StepResult(jsonDocument)` on success.
- Throw `RetryableException` for transient failures (see [§10](#10-error-handling)).
- Throw `PermanentException` for unrecoverable failures.

### `StepRequest`

```csharp
public abstract record StepRequest(Guid RequestId, string StateName);
```

Each concrete subtype carries exactly the data its handler needs — nothing more.

---

## 3. Step 1 — Define step request records

Create one record per state. Derive from `StepRequest` and pass the matching state name constant.

```csharp
// MyFlow/StepRequests/ValidateRequest.cs
public sealed record ValidateRequest(Guid RequestId, string RawInput)
    : StepRequest(RequestId, MyFlowStrategy.States.Validate);

// MyFlow/StepRequests/ProcessRequest.cs
public sealed record ProcessRequest(Guid RequestId, string ValidatedInput, int Priority)
    : StepRequest(RequestId, MyFlowStrategy.States.Process);

// MyFlow/StepRequests/NotifyRequest.cs
public sealed record NotifyRequest(Guid RequestId, string ProcessedOutput)
    : StepRequest(RequestId, MyFlowStrategy.States.Notify);
```

**Rules:**
- `StateName` in the constructor must match the constant used in `Transition`.
- Carry only the data the handler for that state needs. Data from previous steps is extracted in
  `Transition` and forwarded explicitly — handlers must not read from the aggregate.

---

## 4. Step 2 — Implement the strategy

```csharp
// MyFlow/MyFlowStrategy.cs
using System.Text.Json;
using WorkflowEngine.Contracts.Abstractions;
using MyApp.WorkflowStrategies.MyFlow.StepRequests;

public sealed class MyFlowStrategy : IRequestStrategy
{
    public static class States
    {
        public const string Validate = "Validate";
        public const string Process  = "Process";
        public const string Notify   = "Notify";   // terminal
    }

    public string Name => "my-flow";

    public StepRequest CreateInitialMessage(IRequestSnapshot request)
    {
        var rawInput = request.Payload.RootElement.GetProperty("input").GetString()
            ?? throw new ArgumentException("Missing 'input' in payload.");

        return new ValidateRequest(request.RequestId, rawInput);
    }

    public StepRequest? Transition(
        StepRequest completedStep,
        StepResult? stepResult,
        IRequestSnapshot request)
        => completedStep switch
        {
            ValidateRequest => BuildProcessRequest(request.RequestId, stepResult),
            ProcessRequest  => BuildNotifyRequest(request.RequestId, stepResult),
            NotifyRequest   => null,  // terminal — flow is complete
            _ => throw new InvalidOperationException(
                     $"MyFlowStrategy: unrecognized step type '{completedStep.GetType().Name}'."),
        };

    private static ProcessRequest BuildProcessRequest(Guid requestId, StepResult? stepResult)
    {
        var root = stepResult?.Data?.RootElement
            ?? throw new InvalidOperationException("Validate produced no data.");

        return new ProcessRequest(
            requestId,
            root.GetProperty("validatedInput").GetString()!,
            root.GetProperty("priority").GetInt32());
    }

    private static NotifyRequest BuildNotifyRequest(Guid requestId, StepResult? stepResult)
    {
        var output = stepResult?.Data?.RootElement.GetProperty("processedOutput").GetString()
            ?? throw new InvalidOperationException("Process produced no data.");

        return new NotifyRequest(requestId, output);
    }
}
```

**Rules:**
- `Transition` must be pure — no I/O, no side effects.
- Always throw `InvalidOperationException` on unrecognized state names (programming error).
- Return `null` from the terminal state.

---

## 5. Step 3 — Implement step handlers

### Async handler (with I/O)

```csharp
// MyFlow/Handlers/ValidateHandler.cs
using System.Text.Json;
using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Contracts.Exceptions;

public sealed class ValidateHandler : IStepHandler<ValidateRequest>
{
    private readonly IValidationService _validation;

    public ValidateHandler(IValidationService validation)
        => _validation = validation;

    public async Task<StepResult> HandleAsync(ValidateRequest request, CancellationToken ct)
    {
        var result = await _validation.ValidateAsync(request.RawInput, ct);

        if (!result.IsValid)
            throw new PermanentException("VALIDATION_FAILED", result.ErrorMessage);

        var json = JsonSerializer.SerializeToDocument(new
        {
            validatedInput = result.NormalizedInput,
            priority       = result.Priority,
        });

        return new StepResult(json);
    }
}
```

### Synchronous handler (no I/O — returns Task.FromResult)

```csharp
// MyFlow/Handlers/NotifyHandler.cs
public sealed class NotifyHandler : IStepHandler<NotifyRequest>
{
    private readonly INotificationPort _notifications;

    public NotifyHandler(INotificationPort notifications)
        => _notifications = notifications;

    public Task<StepResult> HandleAsync(NotifyRequest request, CancellationToken ct)
    {
        _notifications.Enqueue(request.ProcessedOutput);  // fire-and-forget local enqueue
        return Task.FromResult(new StepResult(null));      // no output data needed
    }
}
```

> **Note:** Both sync and async handlers are fully supported. The engine resolves handlers through
> the `IStepHandlerInvoker` adapter, so synchronous throws are never silently swallowed.

---

## 6. Step 4 — Driven ports

If a handler needs to communicate with an external system, define a **driven port interface** in
your strategy project. The consuming application provides the adapter.

```csharp
// MyFlow/Ports/IValidationService.cs
public interface IValidationService
{
    Task<ValidationResult> ValidateAsync(string input, CancellationToken ct);
}

// MyFlow/Ports/INotificationPort.cs
public interface INotificationPort
{
    void Enqueue(string message);
}
```

Register adapter implementations in DI alongside the handlers (see §7).

---

## 7. Step 5 — DI registration

### Production application (Aspire / ASP.NET Core)

In `Program.cs`, after `builder.AddWorkflowEngine()` (which wires persistence + workers):

```csharp
// Strategy
builder.Services.AddSingleton<IRequestStrategy, MyFlowStrategy>();

// Driven-port adapters
builder.Services.AddScoped<IValidationService, HttpValidationService>();
builder.Services.AddSingleton<INotificationPort, RabbitMqNotificationPort>();

// Step handlers — use AddStepHandler<TRequest, THandler> (registers handler + invoker)
builder.Services.AddStepHandler<ValidateRequest, ValidateHandler>();
builder.Services.AddStepHandler<ProcessRequest,  ProcessHandler>();
builder.Services.AddStepHandler<NotifyRequest,   NotifyHandler>();
```

**Always use `AddStepHandler<>` instead of plain `AddScoped<IStepHandler<>, ...>`.**
`AddStepHandler<>` registers both the typed handler and the non-generic `IStepHandlerInvoker`
that the worker uses for dispatch without reflection.

---

## 8. Registration variants

### 8.1 Default — Scoped lifetime (recommended)

```csharp
builder.Services.AddStepHandler<ValidateRequest, ValidateHandler>();
// equivalent to: ServiceLifetime.Scoped (default)
```

Each worker dispatch creates a new DI scope, so Scoped is the correct default. Handlers can
safely inject Scoped services (e.g. `DbContext`).

### 8.2 Singleton lifetime

Use only when the handler holds no per-request mutable state and all its dependencies are
Singleton-safe.

```csharp
builder.Services.AddStepHandler<NotifyRequest, NotifyHandler>(ServiceLifetime.Singleton);
```

### 8.3 Transient lifetime

Rarely needed. Use when a fresh instance is required for every single invocation.

```csharp
builder.Services.AddStepHandler<ProcessRequest, ProcessHandler>(ServiceLifetime.Transient);
```

### 8.4 Custom registration (multiple strategies)

Register handlers for each strategy in isolation. The engine dispatches by `message.GetType()`,
so there is never ambiguity across strategies even when two strategies share a state name.

```csharp
// Strategy A
builder.Services.AddSingleton<IRequestStrategy, ProvisionVmStrategy>();
builder.Services.AddStepHandler<ProvisionVm.Validate,  ProvisionVmValidateHandler>();
builder.Services.AddStepHandler<ProvisionVm.Deploy,    ProvisionVmDeployHandler>();

// Strategy B
builder.Services.AddSingleton<IRequestStrategy, SendEmailStrategy>();
builder.Services.AddStepHandler<SendEmail.Compose, ComposeEmailHandler>();
builder.Services.AddStepHandler<SendEmail.Send,    SendEmailHandler>();
```

### 8.5 Demo / test setup — without SQL Server

Use `AddWorkflowEngineApplicationServices()` to wire the engine with a custom persistence adapter:

```csharp
var builder = Host.CreateApplicationBuilder();

// Module options
builder.Services.AddSingleton<IOptions<RequestsModuleOptions>>(
    Options.Create(new RequestsModuleOptions
    {
        QueueCapacity    = 100,
        MaxStepRetries   = 3,
        RetryBackoffBase = TimeSpan.FromSeconds(1),
    }));

// Engine — application layer + workers, no SQL wiring
builder.Services.AddWorkflowEngineApplicationServices();

// Adapters (swap for real implementations in production)
builder.Services.AddSingleton<IRequestRepository, InMemoryRequestRepository>();
builder.Services.AddSingleton<ICallbackPort>(NullCallbackPort.Instance);

// Strategy + handlers (same as production)
builder.Services.AddSingleton<IRequestStrategy, MyFlowStrategy>();
builder.Services.AddStepHandler<ValidateRequest, ValidateHandler>();
builder.Services.AddStepHandler<ProcessRequest,  ProcessHandler>();
builder.Services.AddStepHandler<NotifyRequest,   NotifyHandler>();

var host = builder.Build();
await host.StartAsync();
```

---

## 9. Controlling the flow — `IRequestApplicationService`

Resolve from DI inside a scope (e.g. from an HTTP endpoint or background service):

```csharp
using var scope = host.Services.CreateScope();
var engine = scope.ServiceProvider.GetRequiredService<IRequestApplicationService>();
```

### Submit a new request

```csharp
var response = await engine.SubmitAsync(
    SubmitCommandFactory.Create(
        strategy: "my-flow",
        payload:  JsonDocument.Parse("""{"input": "raw-data"}""")),
    ct);

// response.Status  == RequestStatus.Accepted
// response.RequestId — use this to track progress
```

Optional fields on `SubmitCommand`:

| Property | Type | Description |
|---|---|---|
| `ClientId` | `string?` | Client-supplied correlation ID |
| `IdempotencyKey` | `string?` | Deduplication key; duplicate submissions return the original response |
| `CallbackUrl` | `string?` | HTTP URL notified with a signed POST after the terminal step |

### Poll for progress

```csharp
var status = await engine.GetStatusAsync(requestId, ct);

status.Status          // RequestStatus: Accepted | InProgress | Completed | Failed
status.CurrentState    // current domain state name, e.g. "Process"
status.Steps           // IReadOnlyList<StepStatusItem> — per-step execution history
status.Result          // JsonDocument? — final output (set on Completed)
status.ErrorCode       // string? — set on Failed
```

### Re-enqueue a stuck step (after human approval, external event, etc.)

```csharp
// Re-enqueue the most recently started step
await engine.RetryLastStepAsync(requestId, ct);

// Re-enqueue a specific named step
await engine.RetryStepAsync(requestId, "AwaitApproval", ct);
```

### Administratively close a flow

```csharp
await engine.MarkFlowFailedAsync(requestId, "MANUALLY_CANCELLED", "Cancelled by operator.", ct);
```

### Query history and statistics

```csharp
var page = await engine.ListAsync(
    new RequestFilter { Strategy = "my-flow", PageSize = 20, Page = 1 }, ct);

var stats = await engine.GetStatsAsync(
    new StatsFilter { Strategy = "my-flow" }, ct);
```

---

## 10. Error handling

### Transient failures — `RetryableException`

Throw when the failure is temporary and re-running the step may succeed.

```csharp
// Simple message
throw new RetryableException("Remote service returned 503 — will retry.");

// With error code
throw new RetryableException("EXTERNAL_API_TIMEOUT", "External API did not respond within 30 s.");

// Wrapping an inner exception
throw new RetryableException("EXTERNAL_API_TIMEOUT", "External API timed out.", ex);
```

The worker schedules a retry with exponential back-off (`RetryBackoffBase * 2^(attempt-1)`). After
`MaxStepRetries` attempts the step is permanently failed.

### Permanent failures — `PermanentException`

Throw when the failure is deterministic and retrying would always fail.

```csharp
throw new PermanentException("VALIDATION_FAILED", "Payload is missing required field 'projectId'.");
throw new PermanentException("INVALID_STATE",     "Request cannot be provisioned in current state.");
```

The worker immediately marks both the step and the flow as `Failed`, bypassing the retry budget.

### Behaviour summary

| Exception | Step result | Flow result | Retried? |
|---|---|---|---|
| `RetryableException` (budget remaining) | `RetryScheduled` | `InProgress` | Yes — after back-off |
| `RetryableException` (budget exhausted) | `Failed` | `Failed` | No |
| `PermanentException` | `Failed` | `Failed` | No |
| Any other `Exception` | `Failed` | `Failed` | No |

---

## 11. Configuration reference

Bind from `appsettings.json` under the `"WorkflowEngine"` section:

```json
{
  "WorkflowEngine": {
    "WorkerId":            "worker-1",
    "LeaseDuration":       "00:05:00",
    "MaxStepRetries":      3,
    "RetryBackoffBase":    "00:00:10",
    "RetryPollingInterval":"00:00:02",
    "QueueCapacity":       1000
  }
}
```

| Option | Default | Description |
|---|---|---|
| `WorkerId` | random GUID | Unique instance identifier; used as `LockedBy` in step leases |
| `LeaseDuration` | 5 min | How long a step lease is held before it can be reclaimed |
| `MaxStepRetries` | 3 | Maximum retry attempts per step before permanent failure |
| `RetryBackoffBase` | 10 s | Base delay for the first retry; doubles each attempt |
| `RetryPollingInterval` | 2 s | How often `RetryScheduledScanner` polls for due retries |
| `QueueCapacity` | 1000 | `Channel<StepRequest>` capacity (back-pressure threshold) |

---

## 12. Complete example

The `SampleApprovalFlowStrategy` in `WorkflowEngine.Strategies` is a fully runnable 4-step
reference implementation demonstrating:

- Typed value forwarding between steps (`PrepareRequest → EnrichRequest → AwaitApprovalRequest → FinalizeRequest`)
- Human-in-the-loop gate (`AwaitApprovalHandler` throws `RetryableException` until an approval arrives)
- Driven port (`IApprovalService`) injected into the handler
- End-to-end demo via `IHost` in `SampleApprovalFlowDemo` (xUnit test as a runnable medium)

**Namespace layout:**

```
WorkflowEngine.Strategies/
└── SampleApprovalFlow/
    ├── SampleApprovalFlowStrategy.cs   ← IRequestStrategy implementation
    ├── Handlers/
    │   ├── PrepareHandler.cs
    │   ├── EnrichHandler.cs
    │   ├── AwaitApprovalHandler.cs
    │   └── FinalizeHandler.cs
    ├── Ports/
    │   └── IApprovalService.cs         ← driven port; implement in consuming project
    └── StepRequests/
        ├── PrepareRequest.cs
        ├── EnrichRequest.cs
        ├── AwaitApprovalRequest.cs
        └── FinalizeRequest.cs
```

**DI registration for this strategy:**

```csharp
builder.Services.AddSingleton<IRequestStrategy, SampleApprovalFlowStrategy>();
builder.Services.AddSingleton<IApprovalService, YourApprovalServiceAdapter>();
builder.Services.AddStepHandler<PrepareRequest,       PrepareHandler>();
builder.Services.AddStepHandler<EnrichRequest,        EnrichHandler>();
builder.Services.AddStepHandler<AwaitApprovalRequest, AwaitApprovalHandler>();
builder.Services.AddStepHandler<FinalizeRequest,      FinalizeHandler>();
```

**Submitting a request:**

```csharp
var response = await engine.SubmitAsync(
    SubmitCommandFactory.Create(
        strategy: "sample-approval-flow",
        payload:  JsonDocument.Parse("""{"value": 10}""")),
    ct);
```

**Providing approval and re-enqueuing:**

```csharp
// 1. External system records the decision (e.g. REST endpoint writes to DB)
await approvalStore.SaveAsync(requestId, "operator@example.com", ct);

// 2. Same endpoint re-enqueues the waiting step
await engine.RetryLastStepAsync(requestId, ct);
```
