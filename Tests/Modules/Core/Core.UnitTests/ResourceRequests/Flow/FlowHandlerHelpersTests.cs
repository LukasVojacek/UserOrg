using Core.DataAccess.ResourceRequests;
using Core.Features.ResourceRequest.Flow.Handlers;
using WorkflowEngine.Contracts.Exceptions;

namespace Core.UnitTests.ResourceRequests.Flow;

public sealed class FlowHandlerHelpersTests
{
    private const string FailureCode = "resource_request.test_failed";
    private const string FailureMessage = "The test send failed.";

    [Fact]
    public async Task Should_WrapAsRetryable_When_TheSendFailsTransiently()
    {
        var transient = new InvalidOperationException("pool exhausted");

        var thrown = await Assert.ThrowsAsync<RetryableException>(
            () => FlowHandlerHelpers.SendOrRetryAsync(FailureCode, FailureMessage, () => throw transient));

        Assert.Same(transient, thrown.InnerException);
        Assert.Equal(FailureCode, thrown.ErrorInfo.ErrorCode);
    }

    [Fact]
    public async Task Should_PropagateUnwrapped_When_TheSendFailsPermanently()
    {
        var permanent = new PermanentException("resource_request.permanent", "Gone for good.");

        var thrown = await Assert.ThrowsAsync<PermanentException>(
            () => FlowHandlerHelpers.SendOrRetryAsync(FailureCode, FailureMessage, () => throw permanent));

        Assert.Same(permanent, thrown);
    }

    [Fact]
    public async Task Should_PropagateUnwrapped_When_TheProjectionRowIsMissing()
    {
        var missing = new ResourceRequestRowMissingException("proj-gpu");

        var thrown = await Record.ExceptionAsync(
            () => FlowHandlerHelpers.SendOrRetryAsync(FailureCode, FailureMessage, () => throw missing));

        Assert.Same(missing, thrown);
    }
}
