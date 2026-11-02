using SlashText.Models;

namespace SlashText.Services;

/// <summary>
/// Ordered, bounded sequence of image-only clipboard pastes. WPF/Win32 side
/// effects are injected so the ordering, stop conditions and cancellation can
/// be tested without injecting keyboard input into CI desktops.
/// </summary>
public static class ClipboardSequentialPasteService
{
    public const int MaximumImages = 15;
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(1000);

    public enum StopReason { Completed, NoImages, TooManyImages, InvalidItem, TargetChanged, CopyFailed, PasteFailed, Cancelled }

    public sealed record Outcome(int Pasted, int Total, StopReason Reason);

    public static IReadOnlyList<ClipboardHistoryItem> ResolveSelection(
        IEnumerable<ClipboardHistoryItem> available, IEnumerable<string> orderedIds)
    {
        var byId = available.Where(i => i.Kind == "image")
            .GroupBy(i => i.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var selected = new List<ClipboardHistoryItem>();
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in orderedIds)
        {
            if (unique.Add(id) && byId.TryGetValue(id, out var item))
                selected.Add(item);
        }
        return selected;
    }

    public static async Task<Outcome> RunAsync(
        IReadOnlyList<ClipboardHistoryItem> images,
        Func<bool> isTargetForeground,
        Func<ClipboardHistoryItem, bool> restoreClipboard,
        Func<CancellationToken, Task<ClipboardPasteService.Result>> paste,
        Func<CancellationToken, Task> waitBetween,
        CancellationToken cancellationToken,
        Action<int, int>? progress = null)
    {
        if (images.Count == 0) return new Outcome(0, 0, StopReason.NoImages);
        if (images.Count > MaximumImages) return new Outcome(0, images.Count, StopReason.TooManyImages);
        if (images.Any(i => i.Kind != "image")) return new Outcome(0, images.Count, StopReason.InvalidItem);
        var pasted = 0;
        try
        {
            foreach (var image in images)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Never copy the next image if the original target lost focus.
                if (!isTargetForeground()) return new Outcome(pasted, images.Count, StopReason.TargetChanged);
                if (!restoreClipboard(image)) return new Outcome(pasted, images.Count, StopReason.CopyFailed);
                cancellationToken.ThrowIfCancellationRequested();
                var status = await paste(cancellationToken);
                if (status != ClipboardPasteService.Result.Sent)
                    return new Outcome(pasted, images.Count, StopReason.PasteFailed);
                pasted++;
                progress?.Invoke(pasted, images.Count);
                if (pasted < images.Count)
                    await waitBetween(cancellationToken);
            }
            return new Outcome(pasted, images.Count, StopReason.Completed);
        }
        catch (OperationCanceledException)
        {
            return new Outcome(pasted, images.Count, StopReason.Cancelled);
        }
    }
}
