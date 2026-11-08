using Microsoft.AspNetCore.Http;

namespace Core.Shared.Auth;

/// <summary>
/// ADR-0018 scope guard: a project short-name outside the caller's scope and a genuinely-missing project
/// return the byte-identical <c>project.not_found</c> problem, so existence never leaks. Centralizes the rule
/// that was hand-copied across the project/request write and single-read endpoints.
/// </summary>
internal static class CallerScope
{
    extension(Caller caller)
    {
        internal IResult? RequireProjectScope(ProjectShortName shortName) =>
            caller.Scope.Contains(shortName) ? null : Problems.ProjectNotFound();
    }
}