using System.Globalization;
using System.Text;

using UserOrg.Shared.Http;

namespace Core.UnitTests.Shared;

public sealed class PermissionsChangedHeaderByteCapTests
{
    private const int IdLength = 240;

    private static List<string> Ids(int count) =>
        [.. Enumerable.Range(0, count).Select(i => i.ToString("D" + IdLength, CultureInfo.InvariantCulture))];

    [Fact]
    public async Task Should_NameEveryId_When_TheValueIsExactlyFourKilobytes()
    {
        var context = PermissionsChangedResponse.Create();
        var ids = Ids(17);
        Assert.Equal(4096, Encoding.UTF8.GetByteCount(string.Join(',', ids)));

        PermissionsChangedHeader.Append(context, ids);

        Assert.Equal(ids, PermissionsChangedResponse.Ids(await PermissionsChangedResponse.StartAsync(context)));
    }

    [Fact]
    public async Task Should_SendEvictAll_When_TheValueIsOneByteOverFourKilobytes()
    {
        var context = PermissionsChangedResponse.Create();
        List<string> ids = [.. Ids(16), "x" + new string('9', IdLength)];
        Assert.Equal(4097, Encoding.UTF8.GetByteCount(string.Join(',', ids)));

        PermissionsChangedHeader.Append(context, ids);

        Assert.Equal("*", (await PermissionsChangedResponse.StartAsync(context)).ToString());
    }
}