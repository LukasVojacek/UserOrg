namespace WorkflowEngine.Core.DependencyInjection;

/// <summary>
/// Configuration options for the WorkflowEngine module.
/// Bind from <c>appsettings.json</c> section <c>"WorkflowEngine"</c>.
/// </summary>
public sealed class WorkflowEngineOptions
{
    /// <summary>Section name used for <see cref="IConfiguration"/> binding.</summary>
    public const string SectionName = "WorkflowEngine";

    /// <summary>
    /// Unique identifier for this worker instance.
    /// Used as the <c>LockedBy</c> value when acquiring step leases.
    /// Defaults to a random GUID generated at startup.
    /// </summary>
    public string WorkerId { get; init; } = Guid.CreateVersion7().ToString();

    /// <summary>
    /// How long a step lease is held before it is considered expired and reclaimable.
    /// Default: 5 minutes.
    /// </summary>
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Maximum number of retry attempts per step before it is permanently failed.
    /// Default: 1.
    /// </summary>
    public int MaxStepRetries { get; init; } = 1;

    /// <summary>
    /// Base back-off duration for the first retry.
    /// Subsequent retries use exponential back-off: <c>Base * 2^(attempt-1)</c>.
    /// Default: 10 seconds.
    /// </summary>
    public TimeSpan RetryBackoffBase { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How often <c>RetryScheduledScanner</c> polls for due retries.
    /// Default: 2 seconds.
    /// </summary>
    public TimeSpan RetryPollingInterval { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Channel capacity for the in-process <c>ChannelQueueAdapter</c>.
    /// Default: 1000.
    /// </summary>
    public int QueueCapacity { get; init; } = 1000;

    /// <summary>
    /// How many consecutive times <c>RetryScheduledScanner</c> tolerates failing to turn a due
    /// <c>RetryScheduled</c> step back into a message — an unresolvable <c>MessageType</c>, a deserialization
    /// exception, or a payload that deserializes to <c>null</c> — before it gives up and fails the step
    /// (<c>RecordStepFailed</c>/<c>MarkFailed</c>) instead of releasing the lease for another poll.
    /// <para>
    /// This is a distinct budget from <see cref="MaxStepRetries"/>: that one bounds the handler-level
    /// retries <c>RequestWorker</c> schedules after a step has been successfully dispatched and failed
    /// transiently. This one bounds the scanner's own attempts to materialise a message that was never
    /// dispatchable in the first place — a defect the message never recovers from by polling again.
    /// </para>
    /// Default: 3.
    /// </summary>
    public int MaxMaterializationFailures { get; init; } = 3;
}