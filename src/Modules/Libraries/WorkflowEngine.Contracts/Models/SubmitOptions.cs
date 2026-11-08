namespace WorkflowEngine.Contracts.Models;

/// <summary>
/// Optional submission metadata for starting a request. Replaces the trailing nullable-string parameters
/// on the low-level submit surface.
/// </summary>
/// <param name="ClientId">Optional identifier of the caller (for audit/filtering).</param>
/// <param name="CallbackUrl">Optional opaque callback target invoked on terminal state.</param>
/// <param name="IdempotencyKey">Optional key for deduplicating submission attempts.</param>
public sealed record SubmitOptions(
    string? ClientId = null,
    string? CallbackUrl = null,
    string? IdempotencyKey = null);