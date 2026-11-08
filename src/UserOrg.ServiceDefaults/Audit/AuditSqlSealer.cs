using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Lukdrasil.StepUpLogging;
using Lukdrasil.StepUpLogging.Audit.EncryptedSpool;

using Ogham.Sealing;

namespace UserOrg.ServiceDefaults.Audit;

/// <summary>
/// Turns the spooled audit payload into the parametrized immugw <c>sqlexec</c> request that
/// writes one <c>audit_log</c> row, and seals it. The audit unsealing proxy only opens the
/// envelope and forwards what is inside, so every decision about the archive - the columns, the
/// timestamp, the immudb login - is made here.
/// </summary>
internal sealed class AuditSqlSealer(ISealer sealer, ImmuDbCredentials credentials) : IAuditPayloadEncryptor
{
    // immudb counts VARCHAR[n] in bytes, not characters, so these are byte widths - and the
    // table-creation DDL has to declare exactly them.
    private const int ModuleNameBytes = 64;
    private const int VersionBytes = 32;
    private const int UserIdBytes = 128;
    private const int ActivityBytes = 128;
    private const int ObjectTypeBytes = 128;
    private const int ObjectIdBytes = 128;
    private const int IpAddressBytes = 45;
    private const int StatusBytes = 16;
    private const int CorrelationIdBytes = 64;

    // old_values / new_values / metadata are JSON columns, so they are never cut mid-document -
    // half a document is unparseable text that an append-only archive can never take back.
    private const int DocumentBytes = 8192;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async ValueTask<byte[]> EncryptAsync(ReadOnlyMemory<byte> payload)
    {
        var spooled = JsonSerializer.Deserialize<SpoolPayload>(payload.Span, SerializerOptions)
            ?? throw new ArgumentException("The spooled audit payload deserialized to null.", nameof(payload));

        var request = new SealedAuditRequest(credentials, ComposeInsert(spooled));

        return await sealer.SealAsync(JsonSerializer.Serialize(request, SerializerOptions)).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the parametrized immugw <c>sqlexec</c> request that will INSERT one row into <c>audit_log</c>.
    /// Uses <paramref name="spooled"/>.AuditEvent.TimestampUtc, never the wall-clock time at seal-time:
    /// audit records can sit in the producer's spool for hours, and the timestamp must reflect when the
    /// audited action occurred, not when the unsealing proxy processes it.
    /// </summary>
    private static SqlExecRequest ComposeInsert(SpoolPayload spooled)
    {
        var auditEvent = spooled.AuditEvent;
        List<NamedParam> columns =
        [
            new("time_stamp", SqlValue.Timestamp(auditEvent.TimestampUtc))
        ];

        AddText(columns, "module_name", spooled.ModuleName, ModuleNameBytes);
        AddText(columns, "version", spooled.Version, VersionBytes);
        AddText(columns, "user_id", auditEvent.ActorId, UserIdBytes);
        AddText(columns, "activity", auditEvent.Action, ActivityBytes);
        AddText(columns, "object_type", auditEvent.TargetType, ObjectTypeBytes);
        AddText(columns, "object_id", auditEvent.TargetId, ObjectIdBytes);
        AddDocument(columns, "old_values", AsDocument(auditEvent.OldValues));
        AddDocument(columns, "new_values", AsDocument(auditEvent.NewValues));
        AddText(columns, "ip_address", auditEvent.SourceIp, IpAddressBytes);
        AddText(columns, "status", auditEvent.Outcome.ToString(), StatusBytes);
        AddText(columns, "correlation_id", auditEvent.TraceId, CorrelationIdBytes);
        AddDocument(columns, "metadata", ComposeMetadata(auditEvent));

        var names = columns.Select(column => column.Name).ToArray();
        var sql = $"INSERT INTO audit_log ({string.Join(", ", names)}) VALUES (@{string.Join(", @", names)})";

        return new SqlExecRequest(sql, columns, NoWait: true);
    }

    /// <summary>
    /// Adds a column to the INSERT with a text value, truncating to the column's byte width without
    /// splitting UTF-8 sequences. All values travel as named parameters, never as literals concatenated
    /// into the SQL text — this is what prevents SQL injection and distinguishes this from Expirio's
    /// own PoC <c>ToSqlExecRawRequest</c>.
    /// </summary>
    private static void AddText(List<NamedParam> columns, string name, string? value, int maxBytes)
    {
        if (!string.IsNullOrEmpty(value))
        {
            columns.Add(new NamedParam(name, SqlValue.Text(FitToColumn(value, maxBytes))));
        }
    }

    /// <summary>
    /// Stores a JSON document whole, or - when it outgrows the column - swaps it for a marker
    /// document that keeps the column parseable and records the size that was dropped.
    /// </summary>
    private static void AddDocument(List<NamedParam> columns, string name, string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return;
        }

        var serializedBytes = Encoding.UTF8.GetByteCount(json);
        var stored = serializedBytes <= DocumentBytes
            ? json
            : $$"""{"_truncated":true,"bytes":{{serializedBytes}}}""";

        columns.Add(new NamedParam(name, SqlValue.Text(stored)));
    }

    /// <summary>
    /// Everything the column table does not give a column of its own, as one JSON document. An
    /// oversized caller <c>Data</c> is dropped on its own - the library's own fields, <c>eventId</c>
    /// above all, are the receiver's deduplication key and may never be lost to a caller's payload.
    /// </summary>
    private static string ComposeMetadata(AuditEvent auditEvent)
    {
        var whole = JsonSerializer.Serialize(MetadataFields(auditEvent, auditEvent.Data), SerializerOptions);

        if (Encoding.UTF8.GetByteCount(whole) <= DocumentBytes)
        {
            return whole;
        }

        var withoutData = MetadataFields(auditEvent, data: null);
        withoutData["_dataTruncated"] = true;
        withoutData["_dataBytes"] = Encoding.UTF8.GetByteCount(AsDocument(auditEvent.Data) ?? string.Empty);

        return JsonSerializer.Serialize(withoutData, SerializerOptions);
    }

    /// <summary>
    /// The audit event's own fields are written last, so a caller-supplied <c>Data</c> key of the
    /// same name cannot overwrite what the library recorded.
    /// </summary>
    private static Dictionary<string, object?> MetadataFields(AuditEvent auditEvent, IReadOnlyDictionary<string, object?>? data)
    {
        var metadata = new Dictionary<string, object?>(StringComparer.Ordinal);

        if (data is not null)
        {
            foreach (var (key, value) in data)
            {
                metadata[key] = value;
            }
        }

        metadata["actorType"] = auditEvent.ActorType;
        metadata["reason"] = auditEvent.Reason;
        metadata["tenantId"] = auditEvent.TenantId;
        metadata["spanId"] = auditEvent.SpanId;
        metadata["userAgent"] = auditEvent.UserAgent;
        metadata["onBehalfOfId"] = auditEvent.OnBehalfOfId;
        metadata["eventId"] = auditEvent.EventId;

        return metadata;
    }

    private static string? AsDocument(IReadOnlyDictionary<string, object?>? values) =>
        values is null or { Count: 0 } ? null : JsonSerializer.Serialize(values, SerializerOptions);

    /// <summary>
    /// Cuts <paramref name="value"/> down to what the column holds without ever splitting a
    /// UTF-8 sequence - a half-written character would be a permanent, unreadable byte in an
    /// append-only archive.
    /// </summary>
    internal static string FitToColumn(string value, int maxBytes)
    {
        if (Encoding.UTF8.GetByteCount(value) <= maxBytes)
        {
            return value;
        }

        Encoding.UTF8.GetEncoder().Convert(value, new byte[maxBytes], flush: false, out var charactersThatFit, out _, out _);
        var fitted = value[..charactersThatFit];

        // An unpaired surrogate is counted in as a character the encoder consumed without emitting
        // bytes, and then costs 3 bytes as U+FFFD once the cut string is encoded on its own.
        while (Encoding.UTF8.GetByteCount(fitted) > maxBytes)
        {
            fitted = fitted[..^1];
        }

        return fitted;
    }

    private sealed record SpoolPayload(string ModuleName, string Version, AuditEvent AuditEvent);

    private sealed record SealedAuditRequest(ImmuDbCredentials Credentials, SqlExecRequest SqlExec);

    private sealed record SqlExecRequest(string Sql, IReadOnlyList<NamedParam> Params, bool NoWait);

    private sealed record NamedParam(string Name, SqlValue Value);

    /// <summary>immudb's <c>SQLValue</c> one-of, narrowed to the two shapes this table needs.</summary>
    private sealed record SqlValue
    {
        [JsonPropertyName("s")]
        public string? S { get; private init; }

        [JsonPropertyName("ts")]
        public long? Ts { get; private init; }

        internal static SqlValue Text(string value) => new() { S = value };

        internal static SqlValue Timestamp(DateTimeOffset value) =>
            new() { Ts = (value - DateTimeOffset.UnixEpoch).Ticks / TimeSpan.TicksPerMicrosecond };
    }
}