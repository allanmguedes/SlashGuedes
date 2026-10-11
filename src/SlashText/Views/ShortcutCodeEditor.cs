using System.ComponentModel;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace SlashText.Views;

internal static class ShortcutCodeEditor
{
    internal static readonly (string Id, string Label, string? Resource)[] Languages =
    [
        ("text", UiText.Get("Texto simples"), null), ("csharp", "C#", "CSharp-Mode.xshd"),
        ("javascript", "JavaScript", "JavaScript-Mode.xshd"), ("json", "JSON", "Json.xshd"),
        ("python", "Python", "Python-Mode.xshd"), ("sql", "SQL", "TSQL-Mode.xshd"),
        ("powershell", "PowerShell", "PowerShell.xshd"), ("html", "HTML", "HTML-Mode.xshd"),
        ("css", "CSS", "CSS-Mode.xshd"), ("xml", "XML", "XML-Mode.xshd"),
        ("java", "Java", "Java-Mode.xshd"), ("cpp", "C/C++", "CPP-Mode.xshd")
    ];
    private static readonly Dictionary<string, IHighlightingDefinition> Definitions = new();

    internal static string NormalizeLanguage(string language) => language.ToLowerInvariant() switch
    {
        "cs" or "c#" => "csharp", "js" => "javascript", "ps1" => "powershell",
        "py" => "python", "c++" or "c" => "cpp", "" => "text", _ => language.ToLowerInvariant()
    };
    internal static string Label(string language) => Languages.FirstOrDefault(item => item.Id == NormalizeLanguage(language)).Label ?? language;

    internal static TextEditor Create(bool readOnly)
    {
        var editor = new TextEditor
        {
            IsReadOnly = readOnly, ShowLineNumbers = true, FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 13, Padding = new Thickness(10, 8, 10, 8), WordWrap = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        editor.SetResourceReference(Control.BackgroundProperty, "Lab.input");
        editor.SetResourceReference(Control.ForegroundProperty, "Lab.text");
        editor.SetResourceReference(TextEditor.LineNumbersForegroundProperty, "Lab.muted");
        editor.TextArea.Caret.CaretBrush = null;
        editor.Options.ConvertTabsToSpaces = false;
        editor.Options.IndentationSize = 4;
        return editor;
    }

    internal static void BindLanguage(TextEditor editor, Func<string> language)
    {
        string? last = null;
        void Refresh()
        {
            var color = (editor.Background as SolidColorBrush)?.Color ?? Colors.White;
            var dark = (color.R + color.G + color.B) < 384;
            var id = NormalizeLanguage(language()); var key = id + (dark ? "-dark" : "-light");
            if (last == key) return; last = key;
            editor.SyntaxHighlighting = Definition(id, dark);
        }
        editor.LayoutUpdated += (_, _) => Refresh(); editor.Loaded += (_, _) => Refresh();
        Refresh();
    }

    internal static IHighlightingDefinition? Definition(string language, bool dark)
    {
        var resource = Languages.FirstOrDefault(item => item.Id == NormalizeLanguage(language)).Resource;
        if (resource is null) return null;
        var key = resource + dark;
        if (Definitions.TryGetValue(key, out var cached)) return cached;
        using var stream = typeof(TextEditor).Assembly.GetManifestResourceStream("ICSharpCode.AvalonEdit.Highlighting.Resources." + resource)
            ?? throw new InvalidOperationException(UiText.Get("Definição de linguagem não encontrada: ") + resource);
        var xml = XDocument.Load(stream);
        foreach (var item in xml.Descendants().Where(item => item.Name.LocalName == "Color" && item.Attribute("foreground") is not null))
        {
            var name = ((string?)item.Attribute("name") ?? "").ToLowerInvariant();
            var color = name.Contains("comment") ? (dark ? "#78AB69" : "#338341")
                : name.Contains("string") || name.Contains("char") ? (dark ? "#E5B08E" : "#A34A19")
                : name.Contains("number") || name.Contains("digit") ? (dark ? "#B5CEA8" : "#795E26")
                : name.Contains("type") || name.Contains("field") ? (dark ? "#4EC9B0" : "#267F99")
                : name.Contains("punctuation") ? (dark ? "#D4D4D4" : "#333333")
                : dark ? "#82B9F0" : "#005CB7";
            item.SetAttributeValue("foreground", color);
        }
        using var reader = xml.CreateReader();
        var definition = HighlightingLoader.Load(reader, HighlightingManager.Instance);
        Definitions[key] = definition; return definition;
    }
}
