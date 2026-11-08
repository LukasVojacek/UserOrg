using System.Data;

using Microsoft.Data.SqlClient;

using UserOrg.Tools.WaldurAcceptance.Core;

namespace UserOrg.Tools.WaldurAcceptance.Sql;

/// <summary>
/// The projection seam's SQL implementation: runs each <see cref="ProjectionSpec"/> against one
/// UserOrg connection string and materialises <see cref="NaturalKeyRow"/>s. Raw parameterized ADO
/// only — no EF, no product reference — so the tool survives the B14 cutover. Validated at the live
/// run; the pure classifier does not depend on it.
/// </summary>
public sealed class SqlUserOrgReader(string connectionString, string efpCoreServiceName)
{
    public IReadOnlyList<NaturalKeyRow> ReadNaturalKeyRows()
    {
        using var connection = new SqlConnection(connectionString);
        connection.Open();

        var rows = new List<NaturalKeyRow>();
        foreach (var spec in ProjectionSpecs.All)
        {
            rows.AddRange(ReadSpec(connection, spec));
        }

        return rows;
    }

    public IReadOnlyList<ServiceNameRow> ReadServiceNameRows()
    {
        using var connection = new SqlConnection(connectionString);
        connection.Open();

        var rows = new List<ServiceNameRow>();
        foreach (var (table, sql) in ProjectionSpecs.ServiceNameSources)
        {
            using var command = new SqlCommand(sql, connection);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var serviceName = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                var ownerId = reader.IsDBNull(1) ? null : Convert.ToString(reader.GetValue(1), System.Globalization.CultureInfo.InvariantCulture);
                rows.Add(new ServiceNameRow(table, serviceName, ownerId));
            }
        }

        return rows;
    }

    private List<NaturalKeyRow> ReadSpec(SqlConnection connection, ProjectionSpec spec)
    {
        using var command = new SqlCommand(spec.Sql, connection);
        command.Parameters.Add(new SqlParameter("@EfpCoreServiceName", SqlDbType.NVarChar, 256)
        {
            Value = efpCoreServiceName,
        });

        var result = new List<NaturalKeyRow>();
        using var reader = command.ExecuteReader();
        var keyOrdinals = spec.KeyColumns.Select(reader.GetOrdinal).ToArray();

        while (reader.Read())
        {
            var key = string.Join('|', keyOrdinals.Select(o => Value(reader, o)));
            var fields = new Dictionary<string, string?>(StringComparer.Ordinal);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var name = reader.GetName(i);
                if (!spec.KeyColumns.Contains(name, StringComparer.Ordinal))
                {
                    fields[name] = Value(reader, i);
                }
            }

            result.Add(new NaturalKeyRow(spec.EntityType, key, fields));
        }

        return result;
    }

    private static string? Value(SqlDataReader record, int ordinal) =>
        record.IsDBNull(ordinal) ? null : Convert.ToString(record.GetValue(ordinal), System.Globalization.CultureInfo.InvariantCulture);
}