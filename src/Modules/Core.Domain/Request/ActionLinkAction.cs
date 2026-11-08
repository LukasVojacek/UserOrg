namespace Core.Domain.Request;

/// <summary>
/// Action encoded on an <see cref="ActionLink"/> (the <c>Action</c> column). Values match the legacy
/// schema so links minted by either stack are interchangeable: 1 = Accept, 2 = Reject.
/// </summary>
public static class ActionLinkAction
{
    public const int Accept = 1;
    public const int Reject = 2;
}