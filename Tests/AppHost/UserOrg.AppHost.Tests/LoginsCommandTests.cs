namespace UserOrg.AppHost.Tests;

/// <summary>
/// The Keycloak resource carries a highlighted command that lists every seeded login with its password and the
/// realm administrator, so a developer can sign in by hand (ADR 0102: highlighted, on the resource it reads).
/// </summary>
public sealed class LoginsCommandTests
{
    [Fact]
    public void ShouldListTheSeededLoginsAndTheAdminFromAHighlightedKeycloakCommand()
    {
        var keycloak = AppHostSource.RootOf("KeycloakExtensions.cs");
        var command = keycloak.Method("WithLoginsCommand");
        var table = keycloak.Method("LoginsTable").Code();

        Assert.True(
            command.Code().Contains("IsHighlighted = true", StringComparison.Ordinal),
            $"the logins command is no longer highlighted: {command.Describe()}");
        Assert.True(
            command.Code().MentionsWord("HealthStatus"),
            $"the logins command no longer waits for a healthy Keycloak: {command.Describe()}");
        Assert.True(
            table.MentionsWord("SeededLogins") && table.MentionsWord("SeededPassword") && table.MentionsWord("AdminRole"),
            "the logins table no longer lists every seeded login, its password and the realm administrator");

        var call = AppHostSource.Program.Call("WithLoginsCommand");

        Assert.Equal("keycloak", call.Receiver());
    }
}
