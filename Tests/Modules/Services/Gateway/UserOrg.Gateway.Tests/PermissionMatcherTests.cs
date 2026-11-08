using Microsoft.Extensions.Logging;

using UserOrg.Gateway.Features.Authorization;

namespace UserOrg.Gateway.Tests;

public sealed class PermissionMatcherTests
{
    private readonly IPermissionMatcher _matcher;

    public PermissionMatcherTests()
    {
        var loggerMock = new Mock<ILogger<PermissionMatcher>>();
        _matcher = new PermissionMatcher(loggerMock.Object);
    }

    [Fact]
    public void HasRequiredPermission_LiteralMatch_ReturnsTrue()
    {
        // Arrange
        var templates = new[] { "perm:admin:read" };
        var userPerms = new HashSet<string> { "perm:admin:read" };

        // Act
        var result = _matcher.HasRequiredPermission(templates, userPerms);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasRequiredPermission_LiteralNoMatch_ReturnsFalse()
    {
        // Arrange
        var templates = new[] { "perm:admin:read" };
        var userPerms = new HashSet<string> { "perm:user:read" };

        // Act
        var result = _matcher.HasRequiredPermission(templates, userPerms);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HasRequiredPermission_TemplateWithPlaceholder_ReturnsTrue()
    {
        // Arrange
        var templates = new[] { "perm:project:{projectName}:read" };
        var userPerms = new HashSet<string> { "perm:project:myproject:read" };

        // Act
        var result = _matcher.HasRequiredPermission(templates, userPerms);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasRequiredPermission_TemplateWithPlaceholder_ReturnsFalse()
    {
        // Arrange
        var templates = new[] { "perm:project:{projectName}:read" };
        var userPerms = new HashSet<string> { "perm:project:myproject:write" };

        // Act
        var result = _matcher.HasRequiredPermission(templates, userPerms);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HasRequiredPermission_MultiplePlaceholders_ReturnsTrue()
    {
        // Arrange
        var templates = new[] { "perm:{resource}:{id}:read" };
        var userPerms = new HashSet<string> { "perm:project:abc123:read" };

        // Act
        var result = _matcher.HasRequiredPermission(templates, userPerms);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasRequiredPermission_PlaceholderValueWithUnderscore_ReturnsTrue()
    {
        // Arrange
        var templates = new[] { "perm:project:{name}:read" };
        var userPerms = new HashSet<string> { "perm:project:my_project:read" };

        // Act
        var result = _matcher.HasRequiredPermission(templates, userPerms);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasRequiredPermission_PlaceholderValueWithNumbers_ReturnsTrue()
    {
        // Arrange
        var templates = new[] { "perm:project:{id}:read" };
        var userPerms = new HashSet<string> { "perm:project:12345:read" };

        // Act
        var result = _matcher.HasRequiredPermission(templates, userPerms);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasRequiredPermission_MultipleTemplates_FirstMatches_ReturnsTrue()
    {
        // Arrange
        var templates = new[] { "perm:admin:read", "perm:project:{name}:read" };
        var userPerms = new HashSet<string> { "perm:admin:read" };

        // Act
        var result = _matcher.HasRequiredPermission(templates, userPerms);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasRequiredPermission_MultipleTemplates_SecondMatches_ReturnsTrue()
    {
        // Arrange
        var templates = new[] { "perm:admin:read", "perm:project:{name}:read" };
        var userPerms = new HashSet<string> { "perm:project:myproject:read" };

        // Act
        var result = _matcher.HasRequiredPermission(templates, userPerms);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasRequiredPermission_EmptyUserPermissions_ReturnsFalse()
    {
        // Arrange
        var templates = new[] { "perm:admin:read" };
        var userPerms = new HashSet<string>();

        // Act
        var result = _matcher.HasRequiredPermission(templates, userPerms);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HasRequiredPermission_EmptyTemplates_ReturnsFalse()
    {
        // Arrange
        var templates = Array.Empty<string>();
        var userPerms = new HashSet<string> { "perm:admin:read" };

        // Act
        var result = _matcher.HasRequiredPermission(templates, userPerms);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HasRequiredPermission_CaseInsensitiveMatch_ReturnsTrue()
    {
        // Arrange
        var templates = new[] { "perm:admin:read" };
        var userPerms = new HashSet<string> { "PERM:ADMIN:READ" };

        // Act
        var result = _matcher.HasRequiredPermission(templates, userPerms);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasRequiredPermission_TemplateCaseInsensitive_ReturnsTrue()
    {
        // Arrange
        var templates = new[] { "perm:project:{name}:READ" };
        var userPerms = new HashSet<string> { "perm:project:myproject:read" };

        // Act
        var result = _matcher.HasRequiredPermission(templates, userPerms);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasRequiredPermission_PlaceholderWithColon_ReturnsFalse()
    {
        // Arrange - placeholder should match [A-Za-z0-9_]+ which doesn't include ':'
        var templates = new[] { "perm:project:{name}:read" };
        var userPerms = new HashSet<string> { "perm:project:my:project:read" };

        // Act
        var result = _matcher.HasRequiredPermission(templates, userPerms);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HasRequiredPermission_PlaceholderWithSpecialChars_ReturnsFalse()
    {
        // Arrange - placeholder should only match alphanumeric + underscore
        var templates = new[] { "perm:project:{name}:read" };
        var userPerms = new HashSet<string> { "perm:project:my-project:read" };

        // Act
        var result = _matcher.HasRequiredPermission(templates, userPerms);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HasRequiredPermission_SameTemplateTwice_UsesCachedRegex_ReturnsTrue()
    {
        // Arrange
        var templates = new[] { "perm:project:{name}:read" };
        var userPerms1 = new HashSet<string> { "perm:project:proj1:read" };
        var userPerms2 = new HashSet<string> { "perm:project:proj2:read" };

        // Act - call twice with same template (should use cached regex)
        var result1 = _matcher.HasRequiredPermission(templates, userPerms1);
        var result2 = _matcher.HasRequiredPermission(templates, userPerms2);

        // Assert
        Assert.True(result1);
        Assert.True(result2);
    }

    [Fact]
    public void HasRequiredPermission_MultipleUserPermissions_MatchesAny_ReturnsTrue()
    {
        // Arrange
        var templates = new[] { "perm:project:{name}:read" };
        var userPerms = new HashSet<string>
        {
            "perm:other:something",
            "perm:project:myproject:read",
            "perm:admin:write"
        };

        // Act
        var result = _matcher.HasRequiredPermission(templates, userPerms);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasRequiredPermission_ExactBoundaryMatch_ReturnsTrue()
    {
        // Arrange - regex should use ^ and $ anchors
        var templates = new[] { "perm:read" };
        var userPerms = new HashSet<string> { "perm:read" };

        // Act
        var result = _matcher.HasRequiredPermission(templates, userPerms);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasRequiredPermission_PartialMatch_ReturnsFalse()
    {
        // Arrange - partial match should not succeed due to anchors
        var templates = new[] { "perm:read" };
        var userPerms = new HashSet<string> { "perm:read:extra" };

        // Act
        var result = _matcher.HasRequiredPermission(templates, userPerms);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HasRequiredPermission_MixedLiteralAndTemplate_TemplateMatches_ReturnsTrue()
    {
        // Arrange
        var templates = new[] { "perm:admin:write", "perm:project:{name}:read" };
        var userPerms = new HashSet<string> { "perm:project:test:read" };

        // Act
        var result = _matcher.HasRequiredPermission(templates, userPerms);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasRequiredPermission_WithRouteValues_SubstitutesPlaceholder_AllowsCorrectProject()
    {
        // Arrange – user has permission for "myproject"; route also points to "myproject"
        var templates = new[] { "perm:project:{ProjectShortName}:read" };
        var userPerms = new HashSet<string> { "perm:project:myproject:read" };
        var routeValues = new Dictionary<string, string> { ["ProjectShortName"] = "myproject" };

        // Act
        var result = _matcher.HasRequiredPermission(templates, userPerms, routeValues);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasRequiredPermission_WithRouteValues_SubstitutesPlaceholder_DeniesWrongProject()
    {
        // Arrange – user has permission for "projectB" but route points to "projectA"
        var templates = new[] { "perm:project:{ProjectShortName}:read" };
        var userPerms = new HashSet<string> { "perm:project:projectB:read" };
        var routeValues = new Dictionary<string, string> { ["ProjectShortName"] = "projectA" };

        // Act
        var result = _matcher.HasRequiredPermission(templates, userPerms, routeValues);

        // Assert – must be denied even though the regex wildcard would have matched
        Assert.False(result);
    }

    [Fact]
    public void HasRequiredPermission_WithNullRouteValues_FallsBackToRegex()
    {
        // Arrange – no route values supplied; existing wildcard behaviour must be preserved
        var templates = new[] { "perm:project:{ProjectShortName}:read" };
        var userPerms = new HashSet<string> { "perm:project:anyproject:read" };

        // Act
        var result = _matcher.HasRequiredPermission(templates, userPerms, null);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasRequiredPermission_WithEmptyRouteValues_FallsBackToRegex()
    {
        // Arrange – empty route values dict; wildcard behaviour must be preserved
        var templates = new[] { "perm:project:{ProjectShortName}:read" };
        var userPerms = new HashSet<string> { "perm:project:anyproject:read" };
        var routeValues = new Dictionary<string, string>();

        // Act
        var result = _matcher.HasRequiredPermission(templates, userPerms, routeValues);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasRequiredPermission_WithRouteValues_PartialSubstitution_RemainingPlaceholderUsesRegex()
    {
        // Arrange – template has two placeholders; only one is in routeValues
        // After substitution: "perm:project:myproject:{action}" → regex for {action}
        var templates = new[] { "perm:project:{ProjectShortName}:{action}" };
        var userPerms = new HashSet<string> { "perm:project:myproject:read" };
        var routeValues = new Dictionary<string, string> { ["ProjectShortName"] = "myproject" };

        // Act
        var result = _matcher.HasRequiredPermission(templates, userPerms, routeValues);

        // Assert – "myproject" is fixed, {action} wildcard matches "read"
        Assert.True(result);
    }

    [Fact]
    public void HasRequiredPermission_WithRouteValues_PartialSubstitution_DeniesWrongProject()
    {
        // Arrange – after substitution the fixed project part forces the check
        var templates = new[] { "perm:project:{ProjectShortName}:{action}" };
        var userPerms = new HashSet<string> { "perm:project:otherproject:read" };
        var routeValues = new Dictionary<string, string> { ["ProjectShortName"] = "myproject" };

        // Act
        var result = _matcher.HasRequiredPermission(templates, userPerms, routeValues);

        // Assert – "otherproject" does not match fixed "myproject" literal segment
        Assert.False(result);
    }

    [Fact]
    public void HasRequiredPermission_WithRouteValues_LiteralTemplate_IgnoresRouteValues()
    {
        // Arrange – template without placeholders; route values should have no effect
        var templates = new[] { "perm:admin:read" };
        var userPerms = new HashSet<string> { "perm:admin:read" };
        var routeValues = new Dictionary<string, string> { ["ProjectShortName"] = "myproject" };

        // Act
        var result = _matcher.HasRequiredPermission(templates, userPerms, routeValues);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasRequiredPermission_WithRouteValues_CaseInsensitivePlaceholderName_Substitutes()
    {
        // Arrange – placeholder name capitalisation in routeValues differs from template
        var templates = new[] { "perm:project:{ProjectShortName}:read" };
        var userPerms = new HashSet<string> { "perm:project:myproject:read" };
        var routeValues = new Dictionary<string, string> { ["projectshortname"] = "myproject" };

        // Act
        var result = _matcher.HasRequiredPermission(templates, userPerms, routeValues);

        // Assert – substitution must be case-insensitive
        Assert.True(result);
    }
}