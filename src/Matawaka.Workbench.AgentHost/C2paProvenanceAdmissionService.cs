using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Matawaka.Workbench.Protocol;

namespace Matawaka.Workbench.AgentHost;

/// <summary>
/// Pure data-admission boundary for one exact accepted C2PA provenance evidence
/// frontier. This service does not retrieve evidence, invoke c2patool, launch a
/// process, access the network, mutate files, or mint any execution authority.
/// </summary>
public static class C2paProvenanceAdmissionService
{
    public const string ReceiptSchema = "matawaka.provenance-observation-admission-receipt/v0.56";
    public const string Decision = "PROVENANCE_OBSERVED_NO_AUTHORITY";

    private const string EvidenceRepository = "Matawaka/uu-aap";
    private const string EvidenceFrontier = "0e74f89695bbcb02c759000752696c322d908f7a";
    private const string EvidencePath = "scripts/c2pa-workbench-release-observation-binding/v0.1/qualification-receipt.json";
    private const string EvidenceGitBlob = "4ae93b36d5c8a6c2f00cbc53840221834e09ee26";
    private const string EvidenceSha256 = "ba2284c66ae4a48583a0918a1c7d4d6a96cf83e66c632b7d55a4aa0ec4d0b9c5";
    private const int EvidenceBytes = 2415;
    private const string EvidenceSchema = "urn:uu-aap:c2pa-workbench-release-observation-binding-qualification:0.1";
    private const string EvidenceVerdict = "WORKBENCH_V0552_PUBLIC_RELEASE_OBSERVATION_HASH_BOUND_BY_STANDARD_C2PA_EXTERNAL_REFERENCE";
    private const string WorkbenchReleaseCommit = "ea852feeb0e8d92a8977bb251693e7e977913dca";
    private const string AssertionLabel = "c2pa.external-reference";
    private const string DigestAlgorithm = "sha256";
    private const string MediaType = "application/json";

    public static ProvenanceAdmissionProfile AcceptedWorkbenchV0552Profile { get; } = new(
        "matawaka.provenance-admission-profile/v0.56",
        "workbench-v0.55.2/c2pa-release-observation/uu-aap-955",
        new ProvenanceEvidenceBinding(
            EvidenceRepository,
            EvidenceFrontier,
            EvidencePath,
            EvidenceGitBlob,
            EvidenceSha256,
            EvidenceBytes,
            EvidenceSchema,
            EvidenceVerdict),
        AssertionLabel,
        DigestAlgorithm,
        MediaType,
        WorkbenchReleaseCommit,
        Decision);

    public static ProvenanceAdmissionReceipt AdmitAcceptedWorkbenchV0552(ReadOnlySpan<byte> evidenceBytes)
    {
        var bytes = evidenceBytes.ToArray();
        var profile = AcceptedWorkbenchV0552Profile;

        if (bytes.Length != profile.Evidence.Bytes)
            throw new InvalidDataException($"Provenance evidence byte count mismatch: expected={profile.Evidence.Bytes} actual={bytes.Length}.");

        var observedSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(observedSha256, profile.Evidence.Sha256, StringComparison.Ordinal))
            throw new InvalidDataException("Provenance evidence SHA-256 mismatch.");

        var observedGitBlob = ComputeGitBlobSha1(bytes);
        if (!string.Equals(observedGitBlob, profile.Evidence.GitBlobSha1, StringComparison.Ordinal))
            throw new InvalidDataException("Provenance evidence Git blob identity mismatch.");

        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        RequireObject(root, "evidence root");
        RequireExactProperties(root, "evidence root",
            "schema", "tracking_issue", "repository_predecessor_main", "repository_predecessor_tree",
            "source_observation", "c2pa_binding", "claims", "automatic_action",
            "external_mutation_performed", "verdict");

        RequireString(root, "schema", profile.Evidence.EvidenceSchema);
        RequireInt32(root, "tracking_issue", 954);
        RequireString(root, "verdict", profile.Evidence.EvidenceVerdict);
        RequireBoolean(root, "automatic_action", false);
        RequireBoolean(root, "external_mutation_performed", false);

        var source = RequireObjectProperty(root, "source_observation");
        RequireExactProperties(source, "source_observation",
            "checkpoint_commit", "path", "git_blob", "sha256", "bytes", "schema", "verdict",
            "workbench_main_commit", "accepted_tag_object", "accepted_tag_target",
            "annotated_tag_signature_verified");
        RequireString(source, "workbench_main_commit", profile.ExpectedWorkbenchReleaseCommit);
        RequireString(source, "accepted_tag_target", profile.ExpectedWorkbenchReleaseCommit);
        RequireBoolean(source, "annotated_tag_signature_verified", false);

        var binding = RequireObjectProperty(root, "c2pa_binding");
        RequireExactProperties(binding, "c2pa_binding",
            "assertion_label", "digest_alg", "media_type", "external_url",
            "external_reference_hash_match", "immutable_external_resolution_exact_bytes_match",
            "live_c2pa_validation_accepted", "custom_assertion_namespace_registered",
            "source_observation_embedded_as_custom_assertion");
        RequireString(binding, "assertion_label", profile.ExpectedAssertionLabel);
        RequireString(binding, "digest_alg", profile.ExpectedDigestAlgorithm);
        RequireString(binding, "media_type", profile.ExpectedMediaType);
        RequireBoolean(binding, "external_reference_hash_match", true);
        RequireBoolean(binding, "immutable_external_resolution_exact_bytes_match", true);
        RequireBoolean(binding, "live_c2pa_validation_accepted", true);
        RequireBoolean(binding, "custom_assertion_namespace_registered", false);
        RequireBoolean(binding, "source_observation_embedded_as_custom_assertion", false);

        var claims = RequireObjectProperty(root, "claims");
        RequireExactProperties(claims, "claims",
            "c2pa_external_reference_binding_established", "source_observation_rewritten",
            "historical_operator_publication_receipt_reconstructed", "publication_authority_proven",
            "truth_certified", "git_tag_signature_verified",
            "c2pa_inclusion_in_original_workbench_release_proven", "model_invocation_authority_created",
            "runtime_execution_authority_created", "response_authority_created",
            "action_permit_created", "successor_permit_created");
        RequireBoolean(claims, "c2pa_external_reference_binding_established", true);
        RequireBoolean(claims, "source_observation_rewritten", false);
        RequireBoolean(claims, "historical_operator_publication_receipt_reconstructed", false);
        RequireBoolean(claims, "publication_authority_proven", false);
        RequireBoolean(claims, "truth_certified", false);
        RequireBoolean(claims, "git_tag_signature_verified", false);
        RequireBoolean(claims, "c2pa_inclusion_in_original_workbench_release_proven", false);
        RequireBoolean(claims, "model_invocation_authority_created", false);
        RequireBoolean(claims, "runtime_execution_authority_created", false);
        RequireBoolean(claims, "response_authority_created", false);
        RequireBoolean(claims, "action_permit_created", false);
        RequireBoolean(claims, "successor_permit_created", false);

        return new ProvenanceAdmissionReceipt(
            ReceiptSchema,
            profile.Id,
            profile.Evidence,
            observedSha256,
            bytes.Length,
            root.GetProperty("schema").GetString()!,
            root.GetProperty("verdict").GetString()!,
            source.GetProperty("workbench_main_commit").GetString()!,
            true,
            true,
            true,
            false,
            profile.Decision,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            new[]
            {
                "C2PA validation does not create Workbench authority",
                "external-reference hash match does not certify truth",
                "provenance observation does not create publication authority",
                "provenance observation does not create runtime or model-request authority",
                "provenance observation does not create response or display authority",
                "provenance observation does not create action or successor permits",
                "unsigned Git tag remains cryptographically unverified",
                "input evidence creates no permission to fetch or refresh evidence",
                "no network access",
                "no process start",
                "no repository or file mutation"
            });
    }

    private static JsonElement RequireObjectProperty(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value))
            throw new InvalidDataException($"Missing required provenance evidence property: {name}.");
        RequireObject(value, name);
        return value;
    }

    private static void RequireObject(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"Provenance evidence {name} must be an object.");
    }

    private static void RequireString(JsonElement parent, string name, string expected)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String ||
            !string.Equals(value.GetString(), expected, StringComparison.Ordinal))
            throw new InvalidDataException($"Unexpected provenance evidence value for {name}.");
    }

    private static void RequireBoolean(JsonElement parent, string name, bool expected)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False) ||
            value.GetBoolean() != expected)
            throw new InvalidDataException($"Unexpected provenance evidence boolean for {name}.");
    }

    private static void RequireInt32(JsonElement parent, string name, int expected)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out var actual) || actual != expected)
            throw new InvalidDataException($"Unexpected provenance evidence integer for {name}.");
    }

    private static void RequireExactProperties(JsonElement element, string label, params string[] expectedNames)
    {
        var expected = new HashSet<string>(expectedNames, StringComparer.Ordinal);
        var actual = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!actual.Add(property.Name))
                throw new InvalidDataException($"Duplicate provenance evidence property in {label}: {property.Name}.");
            if (!expected.Contains(property.Name))
                throw new InvalidDataException($"Unknown provenance evidence property in {label}: {property.Name}.");
        }

        if (actual.Count != expected.Count || expected.Any(name => !actual.Contains(name)))
            throw new InvalidDataException($"Provenance evidence property set mismatch in {label}.");
    }

    private static string ComputeGitBlobSha1(byte[] bytes)
    {
        var prefix = Encoding.ASCII.GetBytes($"blob {bytes.Length}\0");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        hash.AppendData(prefix);
        hash.AppendData(bytes);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
