namespace Heappe.Features.Connection;

/// <summary>
/// Resilience of the delegation client: one attempt (retries are off) of at most <see cref="AttemptTimeout"/> within
/// <see cref="TotalRequestTimeout"/>, and a breaker that opens after <see cref="MinimumThroughput"/> failures within
/// <see cref="SamplingDuration"/> for <see cref="BreakDuration"/> (QS-04).
/// </summary>
internal sealed class TokenExchangeResilienceOptions
{
    /// <summary>
    /// The timeout of a single attempt.
    /// </summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The timeout of the whole request.
    /// </summary>
    public TimeSpan TotalRequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The number of calls within the sampling duration needed before the breaker can open.
    /// </summary>
    public int MinimumThroughput { get; set; } = 5;

    /// <summary>
    /// The window in which the breaker samples failures.
    /// </summary>
    public TimeSpan SamplingDuration { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long the breaker stays open.
    /// </summary>
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(30);
}
