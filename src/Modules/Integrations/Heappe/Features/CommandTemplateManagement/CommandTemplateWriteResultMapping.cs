using Heappe.Features.KillSwitch;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.AspNetCore.Http;

namespace Heappe.Features.CommandTemplateManagement;

/// <summary>
/// Maps the Activate/Deactivate write-path <see cref="Result{VoidResult, Error}"/> to its HTTP
/// response, shared by both endpoints.
/// </summary>
internal static class CommandTemplateWriteResultMapping
{
    internal static IResult ToWriteHttpResult(this Result<VoidResult, Error> result)
        => result.Match(
            _ => (IResult)Results.NoContent(),
            error => KillSwitchErrorCodes.FrozenConflictOrNull(error)
                ?? TypedResults.Problem(statusCode: result.State.ToHttpStatusCode(), title: result.State.ToTitle(), detail: error.Description));
}