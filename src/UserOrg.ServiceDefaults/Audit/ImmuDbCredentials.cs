using System.Globalization;
using System.Text;

namespace UserOrg.ServiceDefaults.Audit;

/// <summary>
/// The immudb login the audit unsealing proxy replays on this producer's behalf. It travels
/// inside the sealed envelope, so the proxy never has to hold a credential of its own.
/// </summary>
internal sealed record ImmuDbCredentials(string Username, string Password, string Database)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append(CultureInfo.InvariantCulture, $"Username = {Username}, Password = *****, Database = {Database}");
        return true;
    }
}