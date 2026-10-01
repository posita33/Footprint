using System.Text;

namespace Footprint.Core;

public static class CommandFormatter
{
    public static bool TryFormat(string command, ShellKind shell, out string formatted, out string reason)
    {
        formatted = command;
        reason = "";
        if (command.IndexOfAny(['\r', '\n']) >= 0)
        {
            reason = "すでに複数行です。整形は単一のコマンドに対応しています。";
            return false;
        }
        var tokens = new List<string>();
        var token = new StringBuilder();
        char quote = '\0';
        for (var i = 0; i < command.Length; i++)
        {
            var c = command[i];
            if (quote != '\0')
            {
                token.Append(c);
                if (shell == ShellKind.PowerShell && quote == '"' && c == '`' && i + 1 < command.Length)
                    token.Append(command[++i]);
                else if (c == quote) quote = '\0';
                continue;
            }
            if (c == '"' || (shell == ShellKind.PowerShell && c == '\''))
            {
                quote = c;
                token.Append(c);
            }
            else if ("|&;<>()[{}]`^%!?\u0023".Contains(c))
            {
                reason = "パイプ・演算子・展開・継続記号を含むコマンドは安全に整形できないため変更しません。表示の折り返しをご利用ください。";
                return false;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (token.Length > 0) { tokens.Add(token.ToString()); token.Clear(); }
            }
            else token.Append(c);
        }
        if (quote != '\0')
        {
            reason = "引用符が閉じられていないため変更しません。";
            return false;
        }
        if (token.Length > 0) tokens.Add(token.ToString());
        if (tokens.Count < 2)
        {
            reason = "改行できる引数がありません。";
            return false;
        }
        var lines = new List<string> { tokens[0] };
        for (var i = 1; i < tokens.Count; i++)
        {
            var item = tokens[i];
            if (IsOption(item, shell) && !item.Contains('=') &&
                !(shell == ShellKind.PowerShell && item.Contains(':')) && i + 1 < tokens.Count &&
                (!IsOption(tokens[i + 1], shell) || double.TryParse(tokens[i + 1],
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _)))
                item += " " + tokens[++i];
            lines.Add(item);
        }
        formatted = string.Join(shell == ShellKind.CommandPrompt ? " ^\r\n" : " `\r\n", lines);
        return true;
    }

    private static bool IsOption(string token, ShellKind shell) => token.Length > 1 &&
        (token[0] == '-' || (shell == ShellKind.CommandPrompt && token[0] == '/'));
}
