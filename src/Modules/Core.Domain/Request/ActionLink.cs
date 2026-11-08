namespace Core.Domain.Request;

/// <summary>
/// ActionLink — child entity of the Request aggregate (accessed only via <see cref="Request.ActionLinks"/>).
/// Id, RequestId, CreatedAt and ModifiedAt are EF shadow properties.
/// </summary>
public sealed class ActionLink : BaseAuditableEntity, ISoftDeletableEntity
{
    public int Action { get; private set; }
    public string? Link { get; private set; }
    public DateTimeOffset ValidTo { get; private set; }
    public bool IsDeleted { get; private set; }

    private ActionLink() { }

    internal static ActionLink Create(int action, string? link, DateTimeOffset validTo)
    {
        return new ActionLink
        {
            Action = action,
            Link = link,
            ValidTo = validTo,
            IsDeleted = false,
        };
    }

    public void UpdateLink(string? link, DateTimeOffset validTo)
    {
        Link = link;
        ValidTo = validTo;
    }

    public void Delete() => IsDeleted = true;
}