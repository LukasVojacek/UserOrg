using WorkflowEngine.Contracts.Exceptions;

namespace WorkflowEngine.Core.Tests.Contracts;

public sealed class RetryableExceptionTests
{
    [Fact(DisplayName = "Code+message constructor stores both values")]
    public void Should_StoreBothValues_When_CodeAndMessageConstructorUsed()
    {
        var ex = new RetryableException("TIMEOUT", "service timed out");

        Assert.Equal("TIMEOUT", ex.ErrorInfo.ErrorCode);
        Assert.Equal("service timed out", ex.ErrorInfo.ErrorMessage);
        Assert.Equal("service timed out", ex.Message);
    }

    [Fact(DisplayName = "Code+message+inner constructor stores all three values")]
    public void Should_StoreAllValues_When_CodeMessageAndInnerConstructorUsed()
    {
        var inner = new TimeoutException("network timeout");
        var ex = new RetryableException("NET_TIMEOUT", "network call failed", inner);

        Assert.Equal("NET_TIMEOUT", ex.ErrorInfo.ErrorCode);
        Assert.Equal("network call failed", ex.ErrorInfo.ErrorMessage);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact(DisplayName = "RetryableException inherits from Exception")]
    public void Should_InheritFromException_When_Created()
    {
        var ex = new RetryableException("CODE", "msg");

        Assert.IsAssignableFrom<Exception>(ex);
    }
}
