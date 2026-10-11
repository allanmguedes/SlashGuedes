using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;

namespace SlashText.Localization;

/// <summary>Explicit interface copy only. Never pass document text, triggers, paths or device names.</summary>
public static class UiText
{
    public const string DefaultLanguage = "pt-BR";
    public static string Language { get; private set; } = DefaultLanguage;
    private static readonly Lazy<IReadOnlyDictionary<string, Dictionary<string, string>>> Catalogs = new(LoadCatalogs);
    private static IReadOnlyDictionary<string, string> _active => Catalogs.Value[Language];

    public static string Normalize(string? language) => language switch { "en" => "en", "es" => "es", _ => DefaultLanguage };

    public static string Key(string source) => "Ui." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..16];
    public static string Get(string source)
    {
        var key = Key(source);
        return _active.TryGetValue(key, out var translated) ? translated : source;
    }

    // Windows regional formats stay in force. Arguments are never translated.
    public static string Format(FormattableString text) => string.Format(CultureInfo.CurrentCulture, Get(text.Format), text.GetArguments());

    // Update state predates localization and stores a human message. Keep its format stable,
    // translating only this known application-owned status at the presentation boundary.
    public static string UpdateStatus(string message)
    {
        var direct = Get(message);
        if (direct != message) return direct;
        foreach (var source in new[] { "A versão {0} está ignorada neste dispositivo.", "A versão {0} será lembrada novamente mais tarde.", "SlashDesk {0} está disponível." })
        {
            var pattern = "^" + Regex.Escape(source).Replace("\\{0}", "(.+)") + "$";
            var match = Regex.Match(message, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (match.Success) return string.Format(CultureInfo.CurrentCulture, Get(source), match.Groups[1].Value);
        }
        return message;
    }

    internal static void Apply(string? language)
    {
        Language = Normalize(language);
        if (Application.Current is not { } app) return;
        foreach (var pair in _active) app.Resources[pair.Key] = pair.Value;
    }

    internal static void LoadPreference(string path)
    {
        string? language = null;
        try
        {
            if (File.Exists(path))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                foreach (var property in document.RootElement.EnumerateObject())
                    if (property.Name.Equals("language", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                        language = property.Value.GetString();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        { /* The existing settings store owns recovery and diagnostics. */ }
        Apply(language);
    }

    internal static IReadOnlyDictionary<string, Dictionary<string, string>> AllCatalogs => Catalogs.Value;
    private static IReadOnlyDictionary<string, Dictionary<string, string>> LoadCatalogs()
    {
        var result = new Dictionary<string, Dictionary<string, string>>();
        foreach (var language in new[] { DefaultLanguage, "en", "es" })
        {
            using var stream = typeof(UiText).Assembly.GetManifestResourceStream($"SlashText.Localization.{language}.json")
                ?? throw new InvalidOperationException("Missing embedded interface language catalog: " + language);
            result.Add(language, JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
                ?? throw new InvalidOperationException("Invalid interface language catalog: " + language));
        }
        return result;
    }
}
