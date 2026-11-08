using UserOrg.Shared.Persistance.Abstractions.Enums;

namespace UserOrg.Shared.Extensions.Extensions;

public static class IntExtensions
{
    extension(int locationTypeId)
    {
        /// <summary>
        /// Converts the location type identifier to its corresponding <see cref="ELocationType"/> enumeration value.
        /// </summary>
        /// <returns>The <see cref="ELocationType"/> enumeration value corresponding to the location type identifier.</returns>
        /// <exception cref="ArgumentException">Thrown when the location type identifier does not correspond to a defined <see cref="ELocationType"/> value.</exception>
        public ELocationType AsLocationType()
        {
            if (!Enum.IsDefined(typeof(ELocationType), locationTypeId))
            {
                throw new ArgumentException("Invalid location type ID.", nameof(locationTypeId));
            }

            var locationType = (ELocationType)locationTypeId;
            return locationType;
        }
    }
}