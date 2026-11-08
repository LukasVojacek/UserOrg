namespace UserOrg.Shared.Extensions.Extensions;

/// <summary>
/// Provides extension methods for the Type class to enhance type reflection capabilities.
/// </summary>
/// <remarks>This class contains methods that allow for checking if a type is a subclass of a raw generic type,
/// which can be useful in scenarios involving generic type constraints and type hierarchies.</remarks>
internal static class TypeExtensions
{
    /// <summary>
    /// Provides extension methods for the <see cref="Type"/> class to assist with generic type analysis.
    /// </summary>
    /// <param name="type">The type to analyze for generic inheritance relationships. This parameter represents the current type being
    /// checked.</param>
    extension(Type type)
    {
        /// <summary>
        /// Determines whether the current type is a subclass of the specified generic type definition.
        /// </summary>
        /// <remarks>This method traverses the inheritance chain of the current type, checking each base
        /// type to see if it matches the specified generic type definition. It ignores non-generic types in the
        /// hierarchy.</remarks>
        /// <param name="toCheck">The generic type definition to check against the current type's inheritance hierarchy. This parameter must
        /// represent a generic type definition.</param>
        /// <returns>true if the current type is a subclass of the specified generic type definition; otherwise, false.</returns>
        internal bool IsSubclassOfRawGeneric(Type toCheck)
        {
            while (toCheck != null && toCheck != typeof(object))
            {
                var cur = toCheck.IsGenericType ? toCheck.GetGenericTypeDefinition() : toCheck;
                if (type == cur)
                {
                    return true;
                }
                toCheck = toCheck.BaseType!;
            }
            return false;
        }

    }
}