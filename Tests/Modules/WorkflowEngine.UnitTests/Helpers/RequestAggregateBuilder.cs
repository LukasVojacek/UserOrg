using System.Text.Json;
using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Core.Domain.Request;

namespace WorkflowEngine.Core.Tests.Helpers;

/// <summary>
/// Fluent test builder for <see cref="RequestAggregate"/> using the public <c>Rehydrate</c> factory.
/// Avoids tight coupling to the private constructor while still producing valid aggregates with
/// any combination of field values needed by a given test.
/// </summary>
internal static class RequestAggregateBuilder
{
    private static readonly DateTimeOffset DefaultTime = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static RequestAggregate Build(
        Guid? requestId = null,
        string strategy = "test-strategy",
        RequestStatus status = RequestStatus.Accepted,
        string? currentState = null,
        string? clientId = null,
        string? callbackUrl = null,
        string? idempotencyKey = null,
        JsonDocument? result = null,
        string? errorCode = null,
        string? errorMessage = null,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? updatedAt = null,
        DateTimeOffset? completedAt = null,
        byte[]? rowVersion = null,
        IEnumerable<RequestStep>? steps = null)
    {
        return RequestAggregate.Rehydrate(
            requestId: requestId ?? Guid.NewGuid(),
            strategy: strategy,
            payload: JsonDocument.Parse("{}"),
            status: status,
            currentState: currentState,
            clientId: clientId,
            callbackUrl: callbackUrl,
            idempotencyKey: idempotencyKey,
            result: result,
            errorCode: errorCode,
            errorMessage: errorMessage,
            createdAt: createdAt ?? DefaultTime,
            updatedAt: updatedAt ?? DefaultTime,
            completedAt: completedAt,
            rowVersion: rowVersion,
            steps: steps ?? []);
    }

    public static RequestStep BuildStep(
        Guid? stepId = null,
        Guid? requestId = null,
        string stepName = "step-1",
        StepExecutionStatus executionStatus = StepExecutionStatus.Running,
        string? messageType = null,
        JsonDocument? inputData = null,
        int stepRetries = 0,
        DateTimeOffset? nextRetryAt = null,
        string? lockedBy = null,
        DateTimeOffset? lockedUntil = null,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? completedAt = null,
        JsonDocument? result = null,
        string? errorCode = null,
        string? errorMessage = null)
    {
        return RequestStep.Rehydrate(
            stepId: stepId ?? Guid.NewGuid(),
            requestId: requestId ?? Guid.NewGuid(),
            stepName: stepName,
            executionStatus: executionStatus,
            messageType: messageType ?? typeof(TestStepRequest).AssemblyQualifiedName!,
            inputData: inputData ?? JsonDocument.Parse($$"""{"RequestId":"{{Guid.Empty}}","StateName":"{{stepName}}"}"""),
            stepRetries: stepRetries,
            nextRetryAt: nextRetryAt,
            lockedBy: lockedBy,
            lockedUntil: lockedUntil,
            startedAt: startedAt ?? DefaultTime,
            completedAt: completedAt,
            result: result,
            errorCode: errorCode,
            errorMessage: errorMessage);
    }
}
