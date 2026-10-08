using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UEUtraceAnalyzer;

public sealed class GitHubTokenStore
{
    private readonly string _path;
    public GitHubTokenStore(string? path = null) => _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UEUtraceAnalyzer", "GitHubSettings.json");

    public string Load()
    {
        if (!File.Exists(_path)) return "";
        using var document = JsonDocument.Parse(File.ReadAllText(_path));
        var encrypted = document.RootElement.GetProperty("ProtectedToken").GetString()
            ?? throw new InvalidDataException("保存済みトークンの形式が不正です。");
        var bytes = ProtectedData.Unprotect(Convert.FromBase64String(encrypted), null, DataProtectionScope.CurrentUser);
        try { return Encoding.UTF8.GetString(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public void Save(string token)
    {
        token = token.Trim();
        if (token.Length == 0) { Delete(); return; }
        if (token.Any(char.IsWhiteSpace)) throw new ArgumentException("トークンに空白は使用できません。");
        var bytes = Encoding.UTF8.GetBytes(token);
        byte[] encrypted;
        try { encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
        var fullPath = Path.GetFullPath(_path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new { ProtectedToken = Convert.ToBase64String(encrypted) }));
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void Delete() => File.Delete(_path);
}
