using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.JevLab;

internal static class StrictJson
{
    internal const int MaximumBytes = 1_048_576;
    internal static JsonDocument Parse(byte[] bytes)
    {
        Require(bytes.Length <= MaximumBytes, "FILE_TOO_LARGE");
        ReadOnlyMemory<byte> jsonBytes = bytes;
        if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) jsonBytes = jsonBytes[3..];
        var document = JsonDocument.Parse(jsonBytes, new JsonDocumentOptions { MaxDepth = 16 });
        try { Inspect(document.RootElement); return document; }
        catch { document.Dispose(); throw; }
    }

    private static void Inspect(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in value.EnumerateObject())
            {
                Require(seen.Add(p.Name), "DUPLICATE_JSON_KEY");
                ValidUnicode(p.Name);
                Inspect(p.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) Inspect(item);
        else if (value.ValueKind == JsonValueKind.String) ValidUnicode(value.GetString()!);
        else if (value.ValueKind == JsonValueKind.Number)
            Require(value.TryGetDouble(out var n) && double.IsFinite(n), "NON_FINITE_NUMBER");
    }

    internal static void Keys(JsonElement value, params string[] expected)
    {
        Require(value.ValueKind == JsonValueKind.Object, "OBJECT_REQUIRED");
        var actual = value.EnumerateObject().Select(x => x.Name).ToArray();
        Require(actual.Length == expected.Length && expected.All(x => actual.Contains(x, StringComparer.Ordinal)),
            "UNKNOWN_OR_MISSING_FIELD");
    }
    internal static string Text(JsonElement value)
    {
        Require(value.ValueKind == JsonValueKind.String, "TEXT_REQUIRED");
        var text = value.GetString()!;
        Require(!string.IsNullOrWhiteSpace(text), "NONEMPTY_TEXT_REQUIRED");
        return text;
    }
    internal static void Equal(JsonElement value, string expected, string code = "VALUE_MISMATCH") =>
        Require(Text(value) == expected, code);
    internal static void Flag(JsonElement value, bool expected) =>
        Require(value.ValueKind == (expected ? JsonValueKind.True : JsonValueKind.False), "BOUNDARY_FLAG_INVALID");
    internal static string HashText(JsonElement value)
    {
        var text = Text(value);
        Require(text.Length == 71 && text.StartsWith("sha256:", StringComparison.Ordinal) &&
            text.AsSpan(7).ToArray().All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'), "INVALID_SHA256");
        return text;
    }
    internal static string RawHash(byte[] bytes) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
    internal static string CanonicalHash(JsonElement value) => RawHash(Encoding.UTF8.GetBytes(Canonical(value)));

    // This is deliberately NOT a general JSON numeric canonicalizer. The strictly accepted historical
    // candidate/request schema contains no numbers or integer-index object keys. Reject other domains.
    // JS JSON.stringify string escaping is reproduced explicitly, including raw U+2028/U+2029 and emoji.
    internal static string Canonical(JsonElement value)
    {
        var buffer = new StringBuilder();
        WriteCanonical(buffer, value);
        return buffer.ToString();
    }
    private static void WriteCanonical(StringBuilder buffer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                buffer.Append('{');
                var first = true;
                foreach (var property in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    Require(!uint.TryParse(property.Name, NumberStyles.None, CultureInfo.InvariantCulture, out _),
                        "UNSUPPORTED_CANONICAL_INTEGER_KEY");
                    if (!first) buffer.Append(',');
                    first = false;
                    WriteString(buffer, property.Name);
                    buffer.Append(':');
                    WriteCanonical(buffer, property.Value);
                }
                buffer.Append('}');
                break;
            case JsonValueKind.Array:
                buffer.Append('[');
                var initial = true;
                foreach (var item in value.EnumerateArray())
                {
                    if (!initial) buffer.Append(',');
                    initial = false;
                    WriteCanonical(buffer, item);
                }
                buffer.Append(']');
                break;
            case JsonValueKind.String: WriteString(buffer, value.GetString()!); break;
            case JsonValueKind.True: buffer.Append("true"); break;
            case JsonValueKind.False: buffer.Append("false"); break;
            case JsonValueKind.Null: buffer.Append("null"); break;
            default: throw new InvalidDataException("UNSUPPORTED_CANONICAL_NUMBER");
        }
    }
    private static void WriteString(StringBuilder buffer, string value)
    {
        ValidUnicode(value);
        buffer.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': buffer.Append("\\\""); break;
                case '\\': buffer.Append("\\\\"); break;
                case '\b': buffer.Append("\\b"); break;
                case '\f': buffer.Append("\\f"); break;
                case '\n': buffer.Append("\\n"); break;
                case '\r': buffer.Append("\\r"); break;
                case '\t': buffer.Append("\\t"); break;
                default:
                    if (c < 0x20) buffer.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else buffer.Append(c);
                    break;
            }
        }
        buffer.Append('"');
    }
    private static void ValidUnicode(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (!char.IsSurrogate(text[i])) continue;
            Require(char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]),
                "INVALID_UNICODE_SURROGATE");
            i++;
        }
    }
    internal static double Number(JsonElement value, double min, double max)
    {
        Require(value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out _), "NUMBER_REQUIRED");
        var number = value.GetDouble();
        Require(double.IsFinite(number) && number >= min && number <= max, "NUMBER_OUT_OF_RANGE");
        return number;
    }
    internal static void Require(bool condition, string code)
    {
        if (!condition) throw new InvalidDataException(code);
    }
}
