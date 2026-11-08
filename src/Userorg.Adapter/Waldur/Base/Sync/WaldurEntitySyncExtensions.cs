using UserOrganizationManagement.Domain.Entities;

namespace Userorg.Adapter.Waldur.Base.Sync;

internal static class WaldurEntitySyncExtensions
{
    extension(Project target)
    {
        internal void SyncFrom(Project source)
        {
            var changed = false;

            if (target.Name != source.Name)
            {
                target.Name = source.Name;
                changed = true;
            }

            if (target.Description != source.Description)
            {
                target.Description = source.Description;
                changed = true;
            }

            if (target.StartDate != source.StartDate)
            {
                target.StartDate = source.StartDate;
                changed = true;
            }

            if (target.EndDate != source.EndDate)
            {
                target.EndDate = source.EndDate;
                changed = true;
            }

            if (target.IsDeleted != source.IsDeleted)
            {
                target.IsDeleted = source.IsDeleted;
                changed = true;
            }

            if (changed)
            {
                target.ModifiedAt = DateTime.UtcNow;
            }
        }
    }

    extension(ProjectResource target)
    {
        internal void SyncFrom(ProjectResource source)
        {
            var changed = false;

            if (target.Name != source.Name)
            {
                target.Name = source.Name;
                changed = true;
            }

            if (target.Description != source.Description)
            {
                target.Description = source.Description;
                changed = true;
            }

            if (target.StartDate != source.StartDate)
            {
                target.StartDate = source.StartDate;
                changed = true;
            }

            if (target.EndDate != source.EndDate)
            {
                target.EndDate = source.EndDate;
                changed = true;
            }

            if (target.IsDeleted != source.IsDeleted)
            {
                target.IsDeleted = source.IsDeleted;
                changed = true;
            }

            if (changed)
            {
                target.ModifiedAt = DateTime.UtcNow;
            }
        }
    }

    extension(AssignedLocation target)
    {
        internal void SyncFrom(AssignedLocation source,
            AllocationUnitType allocType,
            out List<AssignedLocationValue> newAssignedLocationValues,
            out List<AssignedLocationValue> removedAssignedLocationValues)
        {
            newAssignedLocationValues = [];
            removedAssignedLocationValues = [];
            var changed = false;

            if (target.AllocationUnitCount != source.AllocationUnitCount)
            {
                target.AllocationUnitCount = source.AllocationUnitCount;
                changed = true;
            }

            if (target.Description != source.Description)
            {
                target.Description = source.Description;
                changed = true;
            }

            if (target.AggregationName != source.AggregationName)
            {
                target.AggregationName = source.AggregationName;
                changed = true;
            }

            if (target.IsDeleted != source.IsDeleted)
            {
                target.IsDeleted = source.IsDeleted;
                changed = true;
            }

            if (target.AllocationUnitType?.Id != allocType.Id)
            {
                target.AllocationUnitType = allocType;
                changed = true;
            }

            foreach (var sourceValue in source.AssignedLocationsValues)
            {
                var existing = target.AssignedLocationsValues.FirstOrDefault(f => f.Name == sourceValue.Name);
                if (existing is null)
                {
                    sourceValue.AssignedLocation = target;
                    newAssignedLocationValues.Add(sourceValue);
                    changed = true;
                    continue;
                }

                if (existing.Value != sourceValue.Value)
                {
                    existing.Value = sourceValue.Value;
                    changed = true;
                }
            }

            removedAssignedLocationValues = target.AssignedLocationsValues
                   .ExceptBy(source.AssignedLocationsValues.Select(s => s.Name), t => t.Name)
                   .ToList();

            changed |= removedAssignedLocationValues.Count > 0;

            if (changed)
            {
                target.ModifiedAt = DateTime.UtcNow;
            }
        }
    }

    extension(Provider target)
    {
        internal bool SyncFrom(Provider source, Country country)
        {
            var changed = false;

            if (target.Name != source.Name)
            {
                target.Name = source.Name;
                changed = true;
            }

            if (target.Description != source.Description)
            {
                target.Description = source.Description;
                changed = true;
            }

            if (target.Country?.Id != country.Id)
            {
                target.Country = country;
                changed = true;
            }

            if (target.IsDeleted != source.IsDeleted)
            {
                target.IsDeleted = source.IsDeleted;
                changed = true;
            }

            if (changed)
            {
                target.ModifiedAt = DateTime.UtcNow;
            }

            return changed;
        }
    }

    extension(Location target)
    {
        internal bool SyncFrom(Location source, LocationType locationType)
        {
            var changed = false;

            if (target.Name != source.Name)
            {
                target.Name = source.Name;
                changed = true;
            }

            if (target.Description != source.Description)
            {
                target.Description = source.Description;
                changed = true;
            }

            if (target.IsShared != source.IsShared)
            {
                target.IsShared = source.IsShared;
                changed = true;
            }

            if (target.IsDeleted != source.IsDeleted)
            {
                target.IsDeleted = source.IsDeleted;
                changed = true;
            }

            if (target.LocationType?.Id != locationType.Id)
            {
                target.LocationType = locationType;
                changed = true;
            }

            return changed;
        }
    }

    extension(User target)
    {
        internal void SyncFrom(User source)
        {
            var changed = false;

            if (target.Email != source.Email)
            {
                target.Email = source.Email;
                changed = true;
            }

            if (changed)
            {
                target.ModifiedAt = DateTime.UtcNow;
            }
        }
    }
}