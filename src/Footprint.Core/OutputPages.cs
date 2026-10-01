using System.Text;

namespace Footprint.Core;

/// <summary>Indexes complete logical lines without splitting them between pages.</summary>
public sealed class OutputPages
{
    public const int LinesPerPage = 500;
    private readonly StringBuilder _text = new();
    private readonly List<int> _pageStarts = [0];
    private readonly List<int> _firstLines = [1];
    private int _completedLines;
    private int _pageLines;
    private bool _pendingPage;
    private bool _previousWasCr;
    private bool _endedWithBreak;

    public int Length => _text.Length;
    public int PageCount => _pageStarts.Count;
    public string FullText => _text.ToString();

    public void Reset(string text)
    {
        _text.Clear();
        _pageStarts.Clear();
        _pageStarts.Add(0);
        _firstLines.Clear();
        _firstLines.Add(1);
        _completedLines = _pageLines = 0;
        _pendingPage = _previousWasCr = _endedWithBreak = false;
        Append(text);
    }

    public void Append(string text)
    {
        var start = Length;
        _text.Append(text);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (_previousWasCr && c == '\n')
            {
                // A CRLF belongs entirely to the preceding line, even across chunks.
                _previousWasCr = false;
                continue;
            }
            if (_pendingPage)
            {
                _pageStarts.Add(start + i);
                _firstLines.Add(_completedLines + 1);
                _pageLines = 0;
                _pendingPage = false;
            }
            _previousWasCr = c == '\r';
            _endedWithBreak = c is '\r' or '\n';
            if (_endedWithBreak)
            {
                _completedLines++;
                _pageLines++;
                _pendingPage = _pageLines == LinesPerPage;
            }
        }
    }

    public string GetPage(int index)
    {
        ValidateIndex(index);
        var start = _pageStarts[index];
        var end = index + 1 < PageCount ? _pageStarts[index + 1] : Length;
        return _text.ToString(start, end - start);
    }

    public (int First, int Last) GetLineRange(int index)
    {
        ValidateIndex(index);
        if (Length == 0) return (0, 0);
        return (_firstLines[index], index + 1 < PageCount ? _firstLines[index + 1] - 1 :
            _completedLines + (_endedWithBreak ? 0 : 1));
    }

    private void ValidateIndex(int index)
    {
        if (index < 0 || index >= PageCount) throw new ArgumentOutOfRangeException(nameof(index));
    }
}
