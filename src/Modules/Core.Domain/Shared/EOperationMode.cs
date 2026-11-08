namespace Core.Domain.Shared;

public enum EOperationMode
{
    /// <summary>
    /// Indicates that the value is unspecified or has not been specified.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// Specifies that the resource is accessible in read-only mode.
    /// </summary>
    ReadOnly = 1,

    /// <summary>
    /// Indicates that the item can be modified by the user.
    /// </summary>
    Editable = 2,
}