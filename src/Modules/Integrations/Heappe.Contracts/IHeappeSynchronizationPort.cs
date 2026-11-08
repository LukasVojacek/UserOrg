using System;
using System.Collections.Generic;
using System.Text;

namespace Heappe.Contracts;

/// <summary>
/// Represents a port for triggering synchronization with Heappe for a specific project resource.
/// </summary>
public interface IHeappeSynchronizationPort
{
    /// <summary>
    /// Triggers synchronization for a specific project resource identified by its short name and resource name.
    /// </summary>
    /// <param name="projectShortName"></param>
    /// <param name="resourceName"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task TriggerSynchronizationForProjectResource(string projectShortName, string resourceName, CancellationToken cancellationToken);
}