namespace UserOrg.Shared;

/// <summary>
/// Size attribute for check request size
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class SizeAttribute(long size) : Attribute
{
    public long Size { get; } = size;
}