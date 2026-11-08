namespace UserOrganizationManagement.Domain.Configuration;

public sealed class ActionLink
{
    public const string ConfigurationPath = "ActionLink";
    public int DurationInHours { get; set; } = 120;
    public TimeSpan Duration => TimeSpan.FromHours(DurationInHours);
}