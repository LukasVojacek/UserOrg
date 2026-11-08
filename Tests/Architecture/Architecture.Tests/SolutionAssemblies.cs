using System.Reflection;

namespace Architecture.Tests;

/// <summary>
/// Loads the first-party assemblies the architecture rules are asserted against, and exposes
/// their compiled assembly references by simple name.
/// </summary>
/// <remarks>
/// Boundary rules are asserted over <see cref="Assembly.GetReferencedAssemblies"/> rather than
/// over the .csproj files: it is what the compiler actually emitted, so a reference that is
/// declared but unused cannot make a rule pass or fail spuriously. Every boundary rule here is
/// a negative ("must not reference"), which stays sound under the compiler's pruning of unused
/// references — pruning can only ever remove a name, never invent one.
/// </remarks>
internal static class SolutionAssemblies
{
    /// <summary>The shared kernel. Referenced by nearly everything; references nothing itself.</summary>
    public const string SharedKernel = "UserOrg.Shared";

    /// <summary>Core layers, inner to outer.</summary>
    public const string CoreDomain = "Domain";
    public const string CoreApplication = "Application";
    public const string CoreInfrastructure = "Infrastructure.DataAccess";

    /// <summary>
    /// Composition roots and hosts. Nothing under src/Modules or src/Core (bar the host itself)
    /// may name any of these.
    /// </summary>
    public static readonly string[] Hosts = ["API", "UserOrg.AppHost", "UserOrg.Gateway", "Userorg.Adapter"];

    /// <summary>
    /// The integration families. Each is a self-contained module owning its own persistence;
    /// the families are mutually invisible.
    /// </summary>
    public static readonly string[] HeappeFamily = ["Heappe", "Heappe.Contracts"];
    public static readonly string[] VaultFamily = ["HashiCorpVault", "HashiCorpVault.Contracts"];
    public static readonly string[] WaldurFamily = ["Waldur.Sync", "Waldur.Contracts"];

    /// <summary>Shared libraries under src/Modules/Libraries.</summary>
    public static readonly string[] Libraries = ["Forge.Core", "WebsocketStompClient"];

    /// <summary>Every assembly under src/Modules that this project can load and assert over.</summary>
    public static readonly string[] AllModules =
        [.. HeappeFamily, .. VaultFamily, .. WaldurFamily, .. Libraries];

    /// <summary>Loads a first-party assembly by its simple name.</summary>
    public static Assembly Load(string simpleName) => Assembly.Load(new AssemblyName(simpleName));

    /// <summary>The simple names of every assembly <paramref name="simpleName"/> compiles against.</summary>
    public static IReadOnlyCollection<string> ReferencesOf(string simpleName) =>
        [.. Load(simpleName).GetReferencedAssemblies().Select(reference => reference.Name!)];
}
