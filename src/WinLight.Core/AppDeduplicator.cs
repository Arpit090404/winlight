namespace WinLight.Core;

public static class AppDeduplicator
{
    public static AppItem[] Deduplicate(IEnumerable<AppItem> items)
    {
        AppItem[] entries = items.ToArray();
        if (entries.Length < 2) return entries;

        int[] parent = new int[entries.Length];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;
        for (int i = 0; i < entries.Length; i++)
            for (int j = i + 1; j < entries.Length; j++)
                if (IsSameApp(entries[i], entries[j])) Union(parent, i, j);

        var winners = new Dictionary<int, AppItem>();
        for (int i = 0; i < entries.Length; i++)
        {
            int root = Find(parent, i);
            if (!winners.TryGetValue(root, out AppItem? winner) || Priority(entries[i]) > Priority(winner))
                winners[root] = entries[i];
        }
        return winners.Values.ToArray();
    }

    private static bool IsSameApp(AppItem left, AppItem right)
    {
        if (!string.IsNullOrWhiteSpace(left.AppUserModelId) &&
            string.Equals(left.AppUserModelId, right.AppUserModelId, StringComparison.OrdinalIgnoreCase))
            return true;

        string? leftTarget = NormalizeTarget(left.TargetPath);
        string? rightTarget = NormalizeTarget(right.TargetPath);
        if (leftTarget is not null && rightTarget is not null &&
            string.Equals(leftTarget, rightTarget, StringComparison.OrdinalIgnoreCase))
            return left.NormalizedName == right.NormalizedName &&
                string.Equals(left.Arguments, right.Arguments, StringComparison.OrdinalIgnoreCase);

        // Legacy and synthetic entries may have no shell target, but their identity is still authoritative.
        return leftTarget is null && rightTarget is null &&
            string.Equals(left.Identity, right.Identity, StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeTarget(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return null;
        string value = target.Trim().Trim('"').Replace('/', '\\');
        if (Path.IsPathFullyQualified(value))
        {
            try { value = Path.GetFullPath(value); }
            catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
            { return null; }
        }
        return value.TrimEnd('\\');
    }

    private static int Priority(AppItem item) => item.Source switch
    {
        AppSource.AppsFolder => 3,
        AppSource.UserStartMenu => 2,
        AppSource.CommonStartMenu => 1,
        _ => 0
    };

    private static int Find(int[] parent, int index)
    {
        while (parent[index] != index)
        {
            parent[index] = parent[parent[index]];
            index = parent[index];
        }
        return index;
    }

    private static void Union(int[] parent, int left, int right) => parent[Find(parent, right)] = Find(parent, left);
}
