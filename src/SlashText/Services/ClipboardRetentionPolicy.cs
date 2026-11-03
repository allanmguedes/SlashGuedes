using SlashText.Models;

namespace SlashText.Services;

public static class ClipboardRetentionPolicy
{
    public const int DefaultRecentLimit = 50;
    public const int MinimumRecentLimit = 10;
    public const int MaximumRecentLimit = 500;

    public static int NormalizeLimit(int value) =>
        Math.Clamp(value, MinimumRecentLimit, MaximumRecentLimit);

    // Upgrade only the previous default on first SlashGuedes launch.
    // Future user selections, including an explicit 25, remain untouched.
    public static bool UpgradeLegacyDefault(AppSettings settings)
    {
        if (settings.ClipboardLimit50MigrationApplied) return false;
        if (settings.ClipboardMaxEntries == 25)
            settings.ClipboardMaxEntries = DefaultRecentLimit;
        settings.ClipboardLimit50MigrationApplied = true;
        return true;
    }

    // Favorites and pinned items never consume slots in the recents budget.
    public static IReadOnlyList<ClipboardHistoryItem> SelectExpired(
        IEnumerable<ClipboardHistoryItem> orderedNewestFirst, int recentLimit) =>
        orderedNewestFirst
            .Where(item => !item.IsFavorite && !item.IsPinned)
            .Skip(NormalizeLimit(recentLimit))
            .ToArray();
}
