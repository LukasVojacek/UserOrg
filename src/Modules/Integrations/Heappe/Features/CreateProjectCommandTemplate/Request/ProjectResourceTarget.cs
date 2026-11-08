namespace Heappe.Features.CreateProjectCommandTemplate.Request;

/// <summary>
/// Represents the target project and resource for command template creation.
/// </summary>
/// <param name="ProjectShortName">The project short name.</param>
/// <param name="ResourceName">The resource name.</param>
public sealed record ProjectResourceTarget(
    string ProjectShortName,
    string ResourceName);