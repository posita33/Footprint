namespace Footprint.Core;

/// <summary>Accumulates the same bounded output that is displayed and persisted.</summary>
public sealed class CommandOutputCapture(CommandRecord record, Action<string> append)
{
    private const int OutputLimit = 100_000;
    private bool _truncated;

    public void Receive(string text)
    {
        var remaining = OutputLimit - record.Output.Length;
        if (remaining > 0)
        {
            var chunk = text[..Math.Min(text.Length, remaining)];
            record.Output += chunk;
            append(chunk);
        }
        if (text.Length > remaining && !_truncated)
        {
            _truncated = true;
            const string notice = "\n[出力の保存上限に達しました]\n";
            record.Output += notice;
            append(notice);
        }
    }
}
