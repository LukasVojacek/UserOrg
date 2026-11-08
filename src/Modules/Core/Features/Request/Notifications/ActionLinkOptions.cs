namespace Core.Features.Request.Notifications;

/// <summary>
/// Action-link settings, bound to the shared <c>ActionLink</c> section. <see cref="DurationInHours"/>
/// controls how long a minted link stays valid.
/// </summary>
internal sealed class ActionLinkOptions
{
    public const string ConfigurationPath = "ActionLink";

    public int DurationInHours { get; set; } = 120;

    public TimeSpan Duration => TimeSpan.FromHours(DurationInHours);
}