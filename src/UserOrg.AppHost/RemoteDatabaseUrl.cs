using Microsoft.Data.SqlClient;

namespace UserOrg.AppHost;

internal static class RemoteDatabaseUrl
{
    private const int DefaultPort = 1433;

    private const string TcpPrefix = "tcp:";

    public static string? FromConnectionString(string? connectionString)
    {
        string dataSource;

        try
        {
            dataSource = new SqlConnectionStringBuilder(connectionString).DataSource;
        }
        catch (ArgumentException)
        {
            return null;
        }

        var address = dataSource.StartsWith(TcpPrefix, StringComparison.OrdinalIgnoreCase)
            ? dataSource[TcpPrefix.Length..]
            : dataSource;
        var parts = address.Split(',', 2, StringSplitOptions.TrimEntries);
        var host = parts[0].Split('\\')[0];
        var port = parts.Length == 2 && int.TryParse(parts[1], out var explicitPort) ? explicitPort : DefaultPort;

        return Uri.CheckHostName(host) == UriHostNameType.Unknown
            ? null
            : new UriBuilder("tcp", host, port).Uri.ToString();
    }
}