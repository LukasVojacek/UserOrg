namespace UserOrg.AppHost.Tests;

/// <summary>
/// Guards `aspire publish`. Every fact here was a way the Docker Compose publisher failed before T-085:
/// it crashed on a reference to a resource the manifest did not contain, it hung forever on a resource
/// state no publish ever produces, and it wrote a developer's home directory into a container's
/// environment. None of them is visible from `aspire run`, which is why they survived so long.
/// </summary>
public sealed class ComposePublishTests
{
    /// <summary>Resources deliberately kept out of a published topology, with the reason.</summary>
    private static readonly Dictionary<string, string> ExcludedOnPurpose = new(StringComparer.Ordinal)
    {
        ["Idp"] = "a published topology uses the external identity provider",
        ["db-migrations-script"] = "a developer tool that writes a .sql file",
        ["db-seed-e2e"] = "test data",
        ["k6"] = "a load-test driver, and the only bind mount of the repository tree",
        ["heappe-mock"] = "a fake standing in for a real HEAppE",
        ["rabbitMqStomp"] = "dead since ADR-0056"
    };

    /// <summary>
    /// The three orchestrators run after <c>Build()</c> and start resources through the resource command
    /// service, which waits on states only a run publishes. Under `aspire publish` that wait never
    /// returns, so the AppHost never exits and the publish hangs with no error at all.
    /// </summary>
    [Theory]
    [InlineData("DatabaseSourceSelection.cs")]
    [InlineData("IdentityProviderSelection.cs")]
    [InlineData("VaultSetup.cs")]
    public void Should_ResolveWithoutStartingAnythingUnderPublish(string file)
    {
        var applyTo = Root(file).DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .SingleOrDefault(method => method.Identifier.ValueText == "ApplyTo");

        Assert.True(applyTo is not null, $"{file} no longer declares ApplyTo");

        var code = applyTo!.Code();

        Assert.True(
            code.Contains("IsPublishMode", StringComparison.Ordinal),
            $"{file}'s ApplyTo does not branch on IsPublishMode; under `aspire publish` it will wait on a resource state nothing ever publishes and the publish will hang rather than fail");

        var holdsAnAnswer = Root(file).Code().Contains("TaskCompletionSource", StringComparison.Ordinal);

        Assert.True(
            !holdsAnAnswer || code.Contains("TrySetResult", StringComparison.Ordinal),
            $"{file}'s ApplyTo takes a publish-mode branch without completing its answer; the environment callbacks the publisher runs are waiting on it and would deadlock instead");
    }

    /// <summary>
    /// A published producer is a container on somebody else's host. A path under the publishing
    /// developer's home names nothing there, and the call that built it also created that directory on
    /// the machine that ran the publish.
    /// </summary>
    [Fact]
    public void Should_NotRootAPublishedProducersDurableStateInADeveloperHomeDirectory()
    {
        var durable = Root("AuditChainExtensions.cs").DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .SingleOrDefault(method => method.Identifier.ValueText == "DurableDirectory");

        Assert.True(durable is not null, "AuditChainExtensions.cs no longer declares DurableDirectory");

        var code = durable!.Code();

        Assert.True(
            code.Contains("IsPublishMode", StringComparison.Ordinal),
            "DurableDirectory answers the same path in both modes; a published producer would be told to spool sealed audit envelopes into a directory on the machine that ran `aspire publish`");

        Assert.True(
            code.Contains("ContainerMountType.Volume", StringComparison.Ordinal),
            "the published producer's durable directory has no volume behind it, so every recreation of the container loses the spooled envelopes and the cached sealing keys");
    }

    /// <summary>
    /// Docker Compose interpolates <c>$</c> inside a <c>configs</c> entry's content. An unescaped
    /// <c>${env:…}</c> is rejected as an invalid interpolation and the compose file will not load; an
    /// unescaped <c>$VAR</c> in a shell script is replaced with an empty string and the script runs with
    /// every variable blank, which fails nothing and does nothing.
    /// </summary>
    [Fact]
    public void Should_EscapeEveryDeliveredContainerFile()
    {
        var deliveries = ResourceCommandSyntax.CallsEverywhere("WithContainerFiles");

        Assert.True(deliveries.Count > 0, "no WithContainerFiles call was found, so this test is vacuous");

        var unescaped = deliveries
            .Where(delivery => !delivery.Call.ArgumentList.Code()
                .Contains("AsContainerFileContents", StringComparison.Ordinal))
            .Select(delivery => delivery.File)
            .ToList();

        Assert.True(
            unescaped.Count == 0,
            $"a container file's contents reach the compose publisher unescaped, so docker compose will interpolate the `$` in them: {string.Join(", ", unescaped)}");
    }

    /// <summary>
    /// The failure that produced no compose file at all: a published resource holding an endpoint
    /// reference to an excluded one makes the publisher look up a service that does not exist, and a
    /// `WaitFor` across the same boundary writes a `depends_on` docker refuses to load.
    /// </summary>
    [Fact]
    public void Should_KeepEveryInfrastructureResourceAProducerNeedsInTheManifest()
    {
        string[] required =
        [
            "immudb", "immugw", "immudb-audit-log-table", "Vault", "VaultProxy",
            "audit-proxy", "audit-proxy-sealing-keys-owner", "Emailsender", "mailpit",
            "otel-collector", "userorgDatabase", "db-migrations-apply"
        ];

        var excluded = required.Where(IsExcludedFromManifest).ToList();

        Assert.True(
            excluded.Count == 0,
            $"`userorg`, `gateway` or `audit-proxy` reference these resources, so a published compose file names them in depends_on and in resolved endpoints; excluded, the publish either crashes or emits a compose file docker will not load: {string.Join(", ", excluded)}");
    }

    [Fact]
    public void Should_KeepDeveloperOnlyResourcesOutOfTheManifest()
    {
        var published = ExcludedOnPurpose
            .Where(entry => !IsExcludedFromManifest(entry.Key))
            .Select(entry => $"{entry.Key} ({entry.Value})")
            .ToList();

        Assert.True(
            published.Count == 0,
            $"these resources reach a published manifest but should not: {string.Join("; ", published)}");
    }

    private static bool IsExcludedFromManifest(string resourceName)
    {
        foreach (var (_, root) in ResourceCommandSyntax.AllRoots)
        {
            // Both spellings: most registrations pass the name as a literal, a few pass a constant that
            // holds it, and a lookup that only knew the first would silently find nothing and pass.
            var byConstant = root.DescendantNodes()
                .OfType<FieldDeclarationSyntax>()
                .SelectMany(field => field.Declaration.Variables)
                .Where(variable => variable.Initializer is not null
                    && AppHostSyntax.StringValue(variable.Initializer.Value) == resourceName)
                .Select(variable => variable.Identifier.ValueText)
                .ToList();

            var registration = root.Calls("AddContainer", resourceName)
                .Concat(root.Calls("AddProject", resourceName))
                .Concat(root.Calls("AddSqlServer", resourceName))
                .Concat(root.Calls("AddKeycloak", resourceName))
                .Concat(root.Calls("AddMailPit", resourceName))
                .Concat(root.Calls("AddWireMock", resourceName))
                .Concat(root.Calls("AddK6", resourceName))
                .Concat(root.Calls("AddDockerfile", resourceName))
                .Concat(byConstant.SelectMany(name => root.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Where(call => AppHostSyntax.NameOf(call)?.StartsWith("Add", StringComparison.Ordinal) == true)
                    .Where(call => call.ArgumentList.Arguments
                        .Any(argument => argument.Expression is IdentifierNameSyntax identifier
                            && identifier.Identifier.ValueText == name))))
                .FirstOrDefault();

            if (registration is null)
            {
                continue;
            }

            return registration.AppliesToSameResource(root, "ExcludeFromManifest");
        }

        Assert.Fail($"no registration was found for resource `{resourceName}`");
        return false;
    }

    private static CompilationUnitSyntax Root(string fileName)
    {
        var key = Path.Combine("src", "UserOrg.AppHost", fileName);

        Assert.True(
            ResourceCommandSyntax.AllRoots.ContainsKey(key),
            $"expected AppHost source file `{key}` to exist");

        return ResourceCommandSyntax.AllRoots[key];
    }
}
