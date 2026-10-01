using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Footprint.Core;

/// <summary>Reads legacy text and stores lossless, verified GZip output in JSON.</summary>
public sealed class CompressedOutputConverter : JsonConverter<string>
{
    public override bool HandleNull => true;
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String) return reader.GetString()!;
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("出力データが不正です。");
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        try
        {
            if (root.GetProperty("encoding").GetString() != "gzip-base64-v1")
                throw new JsonException("未対応の出力圧縮形式です。");
            var length = root.GetProperty("length").GetInt32();
            if (length < 0) throw new JsonException("出力の文字数が不正です。");
            var bytes = Convert.FromBase64String(root.GetProperty("data").GetString()!);
            using var source = new MemoryStream(bytes);
            using var gzip = new GZipStream(source, CompressionMode.Decompress);
            using var textReader = new StreamReader(gzip, Utf8, detectEncodingFromByteOrderMarks: false);
            var text = new StringBuilder();
            var buffer = new char[8192];
            int count;
            while ((count = textReader.Read(buffer, 0, buffer.Length)) > 0)
            {
                if ((long)text.Length + count > length) throw new JsonException("解凍した出力の文字数が一致しません。");
                text.Append(buffer, 0, count);
            }
            if (text.Length != length) throw new JsonException("圧縮出力が途中で切れています。");
            var output = text.ToString();
            var hash = Convert.ToHexString(SHA256.HashData(Utf8.GetBytes(output)));
            if (!hash.Equals(root.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase))
                throw new JsonException("圧縮出力の検証に失敗しました。");
            return output;
        }
        catch (Exception error) when (error is IOException or FormatException or KeyNotFoundException or
            InvalidOperationException or DecoderFallbackException or ArgumentException)
        {
            throw new JsonException("圧縮出力を読み込めません。", error);
        }
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        if (value is null) throw new JsonException("出力がnullです。");
        var bytes = Utf8.GetBytes(value);
        using var destination = new MemoryStream();
        using (var gzip = new GZipStream(destination, CompressionLevel.Optimal, leaveOpen: true)) gzip.Write(bytes);
        writer.WriteStartObject();
        writer.WriteString("encoding", "gzip-base64-v1");
        writer.WriteNumber("length", value.Length);
        writer.WriteString("sha256", Convert.ToHexString(SHA256.HashData(bytes)));
        writer.WriteBase64String("data", destination.ToArray());
        writer.WriteEndObject();
    }
}
