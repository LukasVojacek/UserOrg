using System.Text.RegularExpressions;

namespace UserOrg.AppHost.Tests;

public sealed class AuditAndEmailCompositionCharacterizationTests
{
    [Fact]
    public void Should_KeepTheAuditProxyContainerComposition()
    {
        var proxy = Root("AuditChainExtensions.cs").Call("AddContainer", "audit-proxy");

        foreach (var method in new[]
        {
            "WithIconName",
            "WithHttpEndpoint",
            "WithEnvironment",
            "WithHttpHealthCheck",
            "WaitForCompletion"
        })
        {
            Assert.True(proxy.AppliesTo(method), $"the audit-proxy container lost {method}");
        }

        var code = Root("AuditChainExtensions.cs").Code();

        foreach (var token in new[]
        {
            "auditunsealingproxy",
            "ImmuGwBaseUrl",
            "VaultKeyRing__Mode",
            "VaultKeyRing__Directory",
            "SealingKeyStoreDirectory",
            "/health"
        })
        {
            Assert.Contains(token, code, StringComparison.Ordinal);
        }

        foreach (var token in new[] { "VaultKeyRing__Address", "VaultKeyRing__MountPoint" })
        {
            Assert.True(
                !code.Contains(token, StringComparison.Ordinal),
                $"the audit-proxy still receives {token}; in file mode the ring lives on the proxy's volume and a Vault setting only ties it back to the dev Vault that loses it");
        }

        var mode = proxy.AppliedCalls("WithEnvironment")
            .Where(call => AppHostSyntax.StringValue(call.ArgumentList.Arguments[0].Expression) == "VaultKeyRing__Mode")
            .Select(call => AppHostSyntax.StringValue(call.ArgumentList.Arguments[^1].Expression))
            .ToList();

        Assert.True(
            mode is ["File"],
            $"the audit-proxy key ring mode is [{string.Join(", ", mode)}], not [File]; any other mode keeps the ring in the dev Vault, which loses it on every restart");
    }

    [Fact]
    public void Should_KeepTheAuditProxyStartOrderingEdges()
    {
        var code = Root("AuditChainExtensions.cs").Code();

        Assert.False(
            Regex.IsMatch(code, @"WaitFor\s*\(\s*vaultProxy\s*\)"),
            "the audit-proxy still waits for the Vault agent; its key ring is a file on its own volume, so a Vault restart would hold the whole audit chain for nothing");

        Assert.True(
            Regex.IsMatch(code, @"WaitForCompletion\s*\(\s*auditLogTable\s*\)"),
            "the audit-proxy no longer waits for the audit_log table to be created; it would seal into an archive that has no table to receive the rows");
    }

    [Fact]
    public void Should_KeepTheSealingKeyStoreAsAnAbsoluteContainerPath()
    {
        var value = ConstantValue("SealingKeyStoreDirectory");

        Assert.Equal("/audit/sealing-keys", value);
    }

    /// <summary>
    /// The file key ring and the sealing key store share one volume, so neither outlives the other.
    /// </summary>
    /// <remarks>
    /// The store's private halves are wrapped by the ring. A ring kept anywhere the store is not - the dev
    /// Vault before, a second volume now - can vanish while the store stays, and the proxy's bootstrap guard
    /// then refuses to start.
    /// </remarks>
    [Fact]
    public void Should_KeepTheKeyRingBesideTheStoreOnOneVolume()
    {
        var stateDirectory = ConstantValue("AuditStateDirectory");
        var keyRingDirectory = ConstantValue("KeyRingDirectory");

        Assert.Equal("/audit/key-ring", keyRingDirectory);
        Assert.StartsWith($"{stateDirectory}/", keyRingDirectory, StringComparison.Ordinal);
        Assert.StartsWith($"{stateDirectory}/", ConstantValue("SealingKeyStoreDirectory"), StringComparison.Ordinal);

        var proxyVolumes = Root("AuditChainExtensions.cs").Call("AddContainer", "audit-proxy").AppliedCalls("WithVolume");
        var ownerVolumes = Root("AuditChainExtensions.cs").Call("AddContainer", "audit-proxy-sealing-keys-owner").AppliedCalls("WithVolume");

        Assert.True(proxyVolumes.Count == 1, $"expected one WithVolume on the audit-proxy, found {proxyVolumes.Count}; a second volume lets the ring and the store drift apart");
        Assert.True(ownerVolumes.Count == 1, $"expected one WithVolume on the sealing key store owner, found {ownerVolumes.Count}");

        Assert.True(
            proxyVolumes[0].ArgumentList.Code().MentionsWord("AuditStateDirectory"),
            $"the audit-proxy volume is not mounted at AuditStateDirectory ({proxyVolumes[0].Describe()}); the key ring would sit in the container layer and die with it");

        Assert.Equal(proxyVolumes[0].ArgumentList.Code(), ownerVolumes[0].ArgumentList.Code());
    }

    [Fact]
    public void Should_InjectTheProducerDurableDirectoriesWithEnvironmentOnly()
    {
        var method = ProducerEnvironmentMethod();

        Assert.Contains("IResourceBuilder<ProjectResource>", method.ReturnType.ToString(), StringComparison.Ordinal);

        foreach (var key in new[] { "EncryptedSpool__SpoolDirectory", "OghamSealing__CacheDirectory" })
        {
            Assert.True(
                method.Calls("WithEnvironment", key).Count == 1,
                $"the audit chain no longer injects {key} with WithEnvironment");
        }

        foreach (var storage in new[] { "WithVolume", "WithBindMount", "WithContainerFiles" })
        {
            Assert.True(
                method.Calls(storage).Count == 0,
                $"WithAuditChainEnvironment applies {storage} to a ProjectResource; the producers run as local host processes under `aspire run` and Aspire's container storage APIs attach to container resources only");
        }
    }

    [Fact]
    public void Should_KeepTheAuditChainEnvironmentContract()
    {
        var code = ProducerEnvironmentMethod().Code();

        foreach (var key in new[]
        {
            "EncryptedSpool__SpoolDirectory",
            "EncryptedSpool__EndpointBaseUrl",
            "EncryptedSpool__ModuleName",
            "OghamSealing__KeyEndpoint",
            "OghamSealing__CacheDirectory",
            "ImmuDb__Username",
            "ImmuDb__Password",
            "ImmuDb__Database"
        })
        {
            Assert.Contains(key, code, StringComparison.Ordinal);
        }

        Assert.Contains("/.well-known/ogham-sealing-keys.json", code, StringComparison.Ordinal);
        Assert.True(code.MentionsWord("WaitFor"), "a producer no longer waits for the audit-proxy");
    }

    [Fact]
    public void Should_CreateTheDurableAuditDirectoriesBeforeHandingThemOut()
    {
        Assert.Contains(
            "Directory.CreateDirectory",
            Root("AuditChainExtensions.cs").Code(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Should_KeepTheAuditLogTableInitContainerCommandLine()
    {
        var container = Root("ImmuDbExtensions.cs").Call("AddContainer", "immudb-audit-log-table");
        var chain = container.Chain().Code();

        Assert.Contains("curlimages/curl", chain, StringComparison.Ordinal);
        Assert.Contains("ContainerLifetime.Session", chain, StringComparison.Ordinal);
        Assert.Contains("\"sh\"", chain, StringComparison.Ordinal);
        Assert.Contains("create_audit_log_table.sh", chain, StringComparison.Ordinal);

        foreach (var method in new[]
        {
            "WaitFor",
            "WithEntrypoint",
            "WithArgs",
            "WithParentRelationship"
        })
        {
            Assert.True(container.AppliesTo(method), $"the immudb-audit-log-table container lost {method}");
        }

        foreach (var key in new[] { "IMMUGW_URL", "IMMUDB_USER", "IMMUDB_PASSWORD", "IMMUDB_DATABASE" })
        {
            Assert.True(
                container.AppliedCalls("WithEnvironment").Any(call =>
                    call.ArgumentList.Code().Contains(key, StringComparison.Ordinal)),
                $"the immudb-audit-log-table container no longer receives {key}");
        }
    }

    [Fact]
    public void Should_KeepTheAuditLogTableInitScript()
    {
        var path = Path.Combine(
            AppHostSource.Directory,
            "externalServices",
            "immudbInit",
            "create_audit_log_table.sh");

        Assert.True(File.Exists(path), $"expected the audit_log init script at `{path}`");

        var script = File.ReadAllText(path);

        foreach (var fragment in new[]
        {
            "CREATE TABLE IF NOT EXISTS audit_log",
            "$IMMUGW_URL/login",
            "sqlexec",
            "Authorization:"
        })
        {
            Assert.Contains(fragment, script, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Should_KeepTheEmailSenderContainerComposition()
    {
        var emailsender = Root("EmailSenderExtssensions.cs").Call("AddContainer", "Emailsender");
        var code = Root("EmailSenderExtssensions.cs").Code();

        foreach (var method in new[]
        {
            "WithImageRegistry",
            "WithImageTag",
            "WithLifetime",
            "WithEnvironment",
            "WithHttpEndpoint"
        })
        {
            Assert.True(emailsender.AppliesTo(method), $"the Emailsender container lost {method}");
        }

        foreach (var token in new[]
        {
            "opencode.it4i.eu:5050/lexis-platform/backend",
            "MailSettings__Server",
            "MailSettings__Port",
            "ASPNETCORE_RUNTYPE_ENVIRONMENT",
            "2.4.0",
            "8080"
        })
        {
            Assert.Contains(token, code, StringComparison.Ordinal);
        }

        Assert.True(
            code.MentionsWord("WithParentRelationship"),
            "mailpit is no longer nested under the Emailsender resource");
    }

    [Fact]
    public void Should_KeepTheDeveloperBackupFolderAsABindMount()
    {
        var mounts = Root("ResourceExtensions.cs").Calls("WithBindMount", "dbbackup/");

        Assert.True(
            mounts.Count == 1,
            $"expected exactly one `WithBindMount(\"dbbackup/\", …)` in ResourceExtensions.cs, found {mounts.Count}; the restore command reads a .bak a developer drops into a repository folder, and a named volume exposes no host path they could copy into");

        Assert.Contains("/var/opt/mssql/backup/", Root("ResourceExtensions.cs").Code(), StringComparison.Ordinal);
    }

    private static MethodDeclarationSyntax ProducerEnvironmentMethod()
    {
        var methods = Root("AuditChainExtensions.cs")
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Where(method => method.Identifier.ValueText == "WithAuditChainEnvironment")
            .ToList();

        Assert.True(
            methods.Count == 1,
            $"expected exactly one WithAuditChainEnvironment declaration, found {methods.Count}");

        return methods[0];
    }

    private static string? ConstantValue(string name)
    {
        var constant = Root("AuditChainExtensions.cs")
            .DescendantNodes()
            .OfType<FieldDeclarationSyntax>()
            .SelectMany(field => field.Declaration.Variables)
            .SingleOrDefault(variable => variable.Identifier.ValueText == name);

        var initializer = constant?.Initializer;

        Assert.True(initializer is not null, $"AuditChainExtensions.cs no longer declares {name}");

        return AppHostSyntax.StringValue(initializer.Value);
    }

    private static CompilationUnitSyntax Root(string fileName)
    {
        var key = Path.Combine("src", "UserOrg.AppHost", fileName);

        Assert.True(
            ResourceCommandSyntax.AllRoots.ContainsKey(key),
            $"expected AppHost source file `{key}` to exist");

        return ResourceCommandSyntax.AllRoots[key];
    }

    /// <summary>
    /// The sealing key store owner only creates the audit state directories and hands them to the proxy user.
    /// </summary>
    /// <remarks>
    /// With the ring on the same volume as the store, nothing the owner could learn at start makes a key
    /// unopenable, so there is nothing to decide in a start callback and nothing to clear.
    /// </remarks>
    [Fact]
    public void Should_OnlyHandTheAuditStateToTheProxyUser()
    {
        var owner = Root("AuditChainExtensions.cs").Call("AddContainer", "audit-proxy-sealing-keys-owner");

        var args = owner.Chain().Calls("WithArgs");

        Assert.True(args.Count == 1, $"expected one WithArgs on the sealing key store owner, found {args.Count}");

        Assert.False(
            args[0].DescendantNodes().OfType<AnonymousFunctionExpressionSyntax>().Any(),
            $"the sealing key store owner still decides its command line in a start callback ({args[0].Describe()}); the file ring needs no answer from Vault");

        var code = args[0].Code();

        foreach (var fragment in new[] { "mkdir -p", "chown -R", "chmod 0700" })
        {
            Assert.Contains(fragment, code, StringComparison.Ordinal);
        }

        foreach (var directory in new[] { "AuditStateDirectory", "KeyRingDirectory", "SealingKeyStoreDirectory" })
        {
            Assert.True(code.MentionsWord(directory), $"the sealing key store owner never touches {directory}; the proxy user could not write there");
        }

        Assert.DoesNotContain("rm -rf", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// The file-ring producers start from directories the Vault-ring era never wrote.
    /// </summary>
    /// <remarks>
    /// A producer cache or spool from the Vault-ring era holds keys and envelopes for a ring the file-mode
    /// proxy has never had. A fixed generation segment in the run-mode path leaves them behind instead of
    /// emptying them; a published producer has no such history, so its path stays as it was.
    /// </remarks>
    [Fact]
    public void Should_StartTheFileRingProducerDirectoriesEmpty()
    {
        Assert.Equal("file-ring", ConstantValue("ProducerStateGeneration"));
        Assert.Equal("/var/lib/userorg", ConstantValue("ContainerStateRoot"));

        var method = Root("AuditChainExtensions.cs")
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(declaration => declaration.Identifier.ValueText == "ProducerDirectory");

        var generations = method.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Where(identifier => identifier.Identifier.ValueText == "ProducerStateGeneration")
            .ToList();

        Assert.True(generations.Count == 1, $"expected ProducerDirectory to mention ProducerStateGeneration once, found {generations.Count}");

        Assert.True(
            AppHostSyntax.IsInsideRunModeBranch(generations[0], method),
            $"ProducerStateGeneration is outside the run-mode branch of ProducerDirectory ({generations[0].Describe()}); a published producer would move off its volume path");
    }

    /// <summary>
    /// A producer's key cache and spool live under the same app host key as the proxy's store.
    /// </summary>
    /// <remarks>
    /// Unkeyed, two clones or a clone and a worktree share one producer cache, and a store reset in one of
    /// them leaves the other producer sealing to a key no live proxy holds.
    /// </remarks>
    [Fact]
    public void Should_KeyTheProducerDirectoriesByTheAppHostKey()
    {
        foreach (var (name, suffix) in new[] { ("ProducerKeyCacheDirectory", "-sealing-keys"), ("ProducerSpoolDirectory", "-spool") })
        {
            var methods = Root("AuditChainExtensions.cs")
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(method => method.Identifier.ValueText == name)
                .ToList();

            Assert.True(methods.Count == 1, $"expected exactly one {name} declaration in AuditChainExtensions.cs, found {methods.Count}");

            Assert.Contains(suffix, methods[0].Code(), StringComparison.Ordinal);

            Assert.True(
                ProducerEnvironmentMethod().Calls(name).Count == 1,
                $"WithAuditChainEnvironment no longer takes its directory from {name}; the producer would keep its state outside the per-app-host file-ring directory the runbook tells a developer to clear");
        }

        var unkeyed = Root("AuditChainExtensions.cs")
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Where(method => method.Code().MentionsWord("LocalApplicationData") && !method.Code().MentionsWord("AppHostKey"))
            .Select(method => method.Identifier.ValueText)
            .ToList();

        Assert.True(unkeyed.Count == 0, $"these methods still build a LocalApplicationData path without the AppHostKey: {string.Join(", ", unkeyed)}");

        Assert.True(
            Root("AuditChainExtensions.cs")
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Any(method => method.Code().MentionsWord("LocalApplicationData") && method.Code().MentionsWord("AppHostKey")),
            "no method in AuditChainExtensions.cs roots a run-mode producer directory in LocalApplicationData under the AppHostKey");
    }

    /// <summary>
    /// immugw is restarted by the container runtime after a failed start, a bounded number of times.
    /// </summary>
    [Fact]
    public void Should_RestartImmuGwOnFailureAtMostFiveTimes()
    {
        var immuGw = Root("ImmuDbExtensions.cs").Call("AddContainer", "immugw");

        var runtimeArgs = immuGw.Chain().Calls("WithContainerRuntimeArgs")
            .Select(call => string.Join(' ', call.ArgumentList.Arguments.Select(argument => AppHostSyntax.StringValue(argument.Expression))).Replace('=', ' '))
            .ToList();

        Assert.True(
            runtimeArgs.Any(args => args.Contains("--restart on-failure:5", StringComparison.Ordinal)),
            $"immugw carries no `--restart on-failure:5`; a Persistent immugw that failed its start stays down and every audit delivery behind it stalls (runtime args: [{string.Join("; ", runtimeArgs)}])");
    }

    /// <summary>
    /// immugw starts only once immudb answers its readiness probe, not merely once its container runs.
    /// </summary>
    /// <remarks>
    /// A running immudb has not opened its gRPC listener yet; immugw's first dial then fails and the
    /// bounded restarts can run out before immudb is ready. immudb serves /readyz on its metrics port.
    /// </remarks>
    [Fact]
    public void Should_StartImmuGwOnlyOnceImmuDbIsReady()
    {
        var root = Root("ImmuDbExtensions.cs");
        var immudb = root.Call("AddContainer", "immudb").Chain();
        var immuGw = root.Call("AddContainer", "immugw").Chain();

        var healthChecks = immudb.Calls("WithHttpHealthCheck")
            .Select(call => call.ArgumentList.Arguments.Select(argument => AppHostSyntax.StringValue(argument.Expression)).ToList())
            .ToList();
        var metricsEndpoints = immudb.Calls("WithHttpEndpoint")
            .Where(call => call.ArgumentList.Arguments.Any(argument => AppHostSyntax.StringValue(argument.Expression) == "metrics"))
            .ToList();

        Assert.True(
            healthChecks.Any(arguments => arguments.Contains("/readyz") && arguments.Contains("metrics")),
            $"immudb has no WithHttpHealthCheck(\"/readyz\", ..., \"metrics\"); WaitFor(immudb) then waits for a running container, not a ready database (health checks: [{string.Join("; ", healthChecks.Select(arguments => string.Join(' ', arguments)))}])");
        Assert.True(
            metricsEndpoints.Count == 1,
            $"immudb declares {metricsEndpoints.Count} HTTP endpoints named \"metrics\"; the readiness probe needs exactly one (immudb serves /readyz on its metrics port 9497)");
        Assert.True(
            immuGw.Calls("WaitFor").Count == 1,
            "immugw no longer waits for immudb; it would dial a database that is not there yet");
    }

    /// <summary>
    /// A local run gets the HEAppE fake without asking for it, and a publish never does.
    /// </summary>
    /// <remarks>
    /// Without the fake the seed writes its own non-mock endpoint into AssignedLocationValue HEAPPE_URL.
    /// That host does not resolve, the liveness probe against `heappe/Health` fails, and every
    /// HEAppE-backed path answers 503 (ADR-0074) - TemplatesInfo included - so nothing over the seeded
    /// projects can be exercised. Off is still reachable with --Parameters:UseHeappeMock=false.
    /// </remarks>
    [Fact]
    public void Should_RunTheHeappeMockByDefaultUnderALocalRun()
    {
        var gate = AppHostSource.Program.Call("GetValue", "Parameters:UseHeappeMock");
        var code = gate.ArgumentList.Code();

        Assert.Contains("IsRunMode", code, StringComparison.Ordinal);
        Assert.DoesNotContain("defaultValue: false", code, StringComparison.Ordinal);
    }
}
