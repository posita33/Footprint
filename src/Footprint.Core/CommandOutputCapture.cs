namespace Footprint.Core;

/// <summary>Bounds persisted output while delivering every chunk to the live view.</summary>
public sealed class CommandOutputCapture(CommandRecord record, Action<string> append, Action? onTruncated = null)
{
    public const int OutputLimit = 100_000;
    public const string TruncationNotice = "\n[出力の保存上限に達しました]\n";
    public bool IsTruncated { get; private set; }

    public static string ForStorage(string text) => text.Length <= OutputLimit
        ? text : text[..OutputLimit] + TruncationNotice;

    public void Receive(string text)
    {
        if (!IsTruncated)
        {
            var remaining = Math.Max(0, OutputLimit - record.Output.Length);
            record.Output += text[..Math.Min(text.Length, remaining)];
            if (text.Length > remaining)
            {
                IsTruncated = true;
                record.Output += TruncationNotice;
                onTruncated?.Invoke();
            }
        }
        append(text);
    }
}
