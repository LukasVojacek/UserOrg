using UserOrg.Shared;

using UserOrganizationManagement.Domain.Contracts;

namespace UserOrganizationManagement.Application.Tests;

/// <summary>
/// A location's values become a routable <see cref="HeappeInstance"/> only through a valid HEAPPE_IDENTIFIER (#349):
/// there is no URL fallback, the URL is kept for logs only and an unreadable version falls back to 0.0.0.
/// </summary>
public sealed class HeappeInstanceExtensionsTests
{
    [Fact]
    public void ToHeappeInstance_ReturnsNull_When_NoIdentifierIsStored()
    {
        var values = Values((ApplicationConstants.HeappeUriKey, "https://heappe.example.org"), (ApplicationConstants.HeappeVersionKey, "6.4.0"));

        Assert.Null(values.ToHeappeInstance());
    }

    [Fact]
    public void ToHeappeInstance_ReturnsNull_When_TheIdentifierIsInvalid()
    {
        var values = Values((ApplicationConstants.HeappeIdentifierKey, "not a valid identifier!"), (ApplicationConstants.HeappeUriKey, "https://heappe.example.org"));

        Assert.Null(values.ToHeappeInstance());
    }

    [Fact]
    public void ToHeappeInstance_ReadsIdentifierVersionAndUrl_When_KeysDifferInCase()
    {
        var values = Values(("heappe_identifier", " HEAppE-Mock "), ("heappe_url", "https://heappe.example.org/api"), ("heappe_version", "v6.4.0"));

        var instance = values.ToHeappeInstance();

        Assert.Equal(new HeappeInstance(new HeappeIdentifier("heappe-mock"), new Version(6, 4, 0), new Uri("https://heappe.example.org/api/")), instance);
    }

    [Fact]
    public void ToHeappeInstance_LeavesUrlNullAndVersionZero_When_OnlyTheIdentifierIsStored()
    {
        var values = Values((ApplicationConstants.HeappeIdentifierKey, "heappe-mock"));

        var instance = values.ToHeappeInstance();

        Assert.Equal(new HeappeInstance(new HeappeIdentifier("heappe-mock"), new Version(0, 0, 0), null), instance);
    }

    [Fact]
    public void ToHeappeInstance_LeavesUrlNullAndVersionZero_When_UrlAndVersionAreUnreadable()
    {
        var values = Values(
            (ApplicationConstants.HeappeIdentifierKey, "heappe-mock"),
            (ApplicationConstants.HeappeUriKey, "not a url"),
            (ApplicationConstants.HeappeVersionKey, "latest"));

        var instance = values.ToHeappeInstance();

        Assert.Equal(new HeappeInstance(new HeappeIdentifier("heappe-mock"), new Version(0, 0, 0), null), instance);
    }

    private static List<KeyValuePair<string, string>> Values(params (string Key, string Value)[] values) =>
        [.. values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value))];
}
