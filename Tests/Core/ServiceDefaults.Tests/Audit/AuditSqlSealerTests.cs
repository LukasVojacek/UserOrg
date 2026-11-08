using System.Globalization;
using System.Text;
using System.Text.Json;

using Lukdrasil.StepUpLogging;

using UserOrg.ServiceDefaults.Audit;

namespace ServiceDefaults.Tests.Audit;

/// <summary>
/// Covers the producer half of the audit chain end to end: a real audit event, sealed by the
/// real sealer, unsealed with the matching private key, and read back as the parametrized
/// immugw request the proxy will forward.
/// </summary>
public sealed class AuditSqlSealerTests(AuditSealingHarness harness) : IClassFixture<AuditSealingHarness>
{
    /// <summary>2026-02-03T09:30:15.123456Z - long before the seal, because the spool is not instant.</summary>
    private static readonly DateTimeOffset SpooledAt =
        new DateTimeOffset(2026, 2, 3, 9, 30, 15, 123, TimeSpan.Zero) + TimeSpan.FromTicks(4560);

    private const long SpooledAtUnixMicroseconds = 1_770_111_015_123_456;

    private const string Action = "project.user.role.changed";
    private const string ActorId = "user-42";
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    // The spool file's only variable region is base64, so plaintext probes have to be spellable
    // in the base64 alphabet - otherwise their absence says nothing about sealing.
    private const string Base64SafeAction = "projectUserRoleChanged";
    private const string Base64SafeActorId = "user42Xy";

    private static AuditEvent RoleChangeDenied() => AuditEvent.Denied(Action, ActorId, "user") with
    {
        TimestampUtc = SpooledAt,
        EventId = Guid.CreateVersion7(),
        TenantId = "lexis",
        TargetType = "ProjectUser",
        TargetId = "proj-7/user-42",
        Reason = "Nedostatečná oprávnění k změně role",
        SourceIp = "10.1.2.3",
        TraceId = TraceId,
        SpanId = "00f067aa0ba902b7",
        UserAgent = "Mozilla/5.0",
        OnBehalfOfId = "svc-portal",
        Data = new Dictionary<string, object?> { ["projectShortName"] = "PRJ-7" },
        OldValues = new Dictionary<string, object?> { ["role"] = "Manager" },
        NewValues = new Dictionary<string, object?> { ["role"] = "Member" }
    };

    [Fact(DisplayName = "Should_MapEveryAuditColumn_When_TheEventIsSealed")]
    public async Task Should_MapEveryAuditColumn_When_TheEventIsSealed()
    {
        var auditEvent = RoleChangeDenied();

        var audited = await harness.AuditAsync(auditEvent);

        Assert.Equal(SpooledAtUnixMicroseconds, Timestamp(audited, "time_stamp"));
        Assert.Equal(AuditSealingHarness.ModuleName, Text(audited, "module_name"));
        Assert.Equal(AuditSealingHarness.ModuleVersion, Text(audited, "version"));
        Assert.Equal(ActorId, Text(audited, "user_id"));
        Assert.Equal(Action, Text(audited, "activity"));
        Assert.Equal("ProjectUser", Text(audited, "object_type"));
        Assert.Equal("proj-7/user-42", Text(audited, "object_id"));
        Assert.Equal("10.1.2.3", Text(audited, "ip_address"));
        Assert.Equal("Denied", Text(audited, "status"));
        Assert.Equal(TraceId, Text(audited, "correlation_id"));

        Assert.Equal("Manager", JsonProperty(Text(audited, "old_values"), "role"));
        Assert.Equal("Member", JsonProperty(Text(audited, "new_values"), "role"));

        var metadata = Text(audited, "metadata");
        Assert.Equal("user", JsonProperty(metadata, "actorType"));
        Assert.Equal("Nedostatečná oprávnění k změně role", JsonProperty(metadata, "reason"));
        Assert.Equal("lexis", JsonProperty(metadata, "tenantId"));
        Assert.Equal("00f067aa0ba902b7", JsonProperty(metadata, "spanId"));
        Assert.Equal("Mozilla/5.0", JsonProperty(metadata, "userAgent"));
        Assert.Equal("svc-portal", JsonProperty(metadata, "onBehalfOfId"));
        Assert.Equal(auditEvent.EventId.ToString(), JsonProperty(metadata, "eventId"));
        Assert.Equal("PRJ-7", JsonProperty(metadata, "projectShortName"));
    }

    [Fact(DisplayName = "Should_OmitTheColumn_When_TheAuditEventLeavesItUnset")]
    public async Task Should_OmitTheColumn_When_TheAuditEventLeavesItUnset()
    {
        var audited = await harness.AuditAsync(RoleChangeDenied() with { TargetType = null, TargetId = null, SourceIp = null });

        Assert.DoesNotContain("object_type", ParameterNames(audited));
        Assert.DoesNotContain("object_id", ParameterNames(audited));
        Assert.DoesNotContain("ip_address", ParameterNames(audited));
    }

    [Fact(DisplayName = "Should_CarryTheImmuDbLogin_When_TheEventIsSealed")]
    public async Task Should_CarryTheImmuDbLogin_When_TheEventIsSealed()
    {
        var audited = await harness.AuditAsync(RoleChangeDenied());

        var credentials = audited.Content.GetProperty("credentials");
        Assert.Equal(AuditSealingHarness.ImmuDbUsername, credentials.GetProperty("username").GetString());
        Assert.Equal(AuditSealingHarness.ImmuDbPassword, credentials.GetProperty("password").GetString());
        Assert.Equal(AuditSealingHarness.ImmuDbDatabase, credentials.GetProperty("database").GetString());
    }

    [Fact(DisplayName = "Should_PutNoValueInTheSqlText_When_TheEventIsSealed")]
    public async Task Should_PutNoValueInTheSqlText_When_TheEventIsSealed()
    {
        var audited = await harness.AuditAsync(RoleChangeDenied());
        var sql = SqlExec(audited).GetProperty("sql").GetString()!;

        Assert.StartsWith("INSERT INTO audit_log (", sql, StringComparison.Ordinal);
        Assert.True(SqlExec(audited).GetProperty("noWait").GetBoolean());

        foreach (var parameter in Parameters(audited))
        {
            var name = parameter.GetProperty("name").GetString()!;
            Assert.Contains($"@{name}", sql, StringComparison.Ordinal);

            var value = parameter.GetProperty("value");
            var literal = value.TryGetProperty("s", out var text)
                ? text.GetString()!
                : value.GetProperty("ts").GetInt64().ToString(CultureInfo.InvariantCulture);
            Assert.DoesNotContain(literal, sql, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(AuditSealingHarness.ImmuDbPassword, sql, StringComparison.Ordinal);
    }

    [Theory(DisplayName = "Should_TruncateOnAUtf8ByteBoundary_When_AValueExceedsItsColumnLimit")]
    [InlineData('ě', 64)]
    [InlineData('字', 42)]
    public async Task Should_TruncateOnAUtf8ByteBoundary_When_AValueExceedsItsColumnLimit(char character, int expectedCharacters)
    {
        var overlong = new string(character, 200);

        var audited = await harness.AuditAsync(RoleChangeDenied() with { ActorId = overlong });

        var stored = Text(audited, "user_id");
        Assert.Equal(new string(character, expectedCharacters), stored);
        Assert.True(Encoding.UTF8.GetByteCount(stored) <= 128);
        Assert.DoesNotContain("�", stored, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Should_LeaveNoPlaintextInTheSpoolFile_When_TheEventIsSealed")]
    public async Task Should_LeaveNoPlaintextInTheSpoolFile_When_TheEventIsSealed()
    {
        var audited = await harness.AuditAsync(
            AuditEvent.Denied(Base64SafeAction, Base64SafeActorId, "user") with { TimestampUtc = SpooledAt });

        string[] probes = [Base64SafeActorId, Base64SafeAction, AuditSealingHarness.ImmuDbPassword];
        foreach (var probe in probes)
        {
            AssertNotInTheClear(probe, "the spool file", audited.SpoolFileBytes);
            AssertNotInTheClear(probe, "the sealed envelope", audited.EnvelopeBytes);
        }
    }

    [Fact(DisplayName = "Should_ReplaceTheDocumentWithAValidJsonMarker_When_ItExceedsTheColumnLimit")]
    public async Task Should_ReplaceTheDocumentWithAValidJsonMarker_When_ItExceedsTheColumnLimit()
    {
        var nearTheCap = new string('x', 60_000);

        var audited = await harness.AuditAsync(RoleChangeDenied() with
        {
            OldValues = new Dictionary<string, object?> { ["blob"] = nearTheCap }
        });

        var stored = Text(audited, "old_values");
        Assert.True(Encoding.UTF8.GetByteCount(stored) <= 8192, $"old_values is {stored.Length} characters wide.");

        using var marker = JsonDocument.Parse(stored);
        Assert.True(marker.RootElement.GetProperty("_truncated").GetBoolean());

        // {"blob":"xxx...x"} - 60 000 payload characters inside 11 characters of JSON syntax.
        Assert.Equal(60_011, marker.RootElement.GetProperty("bytes").GetInt64());
    }

    [Fact(DisplayName = "Should_KeepTheLibraryOwnedFields_When_TheCallerDataExceedsTheMetadataLimit")]
    public async Task Should_KeepTheLibraryOwnedFields_When_TheCallerDataExceedsTheMetadataLimit()
    {
        var auditEvent = RoleChangeDenied() with
        {
            Data = new Dictionary<string, object?> { ["blob"] = new string('x', 60_000) }
        };

        var audited = await harness.AuditAsync(auditEvent);

        var metadata = Text(audited, "metadata");
        Assert.True(Encoding.UTF8.GetByteCount(metadata) <= 8192, $"metadata is {metadata.Length} characters wide.");
        Assert.Equal(auditEvent.EventId.ToString(), JsonProperty(metadata, "eventId"));
        Assert.Equal("lexis", JsonProperty(metadata, "tenantId"));
        Assert.Equal("Nedostatečná oprávnění k změně role", JsonProperty(metadata, "reason"));
        Assert.Equal("00f067aa0ba902b7", JsonProperty(metadata, "spanId"));
        Assert.Equal("Mozilla/5.0", JsonProperty(metadata, "userAgent"));
        Assert.Equal("svc-portal", JsonProperty(metadata, "onBehalfOfId"));

        Assert.Equal("True", JsonProperty(metadata, "_dataTruncated"));

        // {"blob":"xxx...x"} - 60 000 payload characters inside 11 characters of JSON syntax.
        Assert.Equal("60011", JsonProperty(metadata, "_dataBytes"));
        Assert.DoesNotContain("xxx", metadata, StringComparison.Ordinal);
    }

    // Inline, not [InlineData]: xUnit round-trips theory arguments through a serializer that
    // repairs unpaired surrogates, which would quietly hand the helper a well-formed string.
    [Fact(DisplayName = "Should_StayWithinTheColumnWidth_When_TheValueHoldsAnUnpairedSurrogate")]
    public void Should_StayWithinTheColumnWidth_When_TheValueHoldsAnUnpairedSurrogate()
    {
        AssertFitsTheColumn("ab\uD800cd\uD800", 7);
        AssertFitsTheColumn("abc\uD800", 4);
        AssertFitsTheColumn("abc\uDC00", 5);
    }

    private static void AssertFitsTheColumn(string value, int maxBytes)
    {
        var actualBytes = Encoding.UTF8.GetByteCount(AuditSqlSealer.FitToColumn(value, maxBytes));

        Assert.True(actualBytes <= maxBytes, $"{maxBytes} bytes were allowed, {actualBytes} came back.");
    }

    [Fact(DisplayName = "Should_StoreTheDocumentByteIdentically_When_ItFitsTheColumnLimit")]
    public async Task Should_StoreTheDocumentByteIdentically_When_ItFitsTheColumnLimit()
    {
        var audited = await harness.AuditAsync(RoleChangeDenied() with
        {
            OldValues = new Dictionary<string, object?> { ["role"] = "Manager" }
        });

        var stored = Text(audited, "old_values");

        Assert.Equal("""{"role":"Manager"}""", stored);
        Assert.Equal(18, Encoding.UTF8.GetByteCount(stored));
    }

    private static void AssertNotInTheClear(string plaintext, string where, byte[] bytes) =>
        Assert.True(
            bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(plaintext)) < 0,
            $"'{plaintext}' is readable in {where}.");

    private static JsonElement SqlExec(SealedAudit audited) => audited.Content.GetProperty("sqlExec");

    private static IReadOnlyList<JsonElement> Parameters(SealedAudit audited) =>
        [.. SqlExec(audited).GetProperty("params").EnumerateArray()];

    private static IEnumerable<string> ParameterNames(SealedAudit audited) =>
        Parameters(audited).Select(parameter => parameter.GetProperty("name").GetString()!);

    private static JsonElement Value(SealedAudit audited, string column) =>
        Parameters(audited)
            .Single(parameter => parameter.GetProperty("name").GetString() == column)
            .GetProperty("value");

    private static string Text(SealedAudit audited, string column) => Value(audited, column).GetProperty("s").GetString()!;

    private static long Timestamp(SealedAudit audited, string column) => Value(audited, column).GetProperty("ts").GetInt64();

    private static string JsonProperty(string json, string name)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty(name).ToString();
    }
}