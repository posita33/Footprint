namespace Footprint.Core;

/// <summary>Accumulates all output efficiently and forwards each live chunk.</summary>
public sealed class CommandOutputCapture(CommandRecord record, Action<string> append)
{
    public const string LegacyTruncationNotice = "\n[出力の保存上限に達しました]\n";

    public void Receive(string text)
    {
        record.AppendOutput(text);
        append(text);
    }
}
