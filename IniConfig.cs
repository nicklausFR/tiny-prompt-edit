using System.Globalization;

namespace TinyPromptEdit;

public sealed class IniConfig
{
    private readonly Dictionary<string, Dictionary<string, string>> data =
        new(StringComparer.OrdinalIgnoreCase);

    public static IniConfig Load(string path)
    {
        var cfg = new IniConfig();

        if (!File.Exists(path))
            return cfg;

        string section = "";

        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();

            if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#"))
                continue;

            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                section = line[1..^1].Trim();
                continue;
            }

            int eq = line.IndexOf('=');
            if (eq < 0)
                continue;

            string key = line[..eq].Trim();
            string value = line[(eq + 1)..].Trim();

            if (!cfg.data.TryGetValue(section, out var sec))
            {
                sec = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                cfg.data[section] = sec;
            }

            sec[key] = value;
        }

        return cfg;
    }

    public string Get(string section, string key, string fallback)
    {
        return data.TryGetValue(section, out var sec) &&
               sec.TryGetValue(key, out var value)
            ? value
            : fallback;
    }

    public int GetInt(string section, string key, int fallback) =>
        int.TryParse(Get(section, key, ""), out var v) ? v : fallback;

    public float GetFloat(string section, string key, float fallback) =>
        float.TryParse(Get(section, key, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v : fallback;

    public double GetDouble(string section, string key, double fallback) =>
        double.TryParse(Get(section, key, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v : fallback;

    public bool GetBool(string section, string key, bool fallback) =>
        bool.TryParse(Get(section, key, ""), out var v) ? v : fallback;
}
