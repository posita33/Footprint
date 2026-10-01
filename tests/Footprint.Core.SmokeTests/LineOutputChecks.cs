using Footprint.Core;

internal static class LineOutputChecks
{
    public static void Run()
    {
        foreach (var ending in new[] { "\r\n", "\n", "\r" })
        {
            var first = string.Concat(Enumerable.Range(1, OutputPages.LinesPerPage).Select(i => $"行{i}: 日本語😀{ending}"));
            var second = string.Concat(Enumerable.Repeat("", OutputPages.LinesPerPage).Select(_ => ending));
            var tail = new string('x', 120_000) + "😀TAIL";
            var pages = new OutputPages();
            pages.Reset(first + second + tail);
            Require(pages.PageCount == 3 && pages.GetPage(0) == first && pages.GetPage(1) == second && pages.GetPage(2) == tail,
                "Complete lines, blank lines and long unbroken lines must remain on their pages for every newline format.");
            Require(pages.GetLineRange(0) == (1, 500) && pages.GetLineRange(1) == (501, 1000) && pages.GetLineRange(2) == (1001, 1001),
                "Page line ranges must match complete logical lines.");
            Require(string.Concat(Enumerable.Range(0, pages.PageCount).Select(pages.GetPage)) == pages.FullText,
                "Page concatenation must recover every character exactly.");
            pages.Reset(first);
            Require(pages.PageCount == 1 && pages.GetLineRange(0) == (1, 500), "A final newline must not produce an empty trailing page.");
            pages.Append("");
            Require(pages.PageCount == 1, "Empty chunks must not create pages.");
        }
        var split = new OutputPages();
        split.Append(string.Concat(Enumerable.Repeat("line\r\n", 499)) + "last\r");
        Require(split.PageCount == 1, "A pending CR at the page boundary must stay on its original page.");
        split.Append("\n");
        Require(split.PageCount == 1 && split.GetPage(0).EndsWith("last\r\n"), "Split CRLF must not create an empty page or count twice.");
        var firstPage = split.GetPage(0);
        split.Append("日本語\uD83D");
        split.Append("\uDE00 tail");
        Require(split.PageCount == 2 && split.GetPage(0) == firstPage && split.GetPage(1) == "日本語😀 tail" &&
            split.GetLineRange(1) == (501, 501), "Live partial lines and split surrogate pairs must remain intact on the next page.");
        split.Append("\r");
        split.Append("\nnext\n\r\nlast");
        Require(split.GetLineRange(1) == (501, 504) && string.Concat(Enumerable.Range(0, split.PageCount).Select(split.GetPage)) == split.FullText,
            "Mixed newline formats must count lines once and preserve bytes.");
        split.Reset("");
        Require(split.PageCount == 1 && split.GetPage(0) == "" && split.GetLineRange(0) == (0, 0), "Empty output must report zero lines.");
        split.Reset(new string('x', 200_000) + "😀");
        Require(split.PageCount == 1 && split.GetPage(0) == split.FullText && split.GetLineRange(0) == (1, 1),
            "A line longer than the former character threshold must never split.");
        Console.WriteLine("PASS: line-based paging, line ranges, CRLF/LF/CR, mixed and split newlines, blank/partial/long lines, Unicode and exact reconstruction");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
