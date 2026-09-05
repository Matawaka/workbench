using System.Text.Json;

namespace Matawaka.Workbench.App;

public static class LocalModelInvocationRequestV055Parser
{
    private static readonly HashSet<string> ExpectedProperties = new(StringComparer.Ordinal)
    {
        "Schema",
        "RequestId",
        "RuntimeTreeManifestPath",
        "RuntimeTreeManifestSha256",
        "ExecutableRelativePath",
        "ExpectedExecutableSha256",
        "ModelAcquisitionReceiptPath",
        "ModelAcquisitionReceiptSha256",
        "ModelArtifactId",
        "ExpectedModelSha256",
        "InvocationProfileId",
        "RequestUtf8",
        "MaxRequestBytes",
        "MaxStdoutBytes",
        "MaxStderrBytes",
        "MaxOutputChars",
        "MaxOutputTokens",
        "TimeoutSeconds",
        "TtlSeconds"
    };

    public static LocalModelInvocationRequestV055 ParseExact(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("v0.55 model invocation request JSON is empty.");

        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 32
        });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("v0.55 model invocation request must be one JSON object.");

        var names = document.RootElement.EnumerateObject().Select(x => x.Name).ToArray();
        if (names.Length != ExpectedProperties.Count || names.Distinct(StringComparer.Ordinal).Count() != names.Length)
            throw new InvalidDataException("v0.55 model invocation request has missing or duplicate properties.");
        var unknown = names.Where(x => !ExpectedProperties.Contains(x)).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (unknown.Length != 0)
            throw new InvalidDataException("v0.55 model invocation request contains unknown properties: " + string.Join(", ", unknown));
        var missing = ExpectedProperties.Where(x => !names.Contains(x, StringComparer.Ordinal)).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (missing.Length != 0)
            throw new InvalidDataException("v0.55 model invocation request is missing properties: " + string.Join(", ", missing));

        return JsonSerializer.Deserialize<LocalModelInvocationRequestV055>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false
        }) ?? throw new InvalidDataException("v0.55 model invocation request deserialized to null.");
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks()
    {
        var valid = """
        {
          "Schema":"matawaka.local-model-invocation-request/v0.55",
          "RequestId":"fixture",
          "RuntimeTreeManifestPath":"C:\\fixture\\runtime.json",
          "RuntimeTreeManifestSha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
          "ExecutableRelativePath":"fixture.exe",
          "ExpectedExecutableSha256":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
          "ModelAcquisitionReceiptPath":"C:\\fixture\\receipt.json",
          "ModelAcquisitionReceiptSha256":"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
          "ModelArtifactId":"model",
          "ExpectedModelSha256":"dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd",
          "InvocationProfileId":"FIXTURE_STDIO_V1",
          "RequestUtf8":"hello",
          "MaxRequestBytes":32,
          "MaxStdoutBytes":1024,
          "MaxStderrBytes":1024,
          "MaxOutputChars":256,
          "MaxOutputTokens":32,
          "TimeoutSeconds":5,
          "TtlSeconds":30
        }
        """;
        var parsed = ParseExact(valid);
        var unknownRefused = Refused(valid.Replace("\"TtlSeconds\":30", "\"TtlSeconds\":30,\"Unexpected\":true", StringComparison.Ordinal));
        var duplicateRefused = Refused(valid.Replace("\"TtlSeconds\":30", "\"TtlSeconds\":30,\"TtlSeconds\":30", StringComparison.Ordinal));
        var missingJson = valid.Replace("\"RequestUtf8\":\"hello\",", string.Empty, StringComparison.Ordinal);
        var missingRefused = !string.Equals(missingJson, valid, StringComparison.Ordinal) && Refused(missingJson);
        return new[]
        {
            ("v055-request-parser-valid", parsed.RequestId == "fixture", parsed.RequestId, "fixture"),
            ("v055-request-parser-unknown", unknownRefused, unknownRefused.ToString(), "True"),
            ("v055-request-parser-duplicate", duplicateRefused, duplicateRefused.ToString(), "True"),
            ("v055-request-parser-missing", missingRefused, missingRefused.ToString(), "True")
        };
    }

    private static bool Refused(string json)
    {
        try { _ = ParseExact(json); return false; }
        catch (Exception ex) when (ex is JsonException or InvalidDataException) { return true; }
    }
}
