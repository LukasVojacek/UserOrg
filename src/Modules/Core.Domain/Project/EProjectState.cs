namespace Core.Domain.Project;

/// <summary>
/// Project lifecycle state — orthogonal to <see cref="EOperationMode"/>. Defines business-level
/// phases of a project: Active (operational), Expired (past validity period), or Deleted (soft-deleted).
/// A project is created in Active state and transitions to Expired when its EndDate elapses, or to
/// Deleted when explicitly removed by the project owner/admin.
///
/// Note: Project does NOT use a Draft state; once created, it is Active and ready (Mode determines editability).
/// </summary>
public enum EProjectState
{
    /// <summary>
    /// Project is operational and current, within its validity period.
    /// </summary>
    Active = 1,

    /// <summary>
    /// Project is past its EndDate; archived and no longer accepting operations.
    /// </summary>
    Expired = 2,

    /// <summary>
    /// Project is soft-deleted and hidden from regular queries.
    /// </summary>
    Deleted = 100,
}
