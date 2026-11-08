namespace ParityTests.Spec;

/// <summary>
/// The dev-auth roles a parity spec can run under. Each value maps 1:1 to a client
/// factory on <c>GatewayAttachFixture</c> (the mapping is wired in B05, not here):
/// <list type="bullet">
///   <item><see cref="Admin"/> → <c>CreateAdminGatewayClient</c></item>
///   <item><see cref="Owner"/> → <c>CreateProjectOwnerClientAsync</c></item>
///   <item><see cref="Manager"/> → <c>CreateProjectManagerClientAsync</c></item>
///   <item><see cref="Member"/> → <c>CreateProjectMemberClientAsync</c></item>
///   <item><see cref="Anonymous"/> → <c>CreateAnonymousGatewayClient</c></item>
/// </list>
/// </summary>
public enum Role
{
    Admin,
    Owner,
    Manager,
    Member,
    Anonymous,
}
