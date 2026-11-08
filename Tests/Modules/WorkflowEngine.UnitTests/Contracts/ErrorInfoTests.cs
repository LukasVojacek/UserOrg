using WorkflowEngine.Contracts.Exceptions;

namespace WorkflowEngine.Core.Tests.Contracts;

public sealed class ErrorInfoTests
{
    [Fact(DisplayName = "ErrorInfo record holds ErrorCode and ErrorMessage")]
    public void Should_StoreValues_When_Created()
    {
        var info = new ErrorInfo("CODE_1", "Something went wrong");

        Assert.Equal("CODE_1", info.ErrorCode);
        Assert.Equal("Something went wrong", info.ErrorMessage);
    }

    [Fact(DisplayName = "ErrorInfo records with same values are equal")]
    public void Should_BeEqual_When_ValuesAreIdentical()
    {
        var a = new ErrorInfo("ERR", "msg");
        var b = new ErrorInfo("ERR", "msg");

        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact(DisplayName = "ErrorInfo records with different ErrorCode are not equal")]
    public void Should_NotBeEqual_When_ErrorCodeDiffers()
    {
        var a = new ErrorInfo("ERR_A", "msg");
        var b = new ErrorInfo("ERR_B", "msg");

        Assert.NotEqual(a, b);
    }

    [Fact(DisplayName = "ErrorInfo records with different ErrorMessage are not equal")]
    public void Should_NotBeEqual_When_ErrorMessageDiffers()
    {
        var a = new ErrorInfo("ERR", "message one");
        var b = new ErrorInfo("ERR", "message two");

        Assert.NotEqual(a, b);
    }

    [Fact(DisplayName = "ErrorInfo ToString includes both fields")]
    public void Should_IncludeBothFields_When_ConvertedToString()
    {
        var info = new ErrorInfo("MY_CODE", "my message");
        var str = info.ToString();

        Assert.Contains("MY_CODE", str);
        Assert.Contains("my message", str);
    }
}
