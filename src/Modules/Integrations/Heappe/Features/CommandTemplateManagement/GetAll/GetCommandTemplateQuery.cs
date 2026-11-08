using Heappe.Features.CommandTemplateManagement.GetAll.Response;

using Lukdrasil.Error;
using Lukdrasil.Result;

using UserOrg.Shared.Abstractions.Interfaces;

namespace Heappe.Features.CommandTemplateManagement.GetAll;

/// <summary>
/// Represents a query to retrieve command templates for a specified resource within a project.
/// </summary>
/// <param name="projectShortName">The short name of the project for which command templates are requested. Cannot be null or empty.</param>
/// <param name="resourceName">The name of the resource whose command templates are to be retrieved. Cannot be null or empty.</param>
internal sealed record GetCommandTemplateQuery
(
    string projectShortName,
    string resourceName
) : IRequest<Result<IList<CommandTemplateResponse>, Error>>;