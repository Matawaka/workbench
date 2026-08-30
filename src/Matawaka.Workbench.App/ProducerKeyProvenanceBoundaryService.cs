using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record ProducerKeyProvenanceBoundaryAuthorityReceipt(
    string Schema,
    string Subject,
    string Operation,
    string MainRepositoryRoot,
    string AuthoritySource,
    bool ExplicitUiConfirmationRequired,
    bool ClosureReceiptReadAllowed,
    bool PublicKeyVerificationAllowed,
    bool InMemoryNegativeVerificationAllowed,
    bool SigningAllowed,
    bool PrivateKeyAccessAllowed,
    bool SourceMutationAllowed,
    bool BuildAllowed,
    bool CheckpointAllowed,
    bool NetworkAccessAllowed,
    bool CatalogMutationAllowed,
    bool AgentExecuteAllowed,
    IReadOnlyList<string> AllowedEffects,
    IReadOnlyList<string> NonEffects);

public sealed record ProducerKeyProvenanceClaimFixture(
    string Schema,
    string Version,
    string ClaimedRole,
    string AcceptedWorkbenchCommit,
    string AcceptedWorkbenchTag,
    string ClosureReceiptSha256,
    string ClosureEvidenceEnvelopeDigest);

public sealed record ProducerKeyProvenanceBoundaryReceipt(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    bool Passed,
    string Status,
    string MainRepositoryRoot,
    string MainHeadBefore,
    IReadOnlyList<string> MainTagsBefore,
    IReadOnlyList<string> MainDirtyPathsBefore,
    string MainHeadAfter,
    IReadOnlyList<string> MainTagsAfter,
    IReadOnlyList<string> MainDirtyPathsAfter,
    bool MainRepositoryUnchanged,
    bool ExplicitUiConfirmationRequired,
    bool ExplicitUiConfirmationObserved,
    string SourceClosureArtifactPath,
    string SourceClosureArtifactSha256,
    string SourceClosureEvidenceEnvelopeDigest,
    bool SourceClosureVerified,
    string ClaimSchema,
    string ClaimVersion,
    string ClaimSha256,
    string ClaimCanonicalUtf8,
    string SignatureAlgorithm,
    string PublicKeyFingerprintSha256,
    string DetachedSignatureSha256,
    bool DetachedSignatureVerified,
    bool ClaimByteDriftRefused,
    bool SignatureByteDriftRefused,
    bool PublicKeySubstitutionRefused,
    bool ExactClaimToClosureBindingVerified,
    bool KeyPossessionFixtureDemonstrated,
    bool PrivateKeyMaterialLoadedByBoundary,
    bool SigningOperationAttempted,
    bool ProducerIdentityProven,
    bool ProducerAuthenticationProven,
    bool TrustAnchorEstablished,
    bool CertificateChainValidated,
    bool TrustedTimestampValidated,
    bool CrossMachinePortabilityProven,
    bool CrossOsPortabilityProven,
    bool AuthorityExpansionDetected,
    ProducerKeyProvenanceBoundaryAuthorityReceipt Authority,
    bool SourceMutationAuthorized,
    bool BuildAuthorized,
    bool CheckpointAuthorized,
    bool NetworkAccessAuthorized,
    bool CatalogMutationAuthorized,
    bool AgentExecuteAuthorized,
    bool StableCorePromotionAuthorized,
    IReadOnlyList<string> NonEffects,
    string Note);

/// <summary>
/// Post-acceptance fixture demonstrating only a detached-signature key-possession
/// verification boundary over the exact accepted v0.28 transport evidence closure.
///
/// The fixture private key is not present in Workbench. The boundary imports only
/// public-key material and verifies one fixed detached signature plus three in-memory
/// negative controls. Successful verification proves neither real-world producer
/// identity nor a trust anchor, certificate chain, timestamp authority, or action
/// authority.
/// </summary>
public sealed class ProducerKeyProvenanceBoundaryService
{
    public const string Version = "0.29.0";
    public const string ReceiptSchema = "matawaka.workbench-producer-key-provenance-boundary/v0.29";
    public const string AuthoritySchema = "matawaka.workbench-producer-key-provenance-boundary-authority/v0.29";

    private const string ExpectedTag = "workbench-v0.29-accepted";
    private const string ExpectedV028Head = "c60ce4280f8c9d0bdad773bb581c22ba244cf08d";
    private const string ExpectedV028Tag = "workbench-v0.28-accepted";
    private const string ExpectedClosureSchema = "matawaka.workbench-recovery-transport-adversarial-evidence-closure/v0.28";
    private const string ExpectedClosureSha256 = "ddc96a76bee5b6615d101b3f7e8b45847e1f0f5f9eb796730498f982cfe9aa3a";
    private const string ExpectedClosureEnvelopeDigest = "f96045702c4fc9ae369a4b92ed4a312563be4f8f6210fcf7934a50fd9c2702c4";
    private const string ExpectedClaimSha256 = "94ddcb67ee4e3ac3cfd3fa5cc2e0af24ca46975b3f50516de66889d60282eaba";
    private const string ExpectedPublicKeyFingerprintSha256 = "1048a67242e8d24db9fb900ae1d54275710831623b0ad30c811030a2bb86c734";
    private const string ExpectedDetachedSignatureSha256 = "0123a4f6ed55a8ce9b67d55d736359661204b3d5218f1330ea375009b3a631a0";

    private const string CanonicalClaimJson = "{\"Schema\":\"matawaka.workbench-producer-key-possession-claim-fixture/v0.29\",\"Version\":\"0.29.0\",\"ClaimedRole\":\"fixture-producer-key-holder\",\"AcceptedWorkbenchCommit\":\"c60ce4280f8c9d0bdad773bb581c22ba244cf08d\",\"AcceptedWorkbenchTag\":\"workbench-v0.28-accepted\",\"ClosureReceiptSha256\":\"ddc96a76bee5b6615d101b3f7e8b45847e1f0f5f9eb796730498f982cfe9aa3a\",\"ClosureEvidenceEnvelopeDigest\":\"f96045702c4fc9ae369a4b92ed4a312563be4f8f6210fcf7934a50fd9c2702c4\"}";

    private const string DetachedSignatureBase64 = "HKPcc+BVeWJWUHu5S7O11TWSSA+4dC2qL4E2KRXm5ejQu1FPh06wO8X+NU9m+LR8gXI90nsnm+S8kl99Iy2PQixGTXthVjwhyR4Nic2pLRVFxXPjROSj3bvDgk+erxvcJ0DABXzLJFB7qku66jeTNtGKOaLdwcY4rv9s18uufNv4uaHaSYTXpZ26nhr6HHwGKuOkZRk/7NttnWfVxeE4ztasubFhdke2kuO7p7qWzibCEZBxfuvLMlMxLZuYCtFRpRET/Q1bFgai8No27ZMQqmRVebqTePwz/12InIowKUHV/M1W/3WmjuaQ08xXhCQf7926YJb2+5MTMD1RDvx9Ww==";

    private const string PublicKeyPem =
        "-----BEGIN PUBLIC KEY-----\n" +
        "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAm9R4CtNkymHMOB0if2BC\n" +
        "i1hcTBj6CuXxdGeteU4/yPvoDTL7OO0LyctoBtalKobLkgFLP7mBxLqsC7LtBqdK\n" +
        "9v/7j5EoF9tbn9Mt8wq6Ms4AXE6auGkpKqMwJIQ9Qoy/XdJ6mkiLLmGctyXSiYTI\n" +
        "BqreJru9HK1osAKmsXa93HBeTbMsAFU+iYjG3Ke/dScMmD3hdtQz+gyDVUgLD5yA\n" +
        "xipl9159/6H6F6EB5hXvJu9h7Pej5BH+m4tmKsNFgRRKu6rdSQQdJfVZEwdSFgxy\n" +
        "ySlgGH8TbcCbpMKi30C/Iax37rYU6aZn3n24TfoFztWAAMlqCd07wmSQUlnIQ1XR\n" +
        "fQIDAQAB\n" +
        "-----END PUBLIC KEY-----\n";

    private const string SubstitutionPublicKeyPem =
        "-----BEGIN PUBLIC KEY-----\n" +
        "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA5LV/TsX0PhJTaTLUBjhV\n" +
        "/l/PsJY4FdfVBX7Gy7HR4/Mz64K5BYPH24sAkF2cGEvTQhLSbjqwZ7eW2BwqNyhg\n" +
        "HJ0dt7USJKsj/33atYhRGXhuwxz8TY0nAxhMfaHWf7EOt/IIDZX333cyzvihj8U8\n" +
        "6sr3z0BHYfg58j0G2YZH6djJkfsy5s5HLsVq55pUxBptbf6jRbsgHvqsyXyBYZhI\n" +
        "bWEecvbCMtvAXGWRaCF1ZeC0KYRJ+xwt2P4ijAedFhKEbA2e9KJ0JKnZ1t0nk/WT\n" +
        "PhiWwBXjLa0HOEX8cwCO5WRqhgJ+Vc090xFyoo7/TRJ5C0G291J1Fu2tfPBnGA0F\n" +
        "wQIDAQAB\n" +
        "-----END PUBLIC KEY-----\n";

    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(20);
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task<(ProducerKeyProvenanceBoundaryReceipt Receipt, string ArtifactPath)> VerifyAsync(
        string workspaceRoot,
        bool explicitUiConfirmation,
        CancellationToken cancellationToken)
    {
        if (!explicitUiConfirmation)
            throw new InvalidDataException("Producer-key provenance boundary requires explicit UI confirmation.");

        var repositoryRoot = ResolveRepositoryRoot(workspaceRoot);
        var before = await ObserveGitStateAsync(repositoryRoot, cancellationToken).ConfigureAwait(false);
        RequireAcceptedV029(before);

        var closurePath = FindExactClosureArtifact(repositoryRoot);
        var closureBytes = await File.ReadAllBytesAsync(closurePath, cancellationToken).ConfigureAwait(false);
        var closureSha = HashBytes(closureBytes);
        if (!string.Equals(closureSha, ExpectedClosureSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The retained v0.28 closure bytes no longer match the exact observed closure receipt.");

        var closure = JsonSerializer.Deserialize<RecoveryTransportAdversarialEvidenceClosureReceipt>(closureBytes, JsonOptions)
            ?? throw new InvalidDataException("The exact retained v0.28 closure receipt could not be parsed.");
        VerifyClosure(closure);

        var claimBytes = Utf8NoBom.GetBytes(CanonicalClaimJson);
        var claimSha = HashBytes(claimBytes);
        if (!string.Equals(claimSha, ExpectedClaimSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Embedded v0.29 canonical claim bytes drifted.");

        var claim = JsonSerializer.Deserialize<ProducerKeyProvenanceClaimFixture>(claimBytes, JsonOptions)
            ?? throw new InvalidDataException("Embedded v0.29 canonical claim could not be parsed.");

        var exactClaimBinding =
            string.Equals(claim.Schema, "matawaka.workbench-producer-key-possession-claim-fixture/v0.29", StringComparison.Ordinal) &&
            string.Equals(claim.Version, Version, StringComparison.Ordinal) &&
            string.Equals(claim.ClaimedRole, "fixture-producer-key-holder", StringComparison.Ordinal) &&
            string.Equals(claim.AcceptedWorkbenchCommit, ExpectedV028Head, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(claim.AcceptedWorkbenchTag, ExpectedV028Tag, StringComparison.Ordinal) &&
            string.Equals(claim.ClosureReceiptSha256, closureSha, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(claim.ClosureEvidenceEnvelopeDigest, closure.EvidenceEnvelopeDigest, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(claim.ClosureEvidenceEnvelopeDigest, ExpectedClosureEnvelopeDigest, StringComparison.OrdinalIgnoreCase);

        if (!exactClaimBinding)
            throw new InvalidDataException("The canonical v0.29 claim is not bound to the exact accepted v0.28 closure frontier.");

        var signatureBytes = Convert.FromBase64String(DetachedSignatureBase64);
        var signatureSha = HashBytes(signatureBytes);
        if (!string.Equals(signatureSha, ExpectedDetachedSignatureSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Embedded detached signature bytes drifted.");

        bool signatureVerified;
        bool claimDriftRefused;
        bool signatureDriftRefused;
        bool keySubstitutionRefused;
        string publicKeyFingerprint;

        using (var rsa = RSA.Create())
        {
            rsa.ImportFromPem(PublicKeyPem);
            publicKeyFingerprint = HashBytes(rsa.ExportSubjectPublicKeyInfo());
            if (!string.Equals(publicKeyFingerprint, ExpectedPublicKeyFingerprintSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Embedded v0.29 public key fingerprint drifted.");

            signatureVerified = rsa.VerifyData(
                claimBytes,
                signatureBytes,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            var driftedClaim = new byte[claimBytes.Length + 1];
            Buffer.BlockCopy(claimBytes, 0, driftedClaim, 0, claimBytes.Length);
            driftedClaim[^1] = (byte)' ';
            claimDriftRefused = !rsa.VerifyData(
                driftedClaim,
                signatureBytes,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            var driftedSignature = signatureBytes.ToArray();
            driftedSignature[^1] ^= 0x01;
            signatureDriftRefused = !rsa.VerifyData(
                claimBytes,
                driftedSignature,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
        }

        using (var substitution = RSA.Create())
        {
            substitution.ImportFromPem(SubstitutionPublicKeyPem);
            keySubstitutionRefused = !substitution.VerifyData(
                claimBytes,
                signatureBytes,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
        }

        if (!signatureVerified || !claimDriftRefused || !signatureDriftRefused || !keySubstitutionRefused)
            throw new InvalidDataException("Detached-signature fixture or one of its exact negative controls did not preserve the v0.29 key-possession boundary.");

        var nonEffects = new[]
        {
            "no private key material is embedded, loaded, requested, generated, imported, or persisted by Workbench",
            "no signing operation",
            "no mutation of the v0.28 closure receipt",
            "no transport ZIP copy, mutation, inspection, import, or materialization",
            "no certificate-store access or certificate-chain validation",
            "no trust-anchor establishment",
            "no trusted timestamp validation",
            "no producer identity or producer authentication claim",
            "no recovery execution or rollback",
            "no source mutation",
            "no dotnet restore/build/test/publish",
            "no git add/commit/tag",
            "no git fetch or push",
            "no remote creation/update",
            "no network access",
            "no Matawaka catalog repository mutation",
            "no Agent Execute authority",
            "no ActionPermit creation",
            "no cross-machine or cross-OS portability claim",
            "no canonical UU-AAP conformance claim",
            "no Stable Core or interface-registry promotion",
            "receipt write is limited to Workbench/artifacts/producer-key-provenance-boundaries"
        };

        var authority = new ProducerKeyProvenanceBoundaryAuthorityReceipt(
            AuthoritySchema,
            "human-operator-at-workbench-ui",
            "workbench.maintenance.fixture-key-possession-provenance-boundary",
            repositoryRoot,
            "explicit Key provenance button + confirmation dialog after v0.29 accepted",
            true,
            true,
            true,
            true,
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
                "read and hash the one exact retained v0.28 transport evidence closure receipt",
                "verify one fixed detached RSA-SHA256 signature over one fixed canonical fixture claim",
                "run three in-memory negative verification controls: claim drift, signature drift, public-key substitution",
                "write one bounded provenance-boundary receipt under the fixed Workbench artifact root"
            },
            nonEffects);

        var after = await ObserveGitStateAsync(repositoryRoot, cancellationToken).ConfigureAwait(false);
        var mainUnchanged = GitStatesEqual(before, after);
        if (!mainUnchanged)
            throw new InvalidDataException("Main Workbench Git state changed during the v0.29 producer-key provenance boundary.");

        var demonstrated =
            signatureVerified &&
            claimDriftRefused &&
            signatureDriftRefused &&
            keySubstitutionRefused &&
            exactClaimBinding &&
            mainUnchanged;

        var receipt = new ProducerKeyProvenanceBoundaryReceipt(
            ReceiptSchema,
            Version,
            DateTimeOffset.Now,
            demonstrated,
            demonstrated
                ? "VERIFIED_DETACHED_KEY_POSSESSION_FIXTURE_IDENTITY_UNPROVEN"
                : "KEY_POSSESSION_FIXTURE_VERIFICATION_FAILED",
            repositoryRoot,
            before.Head,
            before.Tags,
            before.DirtyPaths,
            after.Head,
            after.Tags,
            after.DirtyPaths,
            mainUnchanged,
            true,
            explicitUiConfirmation,
            closurePath,
            closureSha,
            closure.EvidenceEnvelopeDigest,
            true,
            claim.Schema,
            claim.Version,
            claimSha,
            CanonicalClaimJson,
            "RSA-2048-PKCS1-v1_5-SHA256",
            publicKeyFingerprint,
            signatureSha,
            signatureVerified,
            claimDriftRefused,
            signatureDriftRefused,
            keySubstitutionRefused,
            exactClaimBinding,
            demonstrated,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            authority,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            nonEffects,
            "v0.29 demonstrates only that one fixed detached signature verifies under one fixed public fixture key for exact claim bytes bound to the exact accepted v0.28 closure, and that claim/signature/key substitution drift is refused. The private key is absent from Workbench. This is not real-world producer identity, producer authentication, a trust anchor, certificate-chain validation, trusted timestamping, portability proof, action authority, canonical UU-AAP conformance, or Stable Core promotion.");

        var artifactDir = Path.Combine(repositoryRoot, "artifacts", "producer-key-provenance-boundaries");
        Directory.CreateDirectory(artifactDir);
        var artifactPath = Path.Combine(
            artifactDir,
            $"producer-key-provenance-boundary-v0.29-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(
            artifactPath,
            JsonSerializer.Serialize(receipt, JsonOptions),
            Utf8NoBom,
            cancellationToken).ConfigureAwait(false);
        return (receipt, artifactPath);
    }

    private static void VerifyClosure(RecoveryTransportAdversarialEvidenceClosureReceipt closure)
    {
        if (!string.Equals(closure.Schema, ExpectedClosureSchema, StringComparison.Ordinal) ||
            !string.Equals(closure.Version, "0.28.0", StringComparison.Ordinal) ||
            !closure.Closed ||
            !string.Equals(closure.Status, "CLOSED_BYTE_BOUND_TRANSPORT_ADVERSARIAL_EVIDENCE_ENVELOPE", StringComparison.Ordinal) ||
            !closure.MainRepositoryUnchanged ||
            !string.Equals(closure.MainHeadBefore, ExpectedV028Head, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(closure.MainHeadAfter, ExpectedV028Head, StringComparison.OrdinalIgnoreCase) ||
            !closure.MainTagsAfter.Contains(ExpectedV028Tag, StringComparer.Ordinal) ||
            closure.MainDirtyPathsBefore.Count != 0 ||
            closure.MainDirtyPathsAfter.Count != 0 ||
            !closure.ExplicitUiConfirmationRequired ||
            !closure.ExplicitUiConfirmationObserved ||
            !closure.PositiveIndependenceReceiptVerified ||
            !closure.AdversarialControlMatrixVerified ||
            !closure.MatrixToPositiveByteBindingVerified ||
            !closure.CommonSourceTransportBindingVerified ||
            !closure.AllAdversarialControlsRefusedBeforeEvidenceMaterialization ||
            !closure.PositiveNegativeEvidencePairClosed ||
            !closure.AuthorityLimitationsPreserved ||
            closure.AuthorityExpansionDetected ||
            !string.Equals(closure.EvidenceEnvelopeDigest, ExpectedClosureEnvelopeDigest, StringComparison.OrdinalIgnoreCase) ||
            closure.ProducerAuthenticationProven ||
            closure.CrossMachinePortabilityProven ||
            closure.CrossOsPortabilityProven ||
            closure.ProductionMainRepositoryRecoveryProven ||
            closure.GeneralFailureRecoveryClaimAllowed ||
            closure.AutomaticRecoveryAuthorized ||
            closure.RecoveryExecutionAuthorized ||
            closure.RollbackAuthorized ||
            closure.DeletionAuthorized ||
            closure.SourceMutationAuthorized ||
            closure.BuildAuthorized ||
            closure.CheckpointAuthorized ||
            closure.NetworkAccessAuthorized ||
            closure.CatalogMutationAuthorized ||
            closure.AgentExecuteAuthorized ||
            closure.StableCorePromotionAuthorized)
            throw new InvalidDataException("The retained v0.28 transport evidence closure does not match the exact bounded predecessor contract.");
    }

    private static string FindExactClosureArtifact(string repositoryRoot)
    {
        var root = Path.GetFullPath(Path.Combine(
            repositoryRoot,
            "artifacts",
            "recovery-transport-adversarial-evidence-closures"));
        if (!Directory.Exists(root))
            throw new InvalidDataException("No retained v0.28 transport evidence closure artifact directory exists.");

        var rootPrefix = root + Path.DirectorySeparatorChar;
        var matches = new List<string>();
        foreach (var file in Directory.GetFiles(
                     root,
                     "recovery-transport-adversarial-evidence-closure-v0.28-*.json",
                     SearchOption.TopDirectoryOnly))
        {
            try
            {
                var full = Path.GetFullPath(file);
                if (!full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                var bytes = File.ReadAllBytes(full);
                if (string.Equals(HashBytes(bytes), ExpectedClosureSha256, StringComparison.OrdinalIgnoreCase))
                    matches.Add(full);
            }
            catch
            {
                // Unreadable retained evidence cannot support the provenance boundary.
            }
        }

        if (matches.Count == 0)
            throw new InvalidDataException("The exact observed v0.28 closure receipt is not retained.");
        if (matches.Count != 1)
            throw new InvalidDataException("More than one file carries the exact observed v0.28 closure bytes; refusing ambiguous fixture evidence path selection.");
        return matches[0];
    }

    private static string ResolveRepositoryRoot(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
            throw new InvalidDataException("Workspace root is required.");
        var root = Path.GetFullPath(Path.Combine(workspaceRoot.Trim(), "Workbench"));
        if (!Directory.Exists(Path.Combine(root, ".git")))
            throw new InvalidDataException($"Workbench Git repository missing: {root}");
        return root;
    }

    private static void RequireAcceptedV029(GitState state)
    {
        if (state.DirtyPaths.Count != 0)
            throw new InvalidDataException("Producer-key provenance boundary requires a clean accepted main Workbench repository.");
        if (!state.Tags.Contains(ExpectedTag, StringComparer.Ordinal))
            throw new InvalidDataException($"Producer-key provenance boundary is enabled only after {ExpectedTag} points at the current HEAD.");
    }

    private static string HashBytes(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed record GitState(
        string Head,
        IReadOnlyList<string> Tags,
        IReadOnlyList<string> DirtyPaths);

    private static async Task<GitState> ObserveGitStateAsync(
        string repositoryRoot,
        CancellationToken cancellationToken)
    {
        var head = (await RunGitReadOnlyAsync(
            repositoryRoot,
            cancellationToken,
            "rev-parse",
            "HEAD").ConfigureAwait(false)).Trim();
        var tags = SplitLines(await RunGitReadOnlyAsync(
            repositoryRoot,
            cancellationToken,
            "tag",
            "--points-at",
            "HEAD").ConfigureAwait(false));
        var status = await RunGitReadOnlyAsync(
            repositoryRoot,
            cancellationToken,
            "status",
            "--porcelain=v1",
            "--untracked-files=all").ConfigureAwait(false);
        return new GitState(head, tags, ParseStatusPaths(status));
    }

    private static bool GitStatesEqual(GitState left, GitState right)
        => string.Equals(left.Head, right.Head, StringComparison.OrdinalIgnoreCase) &&
           left.Tags.SequenceEqual(right.Tags, StringComparer.Ordinal) &&
           left.DirtyPaths.SequenceEqual(right.DirtyPaths, StringComparer.Ordinal);

    private static IReadOnlyList<string> ParseStatusPaths(string output)
    {
        var paths = new List<string>();
        foreach (var raw in output.Replace("\r\n", "\n", StringComparison.Ordinal)
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (raw.Length < 4)
                throw new InvalidDataException($"Unexpected git status porcelain line in v0.29 producer-key provenance boundary: {raw}");
            var path = raw[3..];
            var arrow = path.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrow >= 0)
                path = path[(arrow + 4)..];
            paths.Add(path.Trim('"').Replace('\\', '/').TrimStart('/'));
        }

        return paths
            .OrderBy(x => x, StringComparer.Ordinal)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> SplitLines(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

    private static async Task<string> RunGitReadOnlyAsync(
        string repositoryRoot,
        CancellationToken cancellationToken,
        params string[] args)
    {
        if (args.Length == 0 ||
            !new[] { "rev-parse", "tag", "status" }.Contains(args[0], StringComparer.Ordinal))
            throw new InvalidDataException("Only fixed read-only Git operations are permitted in v0.29 producer-key provenance boundary.");

        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = repositoryRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["GIT_PAGER"] = "cat";
        psi.Environment["PAGER"] = "cat";
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };
        if (!process.Start())
            throw new InvalidDataException("Failed to start fixed read-only Git process for v0.29 producer-key provenance boundary.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(GitTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("Fixed read-only Git operation timed out in v0.29 producer-key provenance boundary.");
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidDataException($"Fixed read-only Git operation failed in v0.29 producer-key provenance boundary: {stderr.Trim()}");
        return stdout;
    }
}