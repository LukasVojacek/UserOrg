using Forge.Core.Features.Permission.Services;

namespace Forge.Core.Features.Role.Services;

public sealed record RoleInfo(int Id, string Name, string Description, string Category, IReadOnlyCollection<PermissionInfo> Permissions, bool IsActive, int Level);

public static class RoleInfoExtensions
{
    extension(RoleInfo roleInfo)
    {
        public bool IsHigherLevelThan(RoleInfo other)
        {
            ArgumentNullException.ThrowIfNull(other);
            return roleInfo.Level > other.Level;
        }
    }

    extension(IList<RoleInfo> roleCollection)
    {
        /// <summary>
        /// Gets the role with the lowest level in the specified category.
        /// </summary>
        /// <param name="category">Case insensitive</param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public RoleInfo GetLowestLevelRole(string category)
        {
            ArgumentNullException.ThrowIfNull(roleCollection);
            ArgumentNullException.ThrowIfNullOrEmpty(category);

            if (roleCollection.Count == 0)
            {
                throw new InvalidOperationException("The role collection is empty.");
            }

            if (roleCollection.Count == 1)
            {
                return roleCollection[0];
            }

            var lowest = GetLowest(roleCollection, category);

            if (lowest is null)
            {
                throw new InvalidOperationException("No roles found in the specified category.");
            }

            return lowest;
        }
        /// <summary>
        /// Gets the role with the lowest level from the current role collection.
        /// </summary>
        /// <remarks>Returns null if the role collection is empty. This property is useful for determining
        /// the least privileged or most basic role assigned within the collection.</remarks>
        public RoleInfo? LowestLevel => GetLowest(roleCollection, null);

        private static RoleInfo? GetLowest(IList<RoleInfo> collection, string? category)
        {
            if (collection.Count == 0)
            {
                return null;
            }

            if (collection.Count == 1)
            {
                return collection[0];
            }
            if (string.IsNullOrEmpty(category))
            {
                return collection
                    .OrderBy(role => role.Level)
                    .FirstOrDefault();
            }
            return collection
                .Where(role => role.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
                .OrderBy(role => role.Level)
                .FirstOrDefault();
        }
    }
}