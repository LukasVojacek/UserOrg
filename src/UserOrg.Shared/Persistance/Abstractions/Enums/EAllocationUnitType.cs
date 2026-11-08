using System;
using System.Collections.Generic;
using System.Text;

namespace UserOrg.Shared.Persistance.Abstractions.Enums;

/// <summary>
/// Specifies the type of allocation unit used for resource allocation and quota management.
/// </summary>
public enum EAllocationUnitType
{
    /// <summary>
    /// No allocation unit type specified. Use when allocation unit type is not applicable or has not been defined.
    /// </summary>
    None = 0,
    /// <summary>
    /// Unspecified allocation unit type. Use when no explicit unit type has been configured.
    /// </summary>
    /// <remarks>HEAppE - 0</remarks>
    Unspecified = 1,
    /// <summary>
    /// Allocation unit based on node hours (e.g., 1 node running for 1 hour = 1 node hour).
    /// </summary>
    /// <remarks>HEAppE - 1</remarks>
    NodeHours = 2,
    /// <summary>
    /// Allocation unit based on core hours (e.g., 1 core running for 1 hour = 1 core hour).
    /// </summary>
    /// <remarks>HEAppE - 2</remarks>
    CoreHours = 3,
    /// <summary>
    /// Allocation unit based on credits.
    /// </summary>
    Credits = 4,
    /// <summary>
    /// Allocation unit based on gigabytes (GB).
    /// </summary>
    GB = 5,
    /// <summary>
    /// Allocation unit based on QPU seconds.
    /// </summary>
    QPUSeconds = 6,
}