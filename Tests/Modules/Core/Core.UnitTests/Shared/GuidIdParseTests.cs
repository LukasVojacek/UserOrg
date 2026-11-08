using Core.Domain.Request;

namespace Core.UnitTests.Shared;

public sealed class GuidIdParseTests
{
    private static readonly Dictionary<string, Func<string, Guid>> Parsers = new()
    {
        [nameof(UserId)] = value => UserId.Parse(value).Value,
        [nameof(RequestId)] = value => RequestId.Parse(value).Value,
        [nameof(ProviderId)] = value => ProviderId.Parse(value).Value,
        [nameof(ProjectResourceId)] = value => ProjectResourceId.Parse(value).Value,
        [nameof(LocationId)] = value => LocationId.Parse(value).Value,
        [nameof(ProjectId)] = value => ProjectId.Parse(value).Value,
    };

    public static TheoryData<string> IdTypes => [.. Parsers.Keys];

    [Theory]
    [MemberData(nameof(IdTypes))]
    public void Should_ReturnTheGuid_When_ValueIsAGuid(string idType)
    {
        var guid = Guid.Parse("0190f3a2-7b1c-7d4e-8f00-123456789abc");

        Assert.Equal(guid, Parsers[idType](guid.ToString()));
        Assert.Equal(guid, Parsers[idType](guid.ToString("B")));
    }

    [Theory]
    [MemberData(nameof(IdTypes))]
    public void Should_ThrowEmpty_When_ValueIsNullEmptyOrWhitespace(string idType)
    {
        foreach (var value in new[] { null!, "", "   " })
        {
            var ex = Assert.Throws<ArgumentException>(() => Parsers[idType](value));
            Assert.Equal("value", ex.ParamName);
            Assert.StartsWith($"{idType} value cannot be empty", ex.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [MemberData(nameof(IdTypes))]
    public void Should_ThrowInvalidFormat_When_ValueIsNotAGuid(string idType)
    {
        var ex = Assert.Throws<ArgumentException>(() => Parsers[idType]("not-a-guid"));

        Assert.Equal("value", ex.ParamName);
        Assert.StartsWith($"Invalid {idType} format: not-a-guid", ex.Message, StringComparison.Ordinal);
    }
}
