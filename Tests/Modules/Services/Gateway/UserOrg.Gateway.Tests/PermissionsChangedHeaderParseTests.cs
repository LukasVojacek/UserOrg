using Microsoft.Extensions.Primitives;

using UserOrg.Shared.Http;

namespace UserOrg.Gateway.Tests;

public sealed class PermissionsChangedHeaderParseTests
{
    [Fact]
    public void Should_UseTheContractedHeaderNames()
    {
        Assert.Equal("X-UserOrg-Permissions-Changed", PermissionsChangedHeader.Name);
        Assert.Equal("X-UserOrg-Gateway-Capabilities", PermissionsChangedHeader.CapabilityHeader);
        Assert.Equal("*", PermissionsChangedHeader.EvictAll);
    }

    [Fact]
    public void Should_ReturnEachId_When_TheValueListsDistinctIds()
    {
        var ids = PermissionsChangedHeader.Parse(new StringValues("user-a,user-b"));

        Assert.Equal(["user-a", "user-b"], ids);
    }

    [Fact]
    public void Should_DropBlanksAndDuplicates_When_TheValueCarriesThem()
    {
        var ids = PermissionsChangedHeader.Parse(new StringValues("user-a,,user-b,user-a, ,user-b"));

        Assert.Equal(["user-a", "user-b"], ids);
    }

    [Fact]
    public void Should_ReturnTheEvictAllMarkerAlone_When_TheValueIsTheMarker()
    {
        var ids = PermissionsChangedHeader.Parse(new StringValues("*"));

        Assert.Equal(["*"], ids);
    }

    [Fact]
    public void Should_AcceptAnIdOf255Characters()
    {
        var id = new string('a', 255);

        var ids = PermissionsChangedHeader.Parse(new StringValues(id));

        Assert.Equal([id], ids);
    }

    [Fact]
    public void Should_ReturnNothing_When_TheHeaderIsAbsent()
    {
        Assert.Empty(PermissionsChangedHeader.Parse(StringValues.Empty));
    }

    [Theory]
    [MemberData(nameof(MalformedValues))]
    public void Should_ReturnAnEmptyList_When_TheValueIsMalformed(string[] values)
    {
        var ids = PermissionsChangedHeader.Parse(new StringValues(values));

        Assert.Empty(ids);
    }

    public static TheoryData<string[]> MalformedValues() => new()
    {
        { new[] { "user-a", "user-b" } },
        { new[] { "*,user-a" } },
        { new[] { "user-a,*" } },
        { new[] { "user-a," + new string('a', 256) } },
        { new[] { "user a" } },
        { new[] { "user-a,user\tb" } },
        { new[] { "user-a,user\u0001b" } },
    };
}