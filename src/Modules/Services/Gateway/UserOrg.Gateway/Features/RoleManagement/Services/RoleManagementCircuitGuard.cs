namespace UserOrg.Gateway.Features.RoleManagement.Services;

/// <summary>
/// Ensures only one administrator can have an active Blazor Server circuit at a time.
/// Prevents concurrent editing conflicts in the RoleManagement UI.
/// </summary>
internal sealed class RoleManagementCircuitGuard : IDisposable
{
    private static readonly SemaphoreSlim _lock = new(1, 1);
    private static string? _activeCircuitId;
    private static DateTimeOffset _activeCircuitStarted;
    private static readonly TimeSpan _inactivityTimeout = TimeSpan.FromMinutes(3);

    private readonly ILogger<RoleManagementCircuitGuard> _logger;

    public RoleManagementCircuitGuard(ILogger<RoleManagementCircuitGuard> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Attempts to acquire circuit lock for the given circuit ID.
    /// Returns true if lock acquired or if circuit already owns the lock.
    /// Returns false if another circuit holds the lock.
    /// </summary>
    public async Task<bool> TryAcquireCircuitAsync(string circuitId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(circuitId);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            // Check if lock expired due to inactivity
            if (_activeCircuitId is not null &&
                DateTimeOffset.UtcNow - _activeCircuitStarted > _inactivityTimeout)
            {
                _logger.LogWarning(
                    "Circuit {ActiveCircuitId} lock expired after {Duration}. Releasing lock.",
                    _activeCircuitId,
                    DateTimeOffset.UtcNow - _activeCircuitStarted);
                _activeCircuitId = null;
            }

            if (_activeCircuitId is null || _activeCircuitId == circuitId)
            {
                _activeCircuitId = circuitId;
                _activeCircuitStarted = DateTimeOffset.UtcNow;
                _logger.LogInformation("Circuit {CircuitId} acquired RoleManagement lock.", circuitId);
                return true;
            }

            _logger.LogWarning(
                "Circuit {CircuitId} failed to acquire lock. Active circuit: {ActiveCircuitId}, Duration: {Duration}",
                circuitId,
                _activeCircuitId,
                DateTimeOffset.UtcNow - _activeCircuitStarted);
            return false;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Releases circuit lock if the given circuit ID owns it.
    /// </summary>
    public async Task ReleaseCircuitAsync(string circuitId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(circuitId);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_activeCircuitId == circuitId)
            {
                _logger.LogInformation(
                    "Circuit {CircuitId} released RoleManagement lock after {Duration}.",
                    circuitId,
                    DateTimeOffset.UtcNow - _activeCircuitStarted);
                _activeCircuitId = null;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Gets current lock status for diagnostic purposes.
    /// </summary>
    public (bool IsLocked, string? ActiveCircuitId, TimeSpan Duration) GetLockStatus()
    {
        return (
            _activeCircuitId is not null,
            _activeCircuitId,
            _activeCircuitId is not null ? DateTimeOffset.UtcNow - _activeCircuitStarted : TimeSpan.Zero
        );
    }

    public void Dispose()
    {
        // No resources to dispose, but implementing IDisposable for consistency
    }

    /// <summary>
    /// Resets the internal lock state. FOR TESTING PURPOSES ONLY.
    /// </summary>
    internal static void ResetForTesting()
    {
        _activeCircuitId = null;
        _activeCircuitStarted = default;
    }
}