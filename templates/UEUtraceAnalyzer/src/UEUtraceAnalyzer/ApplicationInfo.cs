namespace UEUtraceAnalyzer;

public static class ApplicationInfo
{
    public static Version Version
    {
        get
        {
            var value = typeof(App).Assembly.GetName().Version!;
            return new Version(value.Major, value.Minor, value.Build);
        }
    }
    public static string VersionLabel => "v" + Version;
}
