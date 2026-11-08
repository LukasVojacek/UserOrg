using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace UserOrg.Shared.Persistance;

/// <summary>
/// Generates a UUID v7 for a Guid-backed primary key at <c>Add</c> time (before the entity enters the
/// change-tracker identity map), replacing the dropped DB <c>NewId()</c> default (ADR-0049). The value is
/// permanent (<see cref="GeneratesTemporaryValues"/> = <c>false</c>) and sent on INSERT, so no store default
/// is needed and the migration stays zero-diff. Works for shadow <see cref="Guid"/> keys and value-object
/// keys alike: when the key property has a converter the generated Guid is mapped back to the model type
/// through it (e.g. <c>ProviderId.From(guid)</c>), otherwise the raw Guid is returned.
/// </summary>
internal sealed class UuidV7ValueGenerator(ValueConverter? converter) : ValueGenerator
{
    public override bool GeneratesTemporaryValues => false;

    protected override object NextValue(EntityEntry entry)
    {
        var value = Guid.CreateVersion7();
        return converter is null ? value : converter.ConvertFromProvider(value)!;
    }
}