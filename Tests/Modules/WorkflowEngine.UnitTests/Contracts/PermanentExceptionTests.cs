using WorkflowEngine.Contracts.Exceptions;

namespace WorkflowEngine.Core.Tests.Contracts;

public sealed class PermanentExceptionTests
{
    [Fact(DisplayName = "Code+message constructor stores both values")]
    public void Should_StoreBothValues_When_CodeAndMessageConstructorUsed()
    {
        var ex = new PermanentException("INVALID_PAYLOAD", "missing required field");

        Assert.Equal("INVALID_PAYLOAD", ex.ErrorInfo.ErrorCode);
        Assert.Equal("missing required field", ex.ErrorInfo.ErrorMessage);
        Assert.Equal("missing required field", ex.Message);
    }

    [Fact(DisplayName = "Code+message+inner constructor stores all three values")]
    public void Should_StoreAllValues_When_CodeMessageAndInnerConstructorUsed()
    {
        var inner = new FormatException("bad format");
        var ex = new PermanentException("BAD_FORMAT", "format error", inner);

        Assert.Equal("BAD_FORMAT", ex.ErrorInfo.ErrorCode);
        Assert.Equal("format error", ex.ErrorInfo.ErrorMessage);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact(DisplayName = "PermanentException inherits from Exception")]
    public void Should_InheritFromException_When_Created()
    {
        var ex = new PermanentException("CODE", "msg");

        Assert.IsAssignableFrom<Exception>(ex);
    }
}
