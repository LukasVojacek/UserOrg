namespace Core.Domain.User;

public sealed record AssignedProject(ProjectShortName ShortName, UserName AssignedBy, DateTimeOffset AssignedAt);