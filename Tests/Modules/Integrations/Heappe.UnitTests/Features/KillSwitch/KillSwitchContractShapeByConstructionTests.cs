using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

using Heappe.Features.KillSwitch.Contract;

using Xunit;

namespace Heappe.UnitTests.Features.KillSwitch;

/// <summary>
/// CHARACTERIZATION test (#258 safety net) re-asserting the invariant of
/// <see cref="KillSwitchContractShapeTests"/> — no <see cref="Guid"/>, no <c>*Id</c> member, no actor on any
/// KillSwitch contract (ADR-0007 §1, ADR-0010 §5) — but over types DISCOVERED BY REFLECTION rather than a
/// hardcoded list.
/// </summary>
/// <remarks>
/// The existing suite enumerates the contract types in a hardcoded <c>TheoryData</c>, so a type ADDED to the
/// namespace escapes the invariant silently unless the implementer remembers to register it. #258 adds at
/// least one new contract (the standing-whitelist PUT's request, plus whatever shape backs
/// <c>StillAllowed</c>); the test that exists to prevent id leakage must defend those BY CONSTRUCTION.
/// This test needs no maintenance when a contract is added — that is the entire point, so do not replace it
/// with an explicit list.
/// </remarks>
public sealed class KillSwitchContractShapeByConstructionTests
{
    private const string _contractNamespace = "Heappe.Features.KillSwitch.Contract";

    private static List<Type> DiscoverContractTypes()
        => [.. typeof(KillSwitchWhitelistItem).Assembly
            .GetTypes()
            .Where(t => t.Namespace == _contractNamespace
                && t.IsClass
                && !t.IsNested
                && !t.IsAbstract
                && t.GetCustomAttribute<CompilerGeneratedAttribute>() is null)
            .OrderBy(t => t.Name, StringComparer.Ordinal)];

    public static TheoryData<Type> DiscoveredContractTypes
    {
        get
        {
            var data = new TheoryData<Type>();
            foreach (var type in DiscoverContractTypes())
            {
                data.Add(type);
            }

            return data;
        }
    }

    [Fact(DisplayName = "#258 The contract namespace is actually discoverable (guards the guard)")]
    public void Discovery_finds_the_known_contract_types()
    {
        var discovered = DiscoverContractTypes();

        // If a refactor moves or renames the namespace, the Theories above would vacuously pass on an empty
        // set. Anchor on types that exist today so the guard cannot silently stop guarding.
        Assert.Contains(typeof(SetLocationKillSwitchRequest), discovered);
        Assert.Contains(typeof(LocationKillSwitchDetail), discovered);
        Assert.Contains(typeof(KillSwitchWhitelistItem), discovered);
    }

    [Theory(DisplayName = "#258 No contract type in the KillSwitch namespace exposes a Guid")]
    [MemberData(nameof(DiscoveredContractTypes))]
    public void Discovered_contract_types_expose_no_guid(Type contractType)
    {
        var guidProperties = contractType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => IsGuid(p.PropertyType))
            .Select(p => p.Name);

        Assert.Empty(guidProperties);
    }

    [Theory(DisplayName = "#258 No contract type in the KillSwitch namespace exposes an *Id member")]
    [MemberData(nameof(DiscoveredContractTypes))]
    public void Discovered_contract_types_expose_no_id_member(Type contractType)
    {
        var idProperties = contractType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name.EndsWith("Id", StringComparison.Ordinal) || p.Name.EndsWith("Ids", StringComparison.Ordinal))
            .Select(p => p.Name);

        Assert.Empty(idProperties);
    }

    [Theory(DisplayName = "#258 No contract type in the KillSwitch namespace exposes who enforced or lifted")]
    [MemberData(nameof(DiscoveredContractTypes))]
    public void Discovered_contract_types_expose_no_actor(Type contractType)
    {
        var propertyNames = contractType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToList();

        Assert.DoesNotContain("EnforcedByUserId", propertyNames);
        Assert.DoesNotContain("EnforcedBy", propertyNames);
        Assert.DoesNotContain("LiftedByUserId", propertyNames);
        Assert.DoesNotContain("LiftedBy", propertyNames);
        Assert.DoesNotContain("AddedByUserId", propertyNames);
        Assert.DoesNotContain("AddedBy", propertyNames);
    }

    private static bool IsGuid(Type type)
        => type == typeof(Guid)
            || type == typeof(Guid?)
            || (type.IsGenericType && type.GetGenericArguments().Any(IsGuid));
}
