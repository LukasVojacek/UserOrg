using System.Text.Json;

namespace Core.Domain.User;

/// <summary>
/// UserPreference - Child entity of User aggregate.
///
/// DESIGN NOTE (DDD): CreatedAt and ModifiedAt are managed as EF shadow properties.
/// The User navigation property is internal only and not exposed to domain code.
/// </summary>
public class UserPreference : ISoftDeletableEntity
{
    public UserId UserId { get; private set; }
    public string Data { get; private set; } = "{}";
    public bool IsDeleted { get; private set; }

    public static UserPreference Create(UserId userId, JsonDocument userpreferenceJson)
    {
        ArgumentNullException.ThrowIfNull(userpreferenceJson);

        return new UserPreference
        {
            UserId = userId,
            // RootElement.GetRawText() yields the JSON text; JsonDocument.ToString() would yield the type name.
            Data = userpreferenceJson.RootElement.GetRawText(),
            IsDeleted = false,
        };
    }

    public void UpdateData(string jsonData)
    {
        ArgumentNullException.ThrowIfNull(jsonData);
        Data = jsonData;
    }
}