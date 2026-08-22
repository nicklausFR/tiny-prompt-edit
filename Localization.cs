using System.Text.Json;

namespace TinyPromptEdit;

public sealed class Localization
{
    private readonly Dictionary<string, string> translations;

    private Localization(Dictionary<string, string> translations)
    {
        this.translations = translations;
    }

    public static Localization Load(string baseDirectory, string language)
    {
        if (string.IsNullOrWhiteSpace(language) ||
            language.Equals("en", StringComparison.OrdinalIgnoreCase))
            return new Localization(new Dictionary<string, string>());

        string safeLanguage = Path.GetFileName(language.Trim()).ToLowerInvariant();
        string exactPath = Path.Combine(baseDirectory, "locales", safeLanguage + ".po");
        string neutralLanguage = safeLanguage.Split('-', '_')[0];
        string neutralPath = Path.Combine(baseDirectory, "locales", neutralLanguage + ".po");
        string? path = File.Exists(exactPath) ? exactPath : File.Exists(neutralPath) ? neutralPath : null;

        return path is null
            ? new Localization(new Dictionary<string, string>())
            : new Localization(ParsePo(path));
    }

    public string Get(string english) =>
        translations.TryGetValue(english, out string? translated) && translated.Length > 0
            ? translated
            : english;

    private static Dictionary<string, string> ParsePo(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        string id = "";
        string value = "";
        string? state = null;

        void Commit()
        {
            if (id.Length > 0 && value.Length > 0)
                result[id] = value;
            id = "";
            value = "";
            state = null;
        }

        foreach (string rawLine in File.ReadLines(path))
        {
            string line = rawLine.Trim();
            if (line.StartsWith("msgid ", StringComparison.Ordinal))
            {
                Commit();
                id = Decode(line[6..]);
                state = "id";
            }
            else if (line.StartsWith("msgstr ", StringComparison.Ordinal))
            {
                value = Decode(line[7..]);
                state = "value";
            }
            else if (line.StartsWith('"'))
            {
                if (state == "id") id += Decode(line);
                else if (state == "value") value += Decode(line);
            }
            else if (line.Length == 0)
            {
                Commit();
            }
        }

        Commit();
        return result;
    }

    private static string Decode(string quoted) =>
        JsonSerializer.Deserialize<string>(quoted) ?? "";
}
