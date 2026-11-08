namespace UserOrg.Shared.Persistance.Abstractions.Enums;

/// <summary>
/// Specifies the type of a location used for command template assignment and resource allocation.
/// Used by persistence entities to indicate where a command template applies (HPC cluster, cloud target, storage, etc.).
/// </summary>
public enum ELocationType
{
    /// <summary>
    /// Unspecified location type. Use when no explicit location type has been configured.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// High-performance computing (HPC) cluster location.
    /// Typical for batch/parallel compute systems (login/compute nodes).
    /// </summary>
    HPC = 1,

    /// <summary>
    /// Cloud provider location (public or private cloud).
    /// </summary>
    CLOUD = 2,

    /// <summary>
    /// Kubernetes cluster location.
    /// </summary>
    Kubernetes = 3,

    /// <summary>
    /// Generic storage target (filesystem, object store mapped as storage).
    /// </summary>
    Storage = 4,

    /// <summary>
    /// iRODS storage location.
    /// </summary>
    iRODS = 5,

    /// <summary>
    /// FirecREST API location.
    /// </summary>
    FirecREST = 6,

    /// <summary>
    /// Quantum computing or quantum simulation location.
    /// </summary>
    Quantum = 7
}