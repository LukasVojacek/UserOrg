using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Heappe.Features.Nexus;

/// <summary>Tells a failure of the wire (no usable answer) from a bug, and a caller giving up from a timeout.</summary>
internal static class HeappeTransportFailure
{
    /// <summary>
    /// True when the call produced no usable answer: connection error, open circuit, timeout. A cancellation the
    /// caller asked for is not one; it must reach the caller.
    /// </summary>
    public static bool Is(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException or BrokenCircuitException or TimeoutRejectedException or TimeoutException
        || (ex is OperationCanceledException && !ct.IsCancellationRequested);

    /// <summary>True for a timeout rather than a refused or broken connection.</summary>
    public static bool IsTimeout(Exception ex) =>
        ex is TimeoutRejectedException or TimeoutException or OperationCanceledException;
}