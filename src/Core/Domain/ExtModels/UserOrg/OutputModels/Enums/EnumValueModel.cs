namespace UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Enums;

/// <summary>
/// One entry of a reference enum list exposed by <c>api/Enums/*</c>.
/// </summary>
/// <remarks>
/// The four Enums endpoints all return the same <c>{ Id, Name }</c> pair, so they share this single model.
/// It replaces the anonymous type they used to project into: an anonymous type declared as
/// <c>Produces&lt;IEnumerable&lt;object&gt;&gt;</c> gave the generated document a <c>type: array</c> with no
/// <c>items</c>, which tells a client generator nothing about the element (Spectral <c>array-items</c>).
/// Property names and casing are unchanged — <c>JsonOptions.PropertyNamingPolicy</c> is <see langword="null"/>
/// for this API, so these members serialize as <c>Id</c> and <c>Name</c> exactly as the anonymous type did.
/// </remarks>
/// <param name="Id">The numeric value of the enum member.</param>
/// <param name="Name">The name of the enum member.</param>
public sealed record EnumValueModel(int Id, string Name);