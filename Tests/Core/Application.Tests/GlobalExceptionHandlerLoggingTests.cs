using Core.DataAccess.Exceptions;
using Core.Domain.Exceptions;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

using UserOrganizationManagement.API;
using UserOrganizationManagement.Domain.Exceptions;

namespace UserOrganizationManagement.Application.Tests;

/// <summary>
/// The .NET 10 exception-handler middleware drops its own unhandled-exception log once an
/// <see cref="Microsoft.AspNetCore.Diagnostics.IExceptionHandler"/> returns true, so
/// <see cref="GlobalExceptionHandler"/> is the only place a handled exception's stack trace can be recorded.
/// </summary>
public sealed class GlobalExceptionHandlerLoggingTests
{
    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, Exception? Exception)> Records { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Records.Add((logLevel, exception));
    }

    public static TheoryData<Exception, LogLevel> ExceptionsAndLevels() => new()
    {
        { new InvalidOperationException("boom"), LogLevel.Error },
        { new TemplatesInfoPreconditionException("bad precondition"), LogLevel.Error },
        { new RoleServiceException(), LogLevel.Error },
        { new RecordNotFoundException("missing"), LogLevel.Warning },
        { new RecordReadOnlyException(), LogLevel.Warning },
    };

    [Theory]
    [MemberData(nameof(ExceptionsAndLevels))]
    public async Task Should_LogTheExceptionObject_AtTheLevelOfItsStatus(Exception exception, LogLevel expectedLevel)
    {
        var logger = new RecordingLogger<GlobalExceptionHandler>();
        var handler = new GlobalExceptionHandler(logger);
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };

        await handler.TryHandleAsync(context, exception, CancellationToken.None);

        Assert.Contains(logger.Records, r => r.Level == expectedLevel && ReferenceEquals(r.Exception, exception));
    }
}
