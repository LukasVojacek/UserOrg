using Xunit;

namespace Architecture.Tests;

/// <summary>
/// Asserts the direction of the compile-time dependency graph: which assembly is allowed to know
/// that which other assembly exists.
/// </summary>
public sealed class DependencyDirectionTests
{
    /// <summary>Every first-party assembly this suite knows about, hosts included by name only.</summary>
    private static readonly string[] FirstParty =
    [
        SolutionAssemblies.SharedKernel,
        SolutionAssemblies.CoreDomain,
        SolutionAssemblies.CoreApplication,
        SolutionAssemblies.CoreInfrastructure,
        .. SolutionAssemblies.Hosts,
        .. SolutionAssemblies.AllModules,
    ];

    [Fact]
    public void SharedKernel_references_no_other_first_party_assembly()
    {
        AssertDoesNotReference(
            SolutionAssemblies.SharedKernel,
            FirstParty,
            $"{SolutionAssemblies.SharedKernel} is the shared kernel and must stay a leaf");
    }

    [Fact]
    public void Core_Domain_does_not_reference_the_layers_outside_it()
    {
        AssertDoesNotReference(
            SolutionAssemblies.CoreDomain,
            [SolutionAssemblies.CoreApplication, SolutionAssemblies.CoreInfrastructure, .. SolutionAssemblies.Hosts],
            "Core Domain is the innermost layer");
    }

    [Fact]
    public void Core_Application_does_not_reference_persistence_or_a_host()
    {
        AssertDoesNotReference(
            SolutionAssemblies.CoreApplication,
            [SolutionAssemblies.CoreInfrastructure, .. SolutionAssemblies.Hosts],
            "Core Application must not know its persistence adapter or any host");
    }

    [Theory]
    [MemberData(nameof(EveryModule))]
    public void A_module_does_not_reference_a_host(string module)
    {
        AssertDoesNotReference(
            module,
            SolutionAssemblies.Hosts,
            $"Module {module} must not reference a host or composition root");
    }

    [Theory]
    [MemberData(nameof(EveryModule))]
    public void A_module_does_not_reference_Core(string module)
    {
        AssertDoesNotReference(
            module,
            [SolutionAssemblies.CoreDomain, SolutionAssemblies.CoreApplication, SolutionAssemblies.CoreInfrastructure],
            $"Module {module} must stay independent of Core");
    }

    [Theory]
    [MemberData(nameof(EveryCrossFamilyPair))]
    public void Integration_families_do_not_see_each_other(string module, string foreignFamilyMember)
    {
        Assert.DoesNotContain(foreignFamilyMember, SolutionAssemblies.ReferencesOf(module));
    }

    [Theory]
    [InlineData("Heappe.Contracts", "Heappe")]
    [InlineData("HashiCorpVault.Contracts", "HashiCorpVault")]
    public void A_contracts_assembly_does_not_reference_its_implementation(string contracts, string implementation)
    {
        Assert.DoesNotContain(implementation, SolutionAssemblies.ReferencesOf(contracts));
    }

    [Fact]
    public void Core_Application_reaches_the_Heappe_module_only_through_its_contracts()
    {
        var references = SolutionAssemblies.ReferencesOf(SolutionAssemblies.CoreApplication);

        Assert.DoesNotContain("Heappe", references);
        Assert.Contains("Heappe.Contracts", references);
    }

    /// <summary>Every module assembly under src/Modules, one case each.</summary>
    public static TheoryData<string> EveryModule() => new(SolutionAssemblies.AllModules);

    /// <summary>Every (module, foreign-family member) pair across the integration families.</summary>
    public static TheoryData<string, string> EveryCrossFamilyPair()
    {
        string[][] families = [SolutionAssemblies.HeappeFamily, SolutionAssemblies.VaultFamily, SolutionAssemblies.WaldurFamily];

        var data = new TheoryData<string, string>();
        foreach (var family in families)
        {
            var foreign = families.Where(other => other != family).SelectMany(other => other);
            foreach (var module in family)
            {
                foreach (var foreignMember in foreign)
                {
                    data.Add(module, foreignMember);
                }
            }
        }

        return data;
    }

    private static void AssertDoesNotReference(string assembly, IEnumerable<string> forbidden, string because)
    {
        var offenders = SolutionAssemblies.ReferencesOf(assembly).Intersect(forbidden).ToArray();

        Assert.True(offenders.Length == 0, $"{because}, but it references: {string.Join(", ", offenders)}");
    }
}
