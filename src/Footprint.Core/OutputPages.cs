using System.Text;

namespace Footprint.Core;

/// <summary>Keeps full output apart from the small text presented by the UI.</summary>
public sealed class OutputPages
{
    public const int PageSize = 50_000;
    private readonly StringBuilder _text = new();
    public int Length => _text.Length;
    public int PageCount
    {
        get
        {
            var count = Math.Max(1, (int)(((long)Length + PageSize - 1) / PageSize));
            return count > 1 && Boundary((count - 1) * PageSize) == Length ? count - 1 : count;
        }
    }
    public string FullText => _text.ToString();
    public void Reset(string text) { _text.Clear(); _text.Append(text); }
    public void Append(string text) => _text.Append(text);

    public string GetPage(int index)
    {
        if (index < 0 || index >= PageCount) throw new ArgumentOutOfRangeException(nameof(index));
        var start = Boundary(index * PageSize);
        var end = Boundary((int)Math.Min((long)(index + 1) * PageSize, Length));
        return _text.ToString(start, end - start);
    }

    private int Boundary(int index)
    {
        // Keep surrogate pairs and Windows line endings on the preceding page.
        if (index > 0 && index < Length &&
            ((char.IsHighSurrogate(_text[index - 1]) && char.IsLowSurrogate(_text[index])) ||
             (_text[index - 1] == '\r' && _text[index] == '\n'))) return index + 1;
        return index;
    }
}
