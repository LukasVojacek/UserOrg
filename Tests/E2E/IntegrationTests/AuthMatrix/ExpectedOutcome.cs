namespace IntegrationTests.AuthMatrix;

public enum ExpectedOutcome
{
    Allowed,
    Forbidden403,
    Unauthorized401,

    /// <summary>
    /// The gateway's own answer for a path no published API route serves (ADR-0106): a 404 carrying
    /// <see cref="GatewayRouteNotFound.Code"/>, told apart from an upstream 404 for a missing child.
    /// </summary>
    NotFound404
}