using System.IO;
using System.Globalization;
using System.Text;
using SlashText.Models;

namespace SlashText.Services;

public static class CaptureHistoryQuery
{
    public static string Label(CaptureRecord record) => record.MediaKind.ToLowerInvariant() switch
    {
        "gif" => "GIF", "video" => UiText.Get("Vídeo MP4"), _ => record.Type.ToLowerInvariant() switch
        { "monitor" => "Monitor", "regiao" => UiText.Get("Região"), "janela" => UiText.Get("Janela"), "rolagem" => UiText.Get("Captura longa"), _ => UiText.Get("Imagem") }
    };
    public static IReadOnlyList<CaptureRecord> Filter(IEnumerable<CaptureRecord> records, string filter, string search)
    {
        var terms = Normalize(search).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return records.Where(r => filter == "all" || filter.Equals(r.Type, StringComparison.OrdinalIgnoreCase) ||
            filter.Equals(r.MediaKind, StringComparison.OrdinalIgnoreCase))
            .Where(r => { var date = r.CreatedAt.ToLocalTime();
                var text = Normalize($"{Path.GetFileName(string.IsNullOrWhiteSpace(r.FilePath) ? r.PortableRelativePath : r.FilePath)} {Label(r)} {date:dd/MM/yyyy HH:mm} {date:yyyy-MM-dd} {date.ToString("MMMM", CultureInfo.GetCultureInfo("pt-BR"))}");
                return terms.All(t => text.Contains(t, StringComparison.Ordinal)); })
            .OrderByDescending(r => r.CreatedAt).ToArray();
    }
    private static string Normalize(string value) => new(value.Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).Select(char.ToLowerInvariant).ToArray());
}
