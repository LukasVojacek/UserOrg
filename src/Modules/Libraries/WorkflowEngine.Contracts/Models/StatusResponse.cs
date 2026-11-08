using System.Collections.Immutable;
using System.Text.Json;

namespace WorkflowEngine.Contracts.Models;

/// <summary>
/// Per-step detail returned inside <see cref="StatusResponse"/>.
/// </summary>
public sealed record StepStatusItem(
    string StateName,
    StepExecutionStatus ExecutionStatus,
    int StepRetries,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? NextRetryAt,
    string? ErrorCode,
    string? ErrorMessage);

/// <summary>
/// Full status snapshot returned by <c>IRequestApplicationService.GetStatusAsync</c>.
/// </summary>
public sealed record StatusResponse(
    Guid RequestId,
    string? ClientId,
    string Strategy,
    RequestStatus Status,
    string? CurrentState,
    ImmutableList<StepStatusItem> Steps,
    JsonDocument? Result,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt);