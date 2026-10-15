using SlashText.Models;

namespace SlashText.Services;

public static class ClipboardRetentionPolicy
{
    public const int DefaultRecentLimit = 25;
    public const int MinimumRecentLimit = 10;
    public const int MaximumRecentLimit = 500;

    public static int NormalizeLimit(int value) =>
        Math.Clamp(value, MinimumRecentLimit, MaximumRecentLimit);

    // Favorites and pinned items never consume slots in the recents budget.
    public static IReadOnlyList<ClipboardHistoryItem> SelectExpired(
        IEnumerable<ClipboardHistoryItem> orderedNewestFirst, int recentLimit) =>
        orderedNewestFirst
            .Where(item => !item.IsFavorite && !item.IsPinned)
            .Skip(NormalizeLimit(recentLimit))
            .ToArray();
}
