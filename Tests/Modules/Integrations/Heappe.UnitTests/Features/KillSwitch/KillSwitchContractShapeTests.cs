using System;
using System.Linq;
using System.Reflection;

using Heappe.Features.KillSwitch.Contract;

using Xunit;

namespace Heappe.UnitTests.Features.KillSwitch;

/// <summary>
/// Pins the hard contract constraint of B09 (ADR-0077 §1, relationship-map §D): every KillSwitch
/// API surface identifies things by natural key (provider/location/project/resource/template name)
/// and NEVER by a DB id. The GUID PKs of the kill-switch tables are storage detail; a
/// <see cref="Guid"/> or an <c>*Id</c> member on any of these types is a regression.
/// </summary>
public sealed class KillSwitchContractShapeTests
{
    public static TheoryData<Type> ContractTypes =>
    [
        typeof(KillSwitchWhitelistItem),
        typeof(SetLocationWhitelistRequest),
        typeof(SetLocationKillSwitchRequest),
        typeof(SetProviderKillSwitchRequest),
        typeof(KillSwitchLocationState),
        typeof(KillSwitchTemplateState),
        typeof(LocationKillSwitchDetail),
        typeof(KillSwitchLocationUpdateResult),
        typeof(KillSwitchProviderUpdateResult),
        typeof(KillSwitchResyncItem)
    ];

    [Theory(DisplayName = "No KillSwitch contract type exposes a Guid")]
    [MemberData(nameof(ContractTypes))]
    public void Contract_types_expose_no_guid(Type contractType)
    {
        var guidProperties = contractType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => IsGuid(p.PropertyType))
            .Select(p => p.Name);

        Assert.Empty(guidProperties);
    }

    [Theory(DisplayName = "No KillSwitch contract type exposes an *Id member")]
    [MemberData(nameof(ContractTypes))]
    public void Contract_types_expose_no_id_member(Type contractType)
    {
        var idProperties = contractType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name.EndsWith("Id", StringComparison.Ordinal) || p.Name.EndsWith("Ids", StringComparison.Ordinal))
            .Select(p => p.Name);

        Assert.Empty(idProperties);
    }

    /// <summary>
    /// ADR-0082 §5: the audit columns are stored (<c>LocationCommandTemplateKillSwitch.EnforcedByUserId</c> /
    /// <c>LiftedByUserId</c>) but NEVER exposed by any response — only <c>reason</c> and <c>enforcedAt</c> are
    /// admin-visible. Named explicitly (on top of the generic *Id scan above) so this specific ADR call is pinned
    /// even if a future contract field happened not to end in "Id".
    /// </summary>
    [Theory(DisplayName = "No KillSwitch response exposes who enforced or lifted a kill-switch")]
    [InlineData(typeof(KillSwitchLocationState))]
    [InlineData(typeof(LocationKillSwitchDetail))]
    public void Responses_expose_no_enforced_or_lifted_by(Type responseType)
    {
        var propertyNames = responseType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name);

        Assert.DoesNotContain("EnforcedByUserId", propertyNames);
        Assert.DoesNotContain("EnforcedBy", propertyNames);
        Assert.DoesNotContain("LiftedByUserId", propertyNames);
        Assert.DoesNotContain("LiftedBy", propertyNames);
    }

    private static bool IsGuid(Type type)
        => type == typeof(Guid)
            || type == typeof(Guid?)
            || (type.IsGenericType && type.GetGenericArguments().Any(IsGuid));
}
