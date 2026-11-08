using System.Text.RegularExpressions;

namespace UserOrg.AppHost.Tests;

public sealed class ContainerFileAndVolumeStorageTests
{
    private static readonly string[] MachineLocalRoots =
    [
        "SpecialFolder",
        "LocalApplicationData",
        "ApplicationData",
        "UserProfile",
        "GetTempPath",
        "XDG_DATA_HOME",
        "XDG_STATE_HOME",
        "HOME"
    ];

    [Fact]
    public void Should_DeliverTheImmuDbInitScriptWithContainerFiles()
    {
        var container = Root("ImmuDbExtensions.cs").Call("AddContainer", "immudb-audit-log-table");
        var deliveries = container.AppliedCalls("WithContainerFiles");

        Assert.True(
            deliveries.Count == 1,
            $"expected exactly one `WithContainerFiles` on the immudb-audit-log-table container in ImmuDbExtensions.cs, found {deliveries.Count}; the init script is still delivered by a bind mount of the repository tree");

        var arguments = deliveries[0].ArgumentList.Code();

        Assert.Contains("ContainerFile", arguments, StringComparison.Ordinal);
        Assert.Contains("create_audit_log_table.sh", arguments, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_DeliverTheImmuDbInitScriptWhereTheEntrypointRunsIt()
    {
        var container = Root("ImmuDbExtensions.cs").Call("AddContainer", "immudb-audit-log-table");

        var scriptPath = container.AppliedCalls("WithArgs")
            .SelectMany(call => call.ArgumentList.Arguments)
            .Select(argument => AppHostSyntax.StringValue(argument.Expression))
            .FirstOrDefault(value => value is not null && value.EndsWith(".sh", StringComparison.Ordinal));

        Assert.True(
            scriptPath is not null,
            "the immudb-audit-log-table container no longer runs a shell script through WithArgs");

        var separator = scriptPath!.LastIndexOf('/');
        var directory = scriptPath[..separator];
        var fileName = scriptPath[(separator + 1)..];

        var deliveries = container.AppliedCalls("WithContainerFiles")
            .Select(call => call.ArgumentList.Code())
            .ToList();

        Assert.True(
            deliveries.Any(arguments =>
                arguments.Contains(directory, StringComparison.Ordinal)
                && arguments.Contains(fileName, StringComparison.Ordinal)),
            $"nothing the immudb-audit-log-table container receives through WithContainerFiles places `{fileName}` into `{directory}`, which is where its entrypoint runs `{scriptPath}`");
    }

    [Fact]
    public void Should_CarryTheImmuDbInitScriptAsAnEmbeddedResource()
    {
        var project = File
            .ReadAllText(Path.Combine(AppHostSource.Directory, "UserOrg.AppHost.csproj"))
            .Replace('\\', '/');

        Assert.True(
            Regex.IsMatch(project, @"<EmbeddedResource[^>]*Include=""[^""]*create_audit_log_table\.sh"""),
            "UserOrg.AppHost.csproj does not embed externalServices/immudbInit/create_audit_log_table.sh; a container file whose contents are read from the repository tree at run time keeps the very dependency on AppHostDirectory this block removes");

        Assert.False(
            Root("ImmuDbExtensions.cs").Code().MentionsWord("AppHostDirectory"),
            "ImmuDbExtensions.cs still locates the init script through builder.AppHostDirectory");
    }

    [Fact]
    public void Should_NotBindMountAnythingFromImmuDbExtensions()
    {
        var mounts = Root("ImmuDbExtensions.cs").Calls("WithBindMount");

        Assert.True(
            mounts.Count == 0,
            "ImmuDbExtensions.cs still bind-mounts the repository tree into a container: "
                + string.Join("; ", mounts.Select(mount => mount.Describe())));
    }

    [Fact]
    public void Should_StoreTheEmailSenderDatabaseInANamedVolume()
    {
        var container = Root("EmailSenderExtssensions.cs").Call("AddContainer", "Emailsender");
        var volumes = container.AppliedCalls("WithVolume");

        Assert.True(
            volumes.Count == 1,
            $"expected exactly one `WithVolume` on the Emailsender container in EmailSenderExtssensions.cs, found {volumes.Count}; /opt/database is still a bind mount of the repository tree");

        Assert.True(
            volumes[0].ArgumentList.Arguments.Count >= 2,
            "the Emailsender volume is anonymous; an anonymous volume is not reattached after the container is recreated, so the sender's database would not survive a restart");

        Assert.Contains("/opt/database", volumes[0].ArgumentList.Code(), StringComparison.Ordinal);
    }

    [Fact]
    public void Should_NotBindMountAnythingFromEmailSenderExtensions()
    {
        var mounts = Root("EmailSenderExtssensions.cs").Calls("WithBindMount");

        Assert.True(
            mounts.Count == 0,
            "EmailSenderExtssensions.cs still bind-mounts the repository tree into a container: "
                + string.Join("; ", mounts.Select(mount => mount.Describe())));
    }

    [Fact]
    public void Should_StoreTheAuditProxySealingKeysInANamedVolume()
    {
        var container = Root("AuditChainExtensions.cs").Call("AddContainer", "audit-proxy");
        var volumes = container.AppliedCalls("WithVolume");

        Assert.True(
            volumes.Count == 1,
            $"expected exactly one `WithVolume` on the audit-proxy container in AuditChainExtensions.cs, found {volumes.Count}; the sealing key store is still a bind mount of the repository tree");

        Assert.True(
            volumes[0].ArgumentList.Arguments.Count >= 2,
            "the audit-proxy sealing key store is an anonymous volume; it holds the private half of every key ever issued and has to be reattached after the container is recreated");

        Assert.Contains("AuditStateDirectory", volumes[0].ArgumentList.Code(), StringComparison.Ordinal);
    }

    [Fact]
    public void Should_NotBindMountTheAuditProxySealingKeyStore()
    {
        var mounts = Root("AuditChainExtensions.cs").Calls("WithBindMount");

        Assert.True(
            mounts.Count == 0,
            "AuditChainExtensions.cs still bind-mounts a host directory into the audit-proxy container: "
                + string.Join("; ", mounts.Select(mount => mount.Describe())));
    }

    [Fact]
    public void Should_DeriveTheDurableAuditDirectoriesFromAMachineLocalRoot()
    {
        var code = Root("AuditChainExtensions.cs").Code();

        // The app host directory is banned as the root of a durable directory, not as an identity: T-085
        // hashes it into the sealing-key volume's name so two app hosts never share one store, and that
        // hash names no directory at all.
        Assert.DoesNotContain("Path.Combine(builder.AppHostDirectory", code, StringComparison.Ordinal);
        Assert.DoesNotContain("{builder.AppHostDirectory}", code, StringComparison.Ordinal);

        Assert.DoesNotContain("externalServices", code, StringComparison.Ordinal);

        Assert.True(
            MachineLocalRoots.Any(root => code.Contains(root, StringComparison.Ordinal)),
            $"AuditChainExtensions.cs names no machine-local root for the producers' spool and sealing-key cache directories; expected one of {string.Join(", ", MachineLocalRoots)}");
    }

    [Fact]
    public void Should_NotDeclareNormalizeShellScripts()
    {
        var mentions = ResourceCommandSyntax.AllRoots
            .Where(file => file.Value.Code().MentionsWord("NormalizeShellScripts"))
            .Select(file => file.Key)
            .ToList();

        Assert.True(
            mentions.Count == 0,
            $"NormalizeShellScripts still exists in {string.Join(", ", mentions)}; no shell script is bind-mounted any more, so nothing is left for it to normalize");

        var declarations = ResourceCommandSyntax.AllRoots.Values
            .SelectMany(root => root.DescendantNodes())
            .Where(node => node is LocalFunctionStatementSyntax { Identifier.ValueText: "NormalizeShellScripts" }
                or MethodDeclarationSyntax { Identifier.ValueText: "NormalizeShellScripts" })
            .ToList();

        Assert.True(declarations.Count == 0, "NormalizeShellScripts is still declared in the AppHost");
    }

    [Fact]
    public void Should_NotBindMountAnyShellScriptIntoAContainer()
    {
        var resolved = new List<string>();
        var offenders = new List<string>();

        foreach (var (file, call) in ResourceCommandSyntax.CallsEverywhere("WithBindMount"))
        {
            var source = HostPathOf(call);

            if (source is null)
            {
                continue;
            }

            resolved.Add($"{file} -> {source}");

            var scripts = ShellScriptsUnder(source);

            if (scripts.Count > 0)
            {
                offenders.Add($"{file} -> {source} ({string.Join(", ", scripts)})");
            }
        }

        Assert.True(
            resolved.Count > 0,
            "no WithBindMount source in the AppHost resolved to a path that exists on disk, so this test would pass vacuously; the resolution in HostPathOf no longer matches the AppHost's code");

        Assert.True(
            offenders.Count == 0,
            $"a shell script is still bind-mounted into a container, so a container runtime can still rewrite it in the repository tree: {string.Join("; ", offenders)}");
    }

    private static string? HostPathOf(InvocationExpressionSyntax call)
    {
        var segments = call.ArgumentList.Arguments
            .SelectMany(argument => argument.Expression.DescendantNodesAndSelf())
            .Select(node => node as ExpressionSyntax)
            .Where(expression => expression is not null)
            .Select(expression => AppHostSyntax.StringValue(expression!))
            .Where(value => value is not null && !value.StartsWith('/'))
            .Select(value => value!.TrimEnd('/'))
            .Where(value => value.Length > 0)
            .ToList();

        if (segments.Count == 0)
        {
            return null;
        }

        var path = Path.Combine([AppHostSource.Directory, .. segments]);

        return File.Exists(path) || Directory.Exists(path) ? path : null;
    }

    private static IReadOnlyList<string> ShellScriptsUnder(string path)
    {
        if (File.Exists(path))
        {
            return path.EndsWith(".sh", StringComparison.OrdinalIgnoreCase) ? [Path.GetFileName(path)] : [];
        }

        return
        [
            .. Directory
                .EnumerateFiles(path, "*.sh", SearchOption.AllDirectories)
                .Select(script => Path.GetRelativePath(path, script))
        ];
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
