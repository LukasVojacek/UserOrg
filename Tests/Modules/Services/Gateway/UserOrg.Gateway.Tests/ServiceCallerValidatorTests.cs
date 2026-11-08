using System.Net;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using UserOrg.Gateway.Features.ServiceValidation;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// Characterizes the service-to-service caller check: only a configured target is validated, the caller
/// must come from an internal address (loopback, 10/8, 172.16/12, 192.168/16), carry a Host header and
/// name itself in <c>X-Calling-Service</c>, and that name must be on the target's allow list. Every refusal
/// answers null with a Warning; a pass answers the caller name with an Information line.
/// </summary>
public sealed class ServiceCallerValidatorTests : IDisposable
{
    private const string Target = "forge-api";
    private const string Caller = "userorg";
    private const string InternalAddress = "10.1.2.3";

    private readonly CapturingLoggerProvider _logs = new();
    private readonly ILoggerFactory _loggerFactory;

    public ServiceCallerValidatorTests()
    {
        _loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(_logs).SetMinimumLevel(LogLevel.Trace));
    }

    public void Dispose()
    {
        _loggerFactory.Dispose();
        _logs.Dispose();
    }

    [Fact]
    public void Should_ConfigureOnlyTargetsWithAllowedCallers_When_Constructed()
    {
        var validator = Validator(
            new ServiceRoute { Target = Target, AllowedCallers = [Caller] },
            new ServiceRoute { Target = "empty-target", AllowedCallers = [] },
            new ServiceRoute { Target = "null-target", AllowedCallers = null! });

        Assert.True(validator.IsTargetConfigured(Target));
        Assert.True(validator.IsTargetConfigured("FORGE-API"));
        Assert.False(validator.IsTargetConfigured("empty-target"));
        Assert.False(validator.IsTargetConfigured("null-target"));
    }

    [Fact]
    public void Should_ConfigureNoTarget_When_TheRoutesAreNull()
    {
        var options = new ServiceToServiceOptions { Routes = null! };
        var validator = new ServiceCallerValidator(Options.Create(options), Logger());

        Assert.False(validator.IsTargetConfigured(Target));
    }

    [Fact]
    public void Should_Throw_When_TheOptionsAreNull()
    {
        Assert.Throws<ArgumentNullException>(() => new ServiceCallerValidator(null!, Logger()));
    }

    [Fact]
    public void Should_Throw_When_TheLoggerIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new ServiceCallerValidator(Options.Create(new ServiceToServiceOptions()), null!));
    }

    [Fact]
    public void Should_ReturnNullWithoutLogging_When_TheTargetIsNotConfigured()
    {
        var validator = Validator(new ServiceRoute { Target = Target, AllowedCallers = [Caller] });

        var result = validator.ValidateCaller(Request(InternalAddress, Caller), "other-target");

        Assert.Null(result);
        Assert.Empty(_logs.All);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.10.20.30")]
    [InlineData("::1")]
    [InlineData("10.0.0.1")]
    [InlineData("172.16.0.1")]
    [InlineData("172.17.5.5")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    public void Should_ReturnTheCaller_When_AnAllowedCallerComesFromAnInternalAddress(string address)
    {
        var validator = Validator(new ServiceRoute { Target = Target, AllowedCallers = [Caller] });

        var result = validator.ValidateCaller(Request(address, Caller), Target);

        Assert.Equal(Caller, result);
        var entry = Assert.Single(_logs.All);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal($"S2S validation for {Target} succeeded: caller {Caller} from IP {IPAddress.Parse(address)}", entry.Message);
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("11.0.0.1")]
    [InlineData("172.15.0.1")]
    [InlineData("172.32.0.1")]
    [InlineData("192.169.0.1")]
    [InlineData("193.168.0.1")]
    [InlineData("2001:db8::1")]
    [InlineData("fd00::1")]
    public void Should_RefuseWithAWarning_When_TheCallerComesFromANonInternalAddress(string address)
    {
        var validator = Validator(new ServiceRoute { Target = Target, AllowedCallers = [Caller] });

        var result = validator.ValidateCaller(Request(address, Caller), Target);

        Assert.Null(result);
        var entry = Assert.Single(_logs.All);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal($"S2S validation for {Target} failed: non-internal IP {IPAddress.Parse(address)}", entry.Message);
    }

    [Fact]
    public void Should_RefuseWithAWarning_When_TheRemoteAddressIsUnknown()
    {
        var validator = Validator(new ServiceRoute { Target = Target, AllowedCallers = [Caller] });
        var context = Request(InternalAddress, Caller);
        context.Connection.RemoteIpAddress = null;

        var result = validator.ValidateCaller(context, Target);

        Assert.Null(result);
        var entry = Assert.Single(_logs.All);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal($"S2S validation for {Target} failed: non-internal IP (null)", entry.Message);
    }

    [Fact]
    public void Should_RefuseWithAWarning_When_TheHostHeaderIsMissing()
    {
        var validator = Validator(new ServiceRoute { Target = Target, AllowedCallers = [Caller] });
        var context = Request(InternalAddress, Caller);
        context.Request.Headers.Remove("Host");

        var result = validator.ValidateCaller(context, Target);

        Assert.Null(result);
        var entry = Assert.Single(_logs.All);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal($"S2S validation for {Target} failed: missing Host header", entry.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void Should_RefuseWithAWarning_When_TheCallerDoesNotNameItself(string? callingService)
    {
        var validator = Validator(new ServiceRoute { Target = Target, AllowedCallers = [Caller] });

        var result = validator.ValidateCaller(Request(InternalAddress, callingService), Target);

        Assert.Null(result);
        var entry = Assert.Single(_logs.All);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal($"S2S validation for {Target} failed: missing X-Calling-Service header. Services must identify themselves.", entry.Message);
    }

    [Fact]
    public void Should_RefuseWithAWarning_When_TheCallerIsNotOnTheAllowList()
    {
        var validator = Validator(new ServiceRoute { Target = Target, AllowedCallers = [Caller] });

        var result = validator.ValidateCaller(Request(InternalAddress, "intruder"), Target);

        Assert.Null(result);
        var entry = Assert.Single(_logs.All);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal($"S2S validation for {Target} failed: caller intruder not in allowed list", entry.Message);
    }

    [Fact]
    public void Should_MatchTheAllowListIgnoringCase_When_TheCallerNameDiffersInCase()
    {
        var validator = Validator(new ServiceRoute { Target = Target, AllowedCallers = [Caller] });

        var result = validator.ValidateCaller(Request(InternalAddress, "USERORG"), Target);

        Assert.Equal("USERORG", result);
    }

    private ServiceCallerValidator Validator(params ServiceRoute[] routes) =>
        new(Options.Create(new ServiceToServiceOptions { Routes = [.. routes] }), Logger());

    private ILogger<ServiceCallerValidator> Logger() => _loggerFactory.CreateLogger<ServiceCallerValidator>();

    private static DefaultHttpContext Request(string remoteAddress, string? callingService)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remoteAddress);
        context.Request.Headers.Host = "gateway:8080";
        if (callingService is not null)
        {
            context.Request.Headers["X-Calling-Service"] = callingService;
        }

        return context;
    }
}
