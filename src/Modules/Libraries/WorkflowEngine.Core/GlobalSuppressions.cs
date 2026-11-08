// CA1848: Use LoggerMessage delegates for performance.
// Workers log infrequent operational events (startup, errors, one-per-step lifecycle).
// The performance gain from source-generated delegates does not justify the added boilerplate
// for these low-frequency paths.
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance", "CA1848:Use the LoggerMessage delegates",
    Justification = "Worker log calls are infrequent operational events; delegate overhead is negligible.",
    Scope = "module")]

// CA1873: Logger argument evaluation — structured logging with named placeholders is intentional.
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance", "CA1873:Avoid potentially expensive logging arguments",
    Justification = "All logger calls use named structured placeholders, not string interpolation.",
    Scope = "module")]

// CA1031: Catching general Exception in background workers is intentional — workers must not
// crash the host process on any individual message failure.
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design", "CA1031:Do not catch general exception types",
    Justification = "Background workers must catch all exceptions to prevent host process termination.",
    Scope = "module")]

// CA1819: RowVersion property returns byte[] — required by EF Core rowversion concurrency token.
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance", "CA1819:Properties should not return arrays",
    Justification = "RowVersion is an EF Core concurrency token that must be byte[].",
    Scope = "member",
    Target = "~P:WorkflowEngine.Core.Domain.Request.RequestAggregate.RowVersion")]

// CA1054/CA1056: CallbackUrl is an opaque string identifier per design (could be URL, queue name, topic).
// Changing to System.Uri would break non-URL use cases and impose unnecessary allocation.
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design", "CA1054:Change the type of parameter from 'string' to 'System.Uri'",
    Justification = "CallbackUrl is an opaque string; may be a URL, queue name, or other identifier.",
    Scope = "member",
    Target = "~M:WorkflowEngine.Core.Domain.Request.RequestAggregate.Rehydrate(System.Guid,System.String,System.Text.Json.JsonDocument,WorkflowEngine.Contracts.Models.RequestStatus,System.String,System.String,System.String,System.String,System.Text.Json.JsonDocument,System.String,System.String,System.DateTimeOffset,System.DateTimeOffset,System.Nullable{System.DateTimeOffset},System.Byte[],System.Collections.Generic.IEnumerable{WorkflowEngine.Core.Domain.Request.RequestStep})")]

// CA1034: Nested extension block is the C# 14 extension type syntax; not a traditional nested type.
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design", "CA1034:Do not nest type",
    Justification = "C# 14 extension type syntax generates an implicit nested type; suppressed by convention (see Forge.Core).",
    Scope = "module")]