using System.Text.Json;

using WorkflowEngine.Contracts.Models;

namespace WorkflowEngine.Contracts.Abstractions;

/// <summary>
/// Read-only view of a request aggregate, exposed to strategy implementations.
/// Strategies receive this snapshot in <see cref="IRequestStrategy.Transition"/> to inspect the overall
/// request state when computing the next step. Must never be mutated by strategy code.
/// </summary>
public interface IRequestSnapshot
{
    /// <summary>Server-generated unique identifier of the request.</summary>
    Guid RequestId { get; }

    /// <summary>Optional client identifier provided on submission.</summary>
    string? ClientId { get; }

    /// <summary>Name of the strategy that owns this request.</summary>
    string Strategy { get; }

    /// <summary>Current flow-level status.</summary>
    RequestStatus Status { get; }

    /// <summary>Current domain state name as defined by the strategy; <c>null</c> before the first step starts.</summary>
    string? CurrentState { get; }

    /// <summary>Original serialised initial step, stored as the request payload.</summary>
    JsonDocument Payload { get; }

    /// <summary>Optional callback target (e.g. URL) notified after the terminal state.</summary>
    string? CallbackUrl { get; }

    /// <summary>
    /// Reads the persisted result of a previously completed step and deserialises it to <typeparamref name="TResult"/>.
    /// <para>
    /// Lets routing/gate logic consume data produced by earlier steps without a mutable shared context.
    /// Because it reads immutable, persisted data, it is safe to call from pure transitions that may be
    /// re-run during crash-recovery replay.
    /// </para>
    /// </summary>
    /// <typeparam name="TResult">The type the named step's result was serialised from.</typeparam>
    /// <param name="stepName">The <see cref="IStepHandler{TRequest}"/> step name (state name) whose result to read.</param>
    /// <returns>The deserialised result of the named step.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no completed step with <paramref name="stepName"/> exists, the step produced no data,
    /// or its result deserialises to <c>null</c> — all of which indicate the caller asked for a result
    /// that is not available at this point in the flow (a programming error).
    /// </exception>
    TResult ResultOf<TResult>(string stepName);
}