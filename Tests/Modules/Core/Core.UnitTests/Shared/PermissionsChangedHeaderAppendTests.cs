using UserOrg.Shared.Http;

namespace Core.UnitTests.Shared;

public sealed class PermissionsChangedHeaderAppendTests
{
    private static IEnumerable<string> Ids(int count, int length = 8) =>
        Enumerable.Range(0, count).Select(i => i.ToString("D" + length, System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public async Task Should_NameTheIds_When_TheGatewayAdvertisesTheCapability()
    {
        var context = PermissionsChangedResponse.Create();

        PermissionsChangedHeader.Append(context, ["user-a", "user-b"]);

        Assert.Equal(["user-a", "user-b"], PermissionsChangedResponse.Ids(await PermissionsChangedResponse.StartAsync(context)));
    }

    [Fact]
    public async Task Should_SendNothing_When_TheRequestLacksTheCapability()
    {
        var context = PermissionsChangedResponse.Create(withCapability: false);

        PermissionsChangedHeader.Append(context, ["user-a"]);

        Assert.Equal(0, (await PermissionsChangedResponse.StartAsync(context)).Count);
    }

    [Fact]
    public async Task Should_SendNothing_When_TheCapabilityNamesSomethingElse()
    {
        var context = PermissionsChangedResponse.Create(withCapability: false);
        context.Request.Headers[PermissionsChangedHeader.CapabilityHeader] = "something-else";

        PermissionsChangedHeader.Append(context, ["user-a"]);

        Assert.Equal(0, (await PermissionsChangedResponse.StartAsync(context)).Count);
    }

    [Fact]
    public async Task Should_SendOneHeaderOfDistinctIds_When_AppendedMoreThanOnce()
    {
        var context = PermissionsChangedResponse.Create();

        PermissionsChangedHeader.Append(context, ["user-a", "user-b"]);
        PermissionsChangedHeader.Append(context, ["user-b", "user-c", "user-a"]);

        Assert.Equal(["user-a", "user-b", "user-c"], PermissionsChangedResponse.Ids(await PermissionsChangedResponse.StartAsync(context)));
    }

    [Fact]
    public async Task Should_KeepTheHeader_When_TheExceptionHandlerClearedTheResponse()
    {
        var context = PermissionsChangedResponse.Create();

        PermissionsChangedHeader.Append(context, ["user-a"]);

        Assert.Equal(["user-a"], PermissionsChangedResponse.Ids(await PermissionsChangedResponse.FailAsync(context)));
    }

    [Fact]
    public async Task Should_NameEveryId_When_ExactlyOneHundredIdsChanged()
    {
        var context = PermissionsChangedResponse.Create();
        var ids = Ids(100).ToList();

        PermissionsChangedHeader.Append(context, ids);

        Assert.Equal(ids, PermissionsChangedResponse.Ids(await PermissionsChangedResponse.StartAsync(context)));
    }

    [Fact]
    public async Task Should_SendEvictAll_When_MoreThanOneHundredIdsChanged()
    {
        var context = PermissionsChangedResponse.Create();

        PermissionsChangedHeader.Append(context, Ids(101));

        Assert.Equal("*", (await PermissionsChangedResponse.StartAsync(context)).ToString());
    }

    [Fact]
    public async Task Should_SendEvictAll_When_TheIdsExceedFourKilobytes()
    {
        var context = PermissionsChangedResponse.Create();

        PermissionsChangedHeader.Append(context, Ids(20, length: 250));

        Assert.Equal("*", (await PermissionsChangedResponse.StartAsync(context)).ToString());
    }

    [Fact]
    public async Task Should_NameEveryId_When_TheIdsStayBelowFourKilobytes()
    {
        var context = PermissionsChangedResponse.Create();
        var ids = Ids(15, length: 250).ToList();

        PermissionsChangedHeader.Append(context, ids);

        Assert.Equal(ids, PermissionsChangedResponse.Ids(await PermissionsChangedResponse.StartAsync(context)));
    }

    [Fact]
    public async Task Should_SendEvictAll_When_EvictAllWasAppended()
    {
        var context = PermissionsChangedResponse.Create();

        PermissionsChangedHeader.Append(context, ["user-a"]);
        PermissionsChangedHeader.Append(context, [PermissionsChangedHeader.EvictAll]);

        Assert.Equal("*", (await PermissionsChangedResponse.StartAsync(context)).ToString());
    }
}