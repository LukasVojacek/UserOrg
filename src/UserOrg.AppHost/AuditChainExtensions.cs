using System.Security.Cryptography;
using System.Text;

namespace UserOrg.AppHost;

internal static class AuditChainExtensions
{
    // The proxy no longer lives in this repository; it is developed and released from
    // opencode.it4i.eu/lexis-platform/backend/credential-service/audit/auditproxy/auditunsealingproxy and consumed
    // here as a published image. Only a semantic version is ever pulled - the archive is append-only, so
    // which build unsealed a record has to stay answerable after the fact, which a moving tag cannot do.
    private const string ProxyImage = "opencode.it4i.eu:5050/lexis-platform/backend/credential-service/audit/auditproxy/auditunsealingproxy";
    private const string ProxyTag = "0.3.0";

    // The uid the proxy image runs as. Docker seeds a fresh named volume from the image's directory,
    // which is root-owned, so the store arrives unwritable for this user and the sealing key can never
    // be created; the init container below hands the directory over before the proxy starts.
    private const string ProxyUserId = "1654";

    // Inside the container, not on this machine: the sealing key store and the key ring live in one named
    // volume, and all three must be absolute container paths or they resolve against the container's
    // working directory and a healthy proxy writes into a layer that dies with it.
    private const string AuditStateDirectory = "/audit";
    private const string SealingKeyStoreDirectory = "/audit/sealing-keys";
    private const string KeyRingDirectory = "/audit/key-ring";

    // A layout segment, not the warm-cache guard: it separates the directories of an earlier, Vault-ring
    // run from the file ring's, so those producers start empty and the orphans are removed by hand
    // (docs/runbooks/audit-chain-local-recovery.md). A stale key cache is handled by Ogham.Sealing 0.3.0
    // and the proxy's 422 (ADR-0116), not by this name.
    private const string ProducerStateGeneration = "file-ring";

    /// <summary>
    /// A short, stable key for this app host, derived from its directory the way the container runtime
    /// names tell the app hosts on a machine apart. Two clones — or a clone and a worktree — are two app
    /// hosts and must not share proxy state or producer directories: each app host's store, key ring and
    /// spooled envelopes belong together and make no sense mixed with another's.
    /// </summary>
    private static string AppHostKey(IDistributedApplicationBuilder builder) =>
        Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(builder.AppHostDirectory)))[..8];

    /// <summary>
    /// The unsealing half of the audit chain. It has to exist, hold a usable key and publish its key
    /// document before any producer starts sealing against it - a producer that seals first fills its
    /// spool with envelopes nothing can open yet.
    /// </summary>
    internal static IResourceBuilder<ContainerResource> AddAuditUnsealingProxy(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<ContainerResource> immuGw,
        IResourceBuilder<ContainerResource> auditLogTable)
    {
        // The store holds private key halves wrapped by a Data Protection key ring, and the ring lives
        // beside it in the same volume (file mode, ADR-0097), so the two can never get out of step the way
        // a ring in the dev Vault did. The name carries the app host's identity so two clones or a clone
        // and a worktree, which each keep their own state, never share one.
        var auditStateVolume = $"audit-proxy-state-{AppHostKey(builder)}";

        // Runs as root only to hand the volume to the proxy's own uid, then exits. Without it the
        // proxy logs UnauthorizedAccessException on the key file, its unsealing health check stays 503
        // for the life of the run, and every producer waiting on the proxy never starts.
        // 0700: both directories hold key material and only the proxy ever reads them.
        var auditStateOwner = builder.AddContainer("audit-proxy-sealing-keys-owner", "busybox", "1.37.0")
            .WithLifetime(ContainerLifetime.Session)
            .WithVolume(auditStateVolume, AuditStateDirectory)
            .WithEntrypoint("sh")
            .WithArgs(
                "-c",
                $"mkdir -p {SealingKeyStoreDirectory} {KeyRingDirectory} && chown -R {ProxyUserId}:{ProxyUserId} {AuditStateDirectory} && chmod 0700 {SealingKeyStoreDirectory} {KeyRingDirectory}");

        return builder.AddContainer("audit-proxy", ProxyImage, ProxyTag)
            .WithIconName("LockClosed")
            // Plain HTTP, unlike the HTTPS the deployed topology owes it: ADR-0096 decision 4 wants the
            // transport standing between an on-path attacker and the key document, and under `aspire run`
            // every producer reaches this port over loopback, where there is no on-path attacker to stand
            // against. Terminating TLS here instead would mean a certificate inside an image this
            // repository does not build.
            .WithHttpEndpoint(targetPort: 8080)
            .WithUrlForEndpoint("http", static url =>
            {
                url.DisplayText = "🔏 Audit unsealing proxy";
                url.DisplayLocation = UrlDisplayLocation.DetailsOnly;
            })
            .WithEnvironment("ImmuGwBaseUrl", immuGw.GetEndpoint("http"))
            // The file ring needs no Vault, so a recreated dev Vault leaves the proxy's keys alone.
            .WithEnvironment("VaultKeyRing__Mode", "File")
            .WithEnvironment("VaultKeyRing__Directory", KeyRingDirectory)
            .WithEnvironment("SealingKeyStoreDirectory", SealingKeyStoreDirectory)
            // The store holds the private half of every key ever issued, so it outlives the container.
            // A named volume, not a host directory, so the store survives a rebuild of the image.
            .WithVolume(auditStateVolume, AuditStateDirectory)
            .WithHttpHealthCheck("/health")
            .WaitForCompletion(auditLogTable)
            .WaitForCompletion(auditStateOwner);
    }

    /// <summary>
    /// Everything a producer needs to seal audit events into the archive. Every value is mandatory:
    /// a host missing one of them refuses to start rather than serve traffic it cannot audit.
    /// </summary>
    internal static IResourceBuilder<ProjectResource> WithAuditChainEnvironment(
        this IResourceBuilder<ProjectResource> producer,
        IResourceBuilder<ContainerResource> auditProxy,
        string moduleName)
    {
        var proxy = auditProxy.GetEndpoint("http");

        var spool = DurableDirectory(producer, $"{moduleName}-spool", ProducerSpoolDirectory(producer.ApplicationBuilder, moduleName));
        var sealingKeys = DurableDirectory(producer, $"{moduleName}-sealing-keys", ProducerKeyCacheDirectory(producer.ApplicationBuilder, moduleName));

        return producer
            .WithEnvironment("EncryptedSpool__SpoolDirectory", spool)
            .WithEnvironment("EncryptedSpool__EndpointBaseUrl", proxy)
            .WithEnvironment("EncryptedSpool__ModuleName", moduleName)
            .WithEnvironment("OghamSealing__KeyEndpoint", ReferenceExpression.Create($"{proxy}/.well-known/ogham-sealing-keys.json"))
            .WithEnvironment("OghamSealing__CacheDirectory", sealingKeys)
            .WithEnvironment("ImmuDb__Username", ImmuDbExtensions.AuditUser)
            .WithEnvironment("ImmuDb__Password", ImmuDbExtensions.AuditPassword)
            .WithEnvironment("ImmuDb__Database", ImmuDbExtensions.AuditDatabase)
            .WaitFor(auditProxy);
    }

    /// <summary>
    /// The directory a producer spools sealed envelopes and cached sealing keys into, and the storage
    /// behind it. Both hold data that must survive a restart, so neither is ever a temporary directory.
    /// </summary>
    /// <remarks>
    /// The two modes need different answers and the wrong one is silently wrong. Under <c>aspire run</c> a
    /// producer is a local process, so the directory is one on this machine, rooted outside the repository
    /// tree so a run never leaves it dirty. Under <c>aspire publish</c> the same producer is a container on
    /// somebody else's host, where a path under the publishing developer's home names nothing: the
    /// directory is a container path and a named volume is attached to keep it across recreations.
    /// </remarks>
    private static string DurableDirectory(IResourceBuilder<ProjectResource> producer, string name, string path)
    {
        if (producer.ApplicationBuilder.ExecutionContext.IsPublishMode)
        {
            // The annotation rather than WithVolume: that extension is constrained to ContainerResource,
            // and a project only becomes a container image at publish time. The publisher reads the same
            // annotation either way.
            producer.WithAnnotation(new ContainerMountAnnotation(
                name,
                path,
                ContainerMountType.Volume,
                isReadOnly: false));

            return path;
        }

        return Directory.CreateDirectory(path).FullName;
    }

    /// <summary>Where a producer caches the proxy's public sealing-key document.</summary>
    internal static string ProducerKeyCacheDirectory(IDistributedApplicationBuilder builder, string moduleName) =>
        ProducerDirectory(builder, $"{moduleName}-sealing-keys");

    /// <summary>Where a producer spools sealed envelopes until the proxy takes them.</summary>
    internal static string ProducerSpoolDirectory(IDistributedApplicationBuilder builder, string moduleName) =>
        ProducerDirectory(builder, $"{moduleName}-spool");

    /// <summary>
    /// Under <c>aspire run</c> a producer directory is keyed by the app host like the proxy's store, so a
    /// store reset in one clone never leaves another clone's producer sealing to a key no proxy holds, and
    /// by <see cref="ProducerStateGeneration"/>, a layout segment that keeps the file ring's directories
    /// apart from the Vault-ring era's; it does not guard against a stale key cache (ADR-0116).
    /// </summary>
    private static string ProducerDirectory(IDistributedApplicationBuilder builder, string name) =>
        builder.ExecutionContext.IsPublishMode
            ? $"{ContainerStateRoot}/{name}"
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "userorg",
                AppHostKey(builder),
                ProducerStateGeneration,
                name);

    /// <summary>Where a published producer keeps state that outlives the container.</summary>
    private const string ContainerStateRoot = "/var/lib/userorg";
}