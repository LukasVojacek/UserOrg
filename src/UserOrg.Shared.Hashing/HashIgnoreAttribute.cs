namespace UserOrg.Shared.Hashing;

/// <summary>
/// Marks a property on a <see cref="GenerateIdentityHashAttribute"/> type as excluded from its
/// generated <see cref="IIdentityHashed.ComputeIdentityHash"/> — e.g. audit timestamps or other
/// fields that travel with the type for write purposes but must not affect change detection.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class HashIgnoreAttribute : Attribute;