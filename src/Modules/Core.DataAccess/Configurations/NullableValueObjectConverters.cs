using Core.Domain.Shared;

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Core.DataAccess.Configurations;

/// <summary>
/// Maps a nullable <see cref="Email"/> value object to a nullable string column. "Not set" is null end
/// to end — no sentinel, no <c>ConvertsNulls</c> (EF passes null through natively, selecting the
/// null-tolerant reader from the nullable property). See ADR-0010.
/// </summary>
internal sealed class EmailNullableConverter : ValueConverter<Email?, string?>
{
    public EmailNullableConverter()
        : base(
            email => email.HasValue && !string.IsNullOrEmpty(email.Value.Value) ? email.Value.Value : null,
            value => string.IsNullOrEmpty(value) ? (Email?)null : Email.Create(value))
    {
    }
}

/// <summary>
/// Maps a nullable <see cref="Description"/> value object to a nullable string column. "Not set" is null
/// end to end — no sentinel, no <c>ConvertsNulls</c>. See ADR-0010.
/// </summary>
internal sealed class DescriptionNullableConverter : ValueConverter<Description?, string?>
{
    public DescriptionNullableConverter()
        : base(
            d => d.HasValue && !string.IsNullOrEmpty(d.Value.Value) ? d.Value.Value : null,
            value => string.IsNullOrEmpty(value) ? (Description?)null : Description.Create(value))
    {
    }
}