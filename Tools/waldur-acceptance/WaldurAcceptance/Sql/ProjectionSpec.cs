namespace UserOrg.Tools.WaldurAcceptance.Sql;

/// <summary>
/// Declarative natural-key projection for one entity: a SQL query plus which returned columns form
/// the natural key. Every other returned column becomes a comparable field. The same spec is run
/// against both the OLD and the NEW connection string, so the projections are identical by
/// construction. The SQL is validated at the live run (it needs a real UserOrg schema); the specs
/// live here as a seam so the classifier stays testable without a database.
/// </summary>
/// <param name="EntityType">Logical entity name (see <see cref="Core.EntityTypes"/>).</param>
/// <param name="Sql">Parameterless <c>SELECT</c>; column names become field names.</param>
/// <param name="KeyColumns">Columns concatenated (in order, <c>|</c>-joined) into the natural key.</param>
public sealed record ProjectionSpec(string EntityType, string Sql, IReadOnlyList<string> KeyColumns);