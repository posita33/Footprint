namespace Footprint.Core;

public static class WorkspaceNames
{
    public static string Next(IEnumerable<string> titles)
    {
        var largestNumber = 0;
        foreach (var title in titles)
        {
            const string prefix = "タブ ";
            if (title.StartsWith(prefix, StringComparison.Ordinal) &&
                int.TryParse(title[prefix.Length..], out var number))
                largestNumber = Math.Max(largestNumber, number);
        }
        return $"タブ {(long)largestNumber + 1}";
    }
}
