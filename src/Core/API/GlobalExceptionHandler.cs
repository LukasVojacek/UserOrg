using System.Diagnostics;

using Core.DataAccess.Exceptions;
using Core.Domain.Exceptions;

using FluentValidation;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using UserOrganizationManagement.Application.LogMessages;
using UserOrganizationManagement.Domain.Exceptions;

namespace UserOrganizationManagement.API;

internal sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ProblemDetails problem = new();

        switch (exception)
        {
            case TaskCanceledException:
            return false;
            case OperationCanceledException:
            return false;
            case ValidationException e:
            problem.Title = "Validation Failed";
            problem.Status = StatusCodes.Status400BadRequest;
            problem.Extensions.Add("errors", e.Errors.Select(x => new
            {
                x.PropertyName,
                x.ErrorMessage
            }));
            logger.ValidationFailed();
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            break;
            case BearerTokenNotFoundException:
            problem.Title = "Authorization token not found";
            problem.Status = StatusCodes.Status400BadRequest;
            logger.AuthTokenMissing();
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            break;
            case BadHttpRequestException badRequest:
            // Framework route/body binding failure — a caller error, so it belongs with the other 400s.
            // Status comes from the exception, not a hard-coded 400: the framework also raises this
            // type for 413 and 415, which would get this same generic Title/Detail — accepted because
            // no endpoint currently produces them through this handler.
            // Unlike the sibling branches this one also sets Detail and "code" — ADR-0020 requires every
            // caller-facing error to carry a stable code. Deliberate; not an inconsistency to tidy away.
            // "code" is a literal rather than CommonErrorCodes.InvalidFormat because that constant is
            // internal to the Core module assembly while this handler lives in API — referencing it is a
            // CS0122, not a style choice. ADR-0020, amendment 2026-07-20, records the alternatives
            // (InternalsVisibleTo, making the constant public) and why both were rejected.
            problem.Title = "Validation failed";
            problem.Detail = "One or more request parameters have an invalid format.";
            problem.Status = badRequest.StatusCode;
            problem.Extensions["code"] = "validation.invalid_format";
            httpContext.Response.StatusCode = badRequest.StatusCode;
            break;
            case RecordNotFoundException:
            problem.Title = "Record not found";
            problem.Status = StatusCodes.Status404NotFound;
            logger.RecordNotFound();
            httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            break;
            case ProjectNotFoundException:
            problem.Title = "Project not found";
            problem.Status = StatusCodes.Status404NotFound;
            logger.ProjectNotFound();
            httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            break;
            case UserNotFoundException:
            problem.Title = "User not found";
            problem.Status = StatusCodes.Status404NotFound;
            logger.UserNotFound();
            httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            break;
            case SystemRoleNotFoundException:
            problem.Title = "Role not found";
            problem.Status = StatusCodes.Status404NotFound;
            logger.RoleNotFound();
            httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            break;
            case SystemPermissionTypeNotFoundException:
            problem.Title = "System permission type not found";
            problem.Status = StatusCodes.Status404NotFound;
            logger.SystemPermissionNotFound();
            httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            break;
            case RecordReadOnlyException:
            problem.Title = "Record is read-only";
            problem.Status = StatusCodes.Status409Conflict;
            logger.ModificationAttemptOnReadOnlyRecord();
            httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
            break;
            case RoleServiceException:
            problem.Title = "Role service unavailable";
            problem.Status = StatusCodes.Status502BadGateway;
            httpContext.Response.StatusCode = StatusCodes.Status502BadGateway;
            break;
            case HeappeUnavailableException heappeUnavailable:
            problem.Title = "HEAppE service unavailable";
            problem.Status = StatusCodes.Status503ServiceUnavailable;
            // Surface the per-instance cause so the failure is diagnosable from the response, not just the logs
            // (ADR 0074). Only the sanitized descriptor and cause code are exposed — never the HEAppE URL.
            if (heappeUnavailable.Failures.Count > 0)
            {
                problem.Extensions["failures"] = heappeUnavailable.Failures
                    .Select(f => new { instance = f.Instance, cause = f.Cause })
                    .ToList();
            }
            httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            break;
            case DbUpdateConcurrencyException concurrencyException:
            // ADR-0074: the Core model has zero concurrency tokens, so this exception is never
            // caller-fault in the classic optimistic-locking sense - a PK-only UPDATE/DELETE matched
            // no rows and the underlying database-side cause is unidentified. 409 (not 500) is chosen
            // because the retry is safe for the caller and the state is contested, not because the
            // caller sent something invalid. This does NOT mean the retry will succeed - that depends
            // on a database-side fault this handler cannot diagnose.
            problem.Title = "Record was modified or removed by another operation";
            problem.Status = StatusCodes.Status409Conflict;
            httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
            // The 409 above is already committed before the diagnostic runs: a failure while describing
            // ex.Entries (e.g. a broken logging sink) must not turn this response back into an unhandled
            // 500 and destroy the evidence this instrument exists to capture.
            try
            {
                // Same precedence as UserOrg.Shared.Diagnostics.TracingHelper's X-Trace-Id response header,
                // so a log line can be correlated back to the response that carried it.
                var correlationId = Activity.Current?.TraceId.ToHexString() ?? httpContext.TraceIdentifier;
                LogConcurrencyConflictEntries(concurrencyException, correlationId);
            }
            catch (Exception)
            {
                // Best-effort diagnostic; swallow so the already-set 409 still reaches the caller.
            }
            break;
            case TemplatesInfoPreconditionException:
            // A precondition on our side failed (not a HEAppE outage) — 500, not 503 (ADR 0074).
            problem.Title = "Internal error building templates information";
            problem.Status = StatusCodes.Status500InternalServerError;
            httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
            break;
            default:
            problem.Title = "An unexpected error occurred.";
            problem.Status = StatusCodes.Status500InternalServerError;
            problem.Extensions["code"] = "internal_error";
            httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
            break;
        }

        // ExceptionHandlerMiddleware skips its own unhandled-exception log once this handler returns true,
        // so this is the only record of the exception and its stack trace.
        if (httpContext.Response.StatusCode >= StatusCodes.Status500InternalServerError)
        {
            logger.HttpResponseServerError(exception,
                httpContext.Response.StatusCode,
                httpContext.Request.Method,
                httpContext.Request.Scheme,
                httpContext.Request.Host.ToString(),
                httpContext.Request.Path.Value,
                httpContext.Request.QueryString.ToString(),
                httpContext.Request.ContentLength);
        }
        else
        {
            logger.HttpResponseClientError(exception,
                httpContext.Response.StatusCode,
                httpContext.Request.Method,
                httpContext.Request.Scheme,
                httpContext.Request.Host.ToString(),
                httpContext.Request.Path.Value,
                httpContext.Request.QueryString.ToString(),
                httpContext.Request.ContentLength);
        }

        // The raw exception is logged server-side above, never returned to the client (ADR-0020):
        // only a stable title/code plus the correlation id cross the wire on the unexpected path.
        httpContext.Response.ContentType = "application/json";
        problem.Extensions["requestId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken: cancellationToken);

        return true;
    }

    private void LogConcurrencyConflictEntries(DbUpdateConcurrencyException concurrencyException, string correlationId)
    {
        if (concurrencyException.Entries.Count == 0)
        {
            logger.ConcurrencyConflictNoEntries(correlationId);
            return;
        }

        foreach (var entry in concurrencyException.Entries)
        {
            var tableName = ConcurrencyEntryDiagnostics.DescribeTable(entry.Metadata.GetTableName());
            var key = ConcurrencyEntryDiagnostics.DescribeKey(entry.Metadata.FindPrimaryKey(), name => entry.Property(name).CurrentValue);

            logger.ConcurrencyConflictEntry(entry.Entity.GetType().Name, tableName, key, entry.State.ToString(), correlationId);
        }
    }
}

/// <summary>
/// Builds the safe, PII-free per-entry description for <see cref="DbUpdateConcurrencyException"/> diagnostics
/// (entity type, table, key). Kept free of EF's tracking APIs so the null/composite-key fallbacks are testable
/// without needing a live <see cref="Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry"/>.
/// </summary>
/// <remarks>
/// This class acts as a testability seam: <see cref="DescribeTable"/> and <see cref="DescribeKey"/> handle
/// null table names and null/composite primary keys with fallback placeholders. Those degenerate cases are
/// practically unreachable through any real EF tracking flow—a tracked entity must have a table name and
/// primary key by construction—but are valuable for test coverage of defensive fallback branches.
/// By taking primitives (<c>string?</c>, <c>IKey?</c>, and a value-reader delegate) instead of
/// <see cref="Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry"/>, the fallback paths can be
/// unit-tested directly without needing an impossible EF object graph.
/// </remarks>
internal static class ConcurrencyEntryDiagnostics
{
    /// <summary>Placeholder returned when table name is unavailable.</summary>
    internal const string UnknownTable = "<unknown table>";

    /// <summary>Placeholder returned when primary key is unavailable.</summary>
    internal const string UnknownKey = "<unknown key>";

    /// <summary>
    /// Returns a safe description of the table name, with a fallback placeholder if the name is null.
    /// </summary>
    /// <param name="tableName">The table name from <see cref="IReadOnlyEntityType.GetTableName()"/>, or null.</param>
    /// <returns>The table name, or <see cref="UnknownTable"/> if null.</returns>
    internal static string DescribeTable(string? tableName) => tableName ?? UnknownTable;

    /// <summary>
    /// Returns a comma-separated serialization of the primary key values, with a fallback placeholder if the key is null.
    /// </summary>
    /// <param name="primaryKey">The primary key from <see cref="IReadOnlyEntityEntry.Metadata.FindPrimaryKey()"/>, or null.</param>
    /// <param name="currentValueOf">A delegate that retrieves the current value of a key property by name.</param>
    /// <returns>A serialized key in the form "ColumnA=value, ColumnB=value", or <see cref="UnknownKey"/> if the key is null.</returns>
    /// <remarks>
    /// Composite keys are serialized with all columns joined by ", " (e.g., "PartA=1, PartB=2").
    /// The <paramref name="currentValueOf"/> delegate is called to read values from the tracked entity,
    /// keeping this method pure—it takes only primitives and a behavior function, not an EF-specific object.
    /// </remarks>
    internal static string DescribeKey(IKey? primaryKey, Func<string, object?> currentValueOf)
    {
        if (primaryKey is null)
        {
            return UnknownKey;
        }

        return string.Join(", ", primaryKey.Properties.Select(p => $"{p.Name}={currentValueOf(p.Name)}"));
    }
}