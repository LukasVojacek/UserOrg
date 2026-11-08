using Core.Shared;

namespace Core.UnitTests.Shared;

public sealed class CommonErrorCodesTests
{
    // Asserting a constant against a literal normally proves nothing. It earns its place here because the
    // value exists twice: as this constant, and as a hard-coded literal in GlobalExceptionHandler.cs, which
    // cannot reference it — the constant is internal to the Core module assembly, so the reference is a
    // CS0122 (ADR-0020, amendment 2026-07-20). GlobalExceptionHandlerTests pins the handler's copy; this
    // pins the module's, so editing either one alone fails instead of drifting silently.
    [Fact]
    public void Should_MatchGlobalExceptionHandlerLiteral_When_InvalidFormatIsDuplicated()
    {
        Assert.Equal("validation.invalid_format", CommonErrorCodes.InvalidFormat);
    }
}
