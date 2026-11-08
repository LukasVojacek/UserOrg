using System.Text.RegularExpressions;

namespace UserOrg.Tools.DatabaseMigrations;

internal sealed record SeedOptions(string ScriptPath, IReadOnlyDictionary<string, string> Values);

internal static class SeedScript
{
    public static SeedOptions Parse(string[] args)
    {
        string? scriptPath = null;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--script")
            {
                if (i + 1 >= args.Length)
                {
                    throw new ArgumentException("Missing value for --script.");
                }

                scriptPath = args[++i];
            }
            else if (args[i] == "--set")
            {
                if (i + 1 >= args.Length)
                {
                    throw new ArgumentException("Missing value for --set.");
                }

                string setValue = args[++i];
                int equalsIndex = setValue.IndexOf('=', StringComparison.Ordinal);
                if (equalsIndex < 0)
                {
                    throw new ArgumentException($"--set value '{setValue}' is missing '='.");
                }

                string name = setValue[..equalsIndex];
                string value = setValue[(equalsIndex + 1)..];
                values[name] = value;
            }
            else
            {
                throw new ArgumentException($"Unknown option '{args[i]}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(scriptPath))
        {
            throw new ArgumentException("Missing required option --script.");
        }

        return new SeedOptions(scriptPath, values);
    }

    public static string Substitute(string script, IReadOnlyDictionary<string, string> values)
    {
        string result = script;
        foreach (var pair in values)
        {
            result = result.Replace(pair.Key, pair.Value, StringComparison.Ordinal);
        }

        return result;
    }

    public static IReadOnlyList<string> SplitBatches(string script)
    {
        return Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
            .Where(batch => !string.IsNullOrWhiteSpace(batch))
            .ToList();
    }
}