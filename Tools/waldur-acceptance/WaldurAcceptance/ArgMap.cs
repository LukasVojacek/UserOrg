namespace UserOrg.Tools.WaldurAcceptance;

/// <summary>Minimal <c>--key value</c> argument bag. No config framework by design.</summary>
internal sealed class ArgMap
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    public static ArgMap Parse(string[] args)
    {
        var map = new ArgMap();
        for (var i = 0; i + 1 < args.Length; i += 2)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                map._values[args[i][2..]] = args[i + 1];
            }
        }

        return map;
    }

    public bool TryGet(string key, out string value) => _values.TryGetValue(key, out value!);

    public string GetOrDefault(string key, string fallback) =>
        _values.TryGetValue(key, out var value) ? value : fallback;
}