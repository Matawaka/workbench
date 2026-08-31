using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record ProducerKeyRevocationInferenceBoundaryAuthorityReceipt
{
    public string Schema { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string Operation { get; init; } = string.Empty;
    public string MainRepositoryRoot { get; init; } = string.Empty;
    public string AuthoritySource { get; init; } = string.Empty;
    public bool ExplicitUiConfirmationRequired { get; init; }
    public bool SourceContinuityReceiptReadAllowed { get; init; }
    public bool PublicKeyVerificationAllowed { get; init; }
    public bool InMemoryNegativeVerificationAllowed { get; init; }
    public bool LocalPolicyClassificationAllowed { get; init; }
    public bool SigningAllowed { get; init; }
    public bool PrivateKeyAccessAllowed { get; init; }
    public bool RevocationEnforcementAllowed { get; init; }
    public bool KeyRegistryMutationAllowed { get; init; }
    public bool HistoricalEvidenceInvalidationAllowed { get; init; }
    public bool FuturePredecessorPolicyDecisionAllowed { get; init; }
    public bool SourceMutationAllowed { get; init; }
    public bool BuildAllowed { get; init; }
    public bool CheckpointAllowed { get; init; }
    public bool NetworkAccessAllowed { get; init; }
    public bool CatalogMutationAllowed { get; init; }
    public bool AgentExecuteAllowed { get; init; }
    public IReadOnlyList<string> AllowedEffects { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> NonEffects { get; init; } = Array.Empty<string>();
}

public sealed record ProducerKeyRevocationInferenceBoundaryReceipt
{
    public string Schema { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public DateTimeOffset ObservedAt { get; init; }
    public bool Passed { get; init; }
    public string Status { get; init; } = string.Empty;
    public string MainRepositoryRoot { get; init; } = string.Empty;
    public string MainHeadBefore { get; init; } = string.Empty;
    public IReadOnlyList<string> MainTagsBefore { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> MainDirtyPathsBefore { get; init; } = Array.Empty<string>();
    public string MainHeadAfter { get; init; } = string.Empty;
    public IReadOnlyList<string> MainTagsAfter { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> MainDirtyPathsAfter { get; init; } = Array.Empty<string>();
    public bool MainRepositoryUnchanged { get; init; }
    public bool ExplicitUiConfirmationRequired { get; init; }
    public bool ExplicitUiConfirmationObserved { get; init; }

    public string SourceContinuityArtifactPath { get; init; } = string.Empty;
    public string SourceContinuityArtifactSha256 { get; init; } = string.Empty;
    public bool SourceContinuityVerified { get; init; }

    public string RotationClaimSha256 { get; init; } = string.Empty;
    public bool RotationClaimVerified { get; init; }
    public bool RotationClaimExplicitlyDoesNotClaimRevocation { get; init; }
    public string PredecessorPublicKeyFingerprintSha256 { get; init; } = string.Empty;
    public string SuccessorPublicKeyFingerprintSha256 { get; init; } = string.Empty;

    public bool RotationAloneRevocationInferenceRefused { get; init; }
    public bool SuccessorPossessionRevocationInferenceRefused { get; init; }
    public bool OrdinalTrustedTimeInferenceRefused { get; init; }

    public string HistoricalClaimSha256 { get; init; } = string.Empty;
    public string HistoricalSignatureSha256 { get; init; } = string.Empty;
    public bool HistoricalSignatureVerified { get; init; }
    public bool HistoricalClaimByteDriftRefused { get; init; }
    public bool HistoricalPublicKeySubstitutionRefused { get; init; }
    public bool HistoricalEvidencePreserved { get; init; }
    public bool HistoricalEvidenceInvalidated { get; init; }

    public string FuturePredecessorPolicyStatus { get; init; } = string.Empty;
    public bool PredecessorRevocationProven { get; init; }
    public bool TrustedTimestampValidated { get; init; }
    public bool TrustedTemporalOrderingProven { get; init; }
    public bool FuturePredecessorAcceptanceAuthorized { get; init; }
    public bool FuturePredecessorRejectionAuthorized { get; init; }
    public bool RevocationEnforcementAuthorized { get; init; }
    public bool KeyRegistryMutationAuthorized { get; init; }

    public bool PrivateKeyMaterialLoadedByBoundary { get; init; }
    public bool SigningOperationAttempted { get; init; }
    public bool ProducerIdentityProven { get; init; }
    public bool ProducerAuthenticationProven { get; init; }
    public bool CommonControllerProven { get; init; }
    public bool TrustAnchorEstablished { get; init; }
    public bool CertificateChainValidated { get; init; }
    public bool AuthorityExpansionDetected { get; init; }

    public ProducerKeyRevocationInferenceBoundaryAuthorityReceipt Authority { get; init; } = new();

    public bool SourceMutationAuthorized { get; init; }
    public bool BuildAuthorized { get; init; }
    public bool CheckpointAuthorized { get; init; }
    public bool NetworkAccessAuthorized { get; init; }
    public bool CatalogMutationAuthorized { get; init; }
    public bool AgentExecuteAuthorized { get; init; }
    public bool StableCorePromotionAuthorized { get; init; }

    public IReadOnlyList<string> NonEffects { get; init; } = Array.Empty<string>();
    public string Note { get; init; } = string.Empty;
}

/// <summary>
/// Post-acceptance boundary that refuses to infer effective predecessor-key
/// revocation or trusted time from the exact accepted v0.30 rotation-continuity
/// fixture, while independently re-verifying the exact historical v0.29
/// predecessor-key signature.
///
/// Rotation evidence is not revocation evidence. The continued cryptographic
/// verifiability of historical evidence does not authorize future acceptance of
/// the predecessor key. No future acceptance/rejection decision is made here.
/// </summary>
public sealed class ProducerKeyRevocationInferenceBoundaryService
{
    public const string Version = "0.31.0";
    public const string ReceiptSchema = "matawaka.workbench-producer-key-revocation-inference-boundary/v0.31";
    public const string AuthoritySchema = "matawaka.workbench-producer-key-revocation-inference-boundary-authority/v0.31";

    private const string ExpectedTag = "workbench-v0.31-accepted";
    private const string ExpectedV030Head = "1c12f1f51b2a03cf45b2ca792a5e5315b6fc61f3";
    private const string ExpectedV030Tag = "workbench-v0.30-accepted";
    private const string ExpectedV030ContinuitySchema = "matawaka.workbench-producer-key-rotation-continuity-boundary/v0.30";
    private const string ExpectedV030ContinuityReceiptSha256 = "2c4270fc6bf18bf29251d893d3539dcb4afd97e45152b5ec311fa3ce210a2f7d";
    private const string ExpectedPredecessorPublicKeyFingerprintSha256 = "1048a67242e8d24db9fb900ae1d54275710831623b0ad30c811030a2bb86c734";
    private const string ExpectedSuccessorPublicKeyFingerprintSha256 = "ccce3e9dc674eac4633d348f1c19c307b1b55730974875c9e733e24f1a4e53ea";
    private const string ExpectedRotationClaimSha256 = "38fbca126115d9af594e088d9cce626315c8c8dfda679396bb65325d27bfe9c7";
    private const string ExpectedHistoricalClaimSha256 = "94ddcb67ee4e3ac3cfd3fa5cc2e0af24ca46975b3f50516de66889d60282eaba";
    private const string ExpectedHistoricalSignatureSha256 = "0123a4f6ed55a8ce9b67d55d736359661204b3d5218f1330ea375009b3a631a0";

    private const string HistoricalCanonicalClaimJson = "{\"Schema\":\"matawaka.workbench-producer-key-possession-claim-fixture/v0.29\",\"Version\":\"0.29.0\",\"ClaimedRole\":\"fixture-producer-key-holder\",\"AcceptedWorkbenchCommit\":\"c60ce4280f8c9d0bdad773bb581c22ba244cf08d\",\"AcceptedWorkbenchTag\":\"workbench-v0.28-accepted\",\"ClosureReceiptSha256\":\"ddc96a76bee5b6615d101b3f7e8b45847e1f0f5f9eb796730498f982cfe9aa3a\",\"ClosureEvidenceEnvelopeDigest\":\"f96045702c4fc9ae369a4b92ed4a312563be4f8f6210fcf7934a50fd9c2702c4\"}";

    private const string HistoricalDetachedSignatureBase64 = "HKPcc+BVeWJWUHu5S7O11TWSSA+4dC2qL4E2KRXm5ejQu1FPh06wO8X+NU9m+LR8gXI90nsnm+S8kl99Iy2PQixGTXthVjwhyR4Nic2pLRVFxXPjROSj3bvDgk+erxvcJ0DABXzLJFB7qku66jeTNtGKOaLdwcY4rv9s18uufNv4uaHaSYTXpZ26nhr6HHwGKuOkZRk/7NttnWfVxeE4ztasubFhdke2kuO7p7qWzibCEZBxfuvLMlMxLZuYCtFRpRET/Q1bFgai8No27ZMQqmRVebqTePwz/12InIowKUHV/M1W/3WmjuaQ08xXhCQf7926YJb2+5MTMD1RDvx9Ww==";

    private const string PredecessorPublicKeyPem =
        "-----BEGIN PUBLIC KEY-----\n" +
        "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAm9R4CtNkymHMOB0if2BC\n" +
        "i1hcTBj6CuXxdGeteU4/yPvoDTL7OO0LyctoBtalKobLkgFLP7mBxLqsC7LtBqdK\n" +
        "9v/7j5EoF9tbn9Mt8wq6Ms4AXE6auGkpKqMwJIQ9Qoy/XdJ6mkiLLmGctyXSiYTI\n" +
        "BqreJru9HK1osAKmsXa93HBeTbMsAFU+iYjG3Ke/dScMmD3hdtQz+gyDVUgLD5yA\n" +
        "xipl9159/6H6F6EB5hXvJu9h7Pej5BH+m4tmKsNFgRRKu6rdSQQdJfVZEwdSFgxy\n" +
        "ySlgGH8TbcCbpMKi30C/Iax37rYU6aZn3n24TfoFztWAAMlqCd07wmSQUlnIQ1XR\n" +
        "fQIDAQAB\n" +
        "-----END PUBLIC KEY-----\n";

    private const string SuccessorPublicKeyPem =
        "-----BEGIN PUBLIC KEY-----\n" +
        "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAwbWIm5bR/wReHAh51hhf\n" +
        "V8yiFXiSAxA2uvx3r3cp/VoUjwcqOk+svsKZMRM2HFHBqkYqh4nIlTBJS0TQOvnV\n" +
        "0Xn9AdKmLchVd+abkEL8lvNtIqY8Mgc0pH44aPTODQaR6jLB5OrVumZvajm6ykkI\n" +
        "5IT6N2c1UC2L/Ly+GJkYoNDYJagh0FpJiO3Ek9IPko9jL2KxA7kdpl2maUeOTGG2\n" +
        "73eY6X4q3OxyqoSmc6PbWXcYiVJPSg7USnVVy4Uf5ayBfvwvypHgEXfQ1sHE96OD\n" +
        "3NTxCS3k1kJG43wb/B20Ib8P92V/BRpNC0DkZeAxIskT1eBnRuCj/wJC/rfmpIx4\n" +
        "oQIDAQAB\n" +
        "-----END PUBLIC KEY-----\n";

    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(20);
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task<(ProducerKeyRevocationInferenceBoundaryReceipt Receipt, string ArtifactPath)> VerifyAsync(
        string workspaceRoot,
        bool explicitUiConfirmation,
        CancellationToken cancellationToken)
    {
        if (!explicitUiConfirmation)
            throw new InvalidDataException("Revocation inference boundary requires explicit UI confirmation.");

        var repositoryRoot = ResolveRepositoryRoot(workspaceRoot);
        var before = await ObserveGitStateAsync(repositoryRoot, cancellationToken).ConfigureAwait(false);
        RequireAcceptedV031(before);

        var sourcePath = FindExactContinuityArtifact(repositoryRoot);
        var sourceBytes = await File.ReadAllBytesAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        var sourceSha = HashBytes(sourceBytes);
        if (!string.Equals(sourceSha, ExpectedV030ContinuityReceiptSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The retained v0.30 continuity receipt bytes drifted.");

        var source = JsonSerializer.Deserialize<ProducerKeyRotationContinuityBoundaryReceipt>(sourceBytes, JsonOptions)
            ?? throw new InvalidDataException("The exact retained v0.30 continuity receipt could not be parsed.");
        VerifySourceContinuity(source);

        var rotationClaim = JsonSerializer.Deserialize<ProducerKeyRotationClaimFixture>(
            source.RotationClaimCanonicalUtf8,
            JsonOptions) ?? throw new InvalidDataException("The exact v0.30 rotation claim could not be parsed.");

        var rotationClaimVerified =
            string.Equals(HashBytes(Utf8NoBom.GetBytes(source.RotationClaimCanonicalUtf8)), ExpectedRotationClaimSha256, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(source.RotationClaimSha256, ExpectedRotationClaimSha256, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(rotationClaim.Schema, "matawaka.workbench-key-rotation-continuity-claim-fixture/v0.30", StringComparison.Ordinal) &&
            string.Equals(rotationClaim.Version, "0.30.0", StringComparison.Ordinal) &&
            string.Equals(rotationClaim.Relation, "fixture-successor-key-continuity", StringComparison.Ordinal) &&
            string.Equals(rotationClaim.AcceptedWorkbenchCommit, "c45581a0f93be150cd7c1ac88d0d5296fbcc03bf", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(rotationClaim.AcceptedWorkbenchTag, "workbench-v0.29-accepted", StringComparison.Ordinal) &&
            string.Equals(rotationClaim.PredecessorPublicKeyFingerprintSha256, ExpectedPredecessorPublicKeyFingerprintSha256, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(rotationClaim.SuccessorPublicKeyFingerprintSha256, ExpectedSuccessorPublicKeyFingerprintSha256, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(rotationClaim.Ordinal, "successor-1", StringComparison.Ordinal);

        if (!rotationClaimVerified)
            throw new InvalidDataException("The retained v0.30 rotation claim no longer matches its exact bounded fixture.");

        var explicitlyNoRevocationClaim = !rotationClaim.PredecessorRevocationClaimed;
        var rotationInferenceRefused =
            explicitlyNoRevocationClaim &&
            !source.PredecessorRevocationProven &&
            source.PredecessorToSuccessorBindingVerified;

        var successorInferenceRefused =
            source.SuccessorPossessionSignatureVerified &&
            source.SuccessorPossessionBindingVerified &&
            !source.PredecessorRevocationProven;

        var ordinalTrustedTimeInferenceRefused =
            string.Equals(rotationClaim.Ordinal, "successor-1", StringComparison.Ordinal) &&
            !source.TrustedTimestampValidated &&
            !source.TrustedTemporalOrderingProven;

        if (!rotationInferenceRefused || !successorInferenceRefused || !ordinalTrustedTimeInferenceRefused)
            throw new InvalidDataException("v0.31 refused-inference invariants did not hold for the exact v0.30 continuity receipt.");

        var historicalClaimBytes = Utf8NoBom.GetBytes(HistoricalCanonicalClaimJson);
        if (!string.Equals(HashBytes(historicalClaimBytes), ExpectedHistoricalClaimSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Embedded historical v0.29 claim bytes drifted.");

        var historicalSignatureBytes = Convert.FromBase64String(HistoricalDetachedSignatureBase64);
        if (!string.Equals(HashBytes(historicalSignatureBytes), ExpectedHistoricalSignatureSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Embedded historical v0.29 detached signature bytes drifted.");

        bool historicalSignatureVerified;
        bool historicalClaimDriftRefused;
        bool historicalPublicKeySubstitutionRefused;
        string predecessorFingerprint;
        string successorFingerprint;

        using (var predecessor = RSA.Create())
        {
            predecessor.ImportFromPem(PredecessorPublicKeyPem);
            predecessorFingerprint = HashBytes(predecessor.ExportSubjectPublicKeyInfo());
            if (!string.Equals(predecessorFingerprint, ExpectedPredecessorPublicKeyFingerprintSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Embedded predecessor public key fingerprint drifted.");

            historicalSignatureVerified = predecessor.VerifyData(
                historicalClaimBytes,
                historicalSignatureBytes,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            var driftedHistoricalClaim = historicalClaimBytes.ToArray();
            driftedHistoricalClaim[^1] ^= 0x01;
            historicalClaimDriftRefused = !predecessor.VerifyData(
                driftedHistoricalClaim,
                historicalSignatureBytes,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
        }

        using (var successor = RSA.Create())
        {
            successor.ImportFromPem(SuccessorPublicKeyPem);
            successorFingerprint = HashBytes(successor.ExportSubjectPublicKeyInfo());
            if (!string.Equals(successorFingerprint, ExpectedSuccessorPublicKeyFingerprintSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Embedded successor public key fingerprint drifted.");

            historicalPublicKeySubstitutionRefused = !successor.VerifyData(
                historicalClaimBytes,
                historicalSignatureBytes,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
        }

        var historicalEvidencePreserved =
            historicalSignatureVerified &&
            historicalClaimDriftRefused &&
            historicalPublicKeySubstitutionRefused &&
            string.Equals(source.PredecessorPublicKeyFingerprintSha256, predecessorFingerprint, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(source.SuccessorPublicKeyFingerprintSha256, successorFingerprint, StringComparison.OrdinalIgnoreCase);

        if (!historicalEvidencePreserved)
            throw new InvalidDataException("Historical v0.29 predecessor-key evidence was not preserved as exact cryptographic evidence.");

        const string futurePolicyStatus = "UNRESOLVED_NO_REVOCATION_OR_TRUSTED_TIME_POLICY_AUTHORITY";
        var futureAcceptanceAuthorized = false;
        var futureRejectionAuthorized = false;
        var revocationEnforcementAuthorized = false;
        var keyRegistryMutationAuthorized = false;

        var nonEffects = new[]
        {
            "no private key material is embedded, loaded, requested, generated, imported, or persisted by Workbench",
            "no signing operation",
            "no predecessor key revocation operation or revocation enforcement",
            "no successor key activation or key registry mutation",
            "no certificate-store access or certificate-chain validation",
            "no trust-anchor establishment",
            "no trusted timestamp or trusted temporal-ordering claim",
            "no producer identity, producer authentication, or common-controller claim",
            "no future predecessor-key acceptance or rejection decision",
            "no historical evidence deletion, mutation, invalidation, or ontological erasure",
            "no transport inspection/import/materialization",
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
            "receipt write is limited to Workbench/artifacts/key-revocation-inference-boundaries"
        };

        var authority = new ProducerKeyRevocationInferenceBoundaryAuthorityReceipt
        {
            Schema = AuthoritySchema,
            Subject = "human-operator-at-workbench-ui",
            Operation = "workbench.maintenance.revocation-inference-refusal-and-historical-evidence-preservation",
            MainRepositoryRoot = repositoryRoot,
            AuthoritySource = "explicit Revocation boundary button + confirmation dialog after v0.31 accepted",
            ExplicitUiConfirmationRequired = true,
            SourceContinuityReceiptReadAllowed = true,
            PublicKeyVerificationAllowed = true,
            InMemoryNegativeVerificationAllowed = true,
            LocalPolicyClassificationAllowed = true,
            SigningAllowed = false,
            PrivateKeyAccessAllowed = false,
            RevocationEnforcementAllowed = false,
            KeyRegistryMutationAllowed = false,
            HistoricalEvidenceInvalidationAllowed = false,
            FuturePredecessorPolicyDecisionAllowed = false,
            SourceMutationAllowed = false,
            BuildAllowed = false,
            CheckpointAllowed = false,
            NetworkAccessAllowed = false,
            CatalogMutationAllowed = false,
            AgentExecuteAllowed = false,
            AllowedEffects = new[]
            {
                "read and hash the one exact retained v0.30 key-continuity receipt",
                "parse the exact retained v0.30 rotation claim and refuse unsupported revocation/trusted-time inference",
                "re-verify the exact historical v0.29 predecessor-key detached signature",
                "run in-memory historical claim-drift and successor-key-substitution negative controls",
                "classify future predecessor-key policy as unresolved because no policy authority is present",
                "write one bounded revocation-inference receipt under the fixed Workbench artifact root"
            },
            NonEffects = nonEffects
        };

        var after = await ObserveGitStateAsync(repositoryRoot, cancellationToken).ConfigureAwait(false);
        var mainUnchanged = GitStatesEqual(before, after);
        if (!mainUnchanged)
            throw new InvalidDataException("Main Workbench Git state changed during v0.31 revocation inference boundary.");

        var passed =
            rotationClaimVerified &&
            explicitlyNoRevocationClaim &&
            rotationInferenceRefused &&
            successorInferenceRefused &&
            ordinalTrustedTimeInferenceRefused &&
            historicalEvidencePreserved &&
            mainUnchanged;

        var receipt = new ProducerKeyRevocationInferenceBoundaryReceipt
        {
            Schema = ReceiptSchema,
            Version = Version,
            ObservedAt = DateTimeOffset.Now,
            Passed = passed,
            Status = passed
                ? "REFUSED_REVOCATION_INFERENCE_PRESERVED_HISTORICAL_EVIDENCE_FUTURE_POLICY_UNRESOLVED"
                : "FAILED_REVOCATION_INFERENCE_BOUNDARY",
            MainRepositoryRoot = repositoryRoot,
            MainHeadBefore = before.Head,
            MainTagsBefore = before.Tags,
            MainDirtyPathsBefore = before.DirtyPaths,
            MainHeadAfter = after.Head,
            MainTagsAfter = after.Tags,
            MainDirtyPathsAfter = after.DirtyPaths,
            MainRepositoryUnchanged = mainUnchanged,
            ExplicitUiConfirmationRequired = true,
            ExplicitUiConfirmationObserved = explicitUiConfirmation,

            SourceContinuityArtifactPath = sourcePath,
            SourceContinuityArtifactSha256 = sourceSha,
            SourceContinuityVerified = true,

            RotationClaimSha256 = source.RotationClaimSha256,
            RotationClaimVerified = rotationClaimVerified,
            RotationClaimExplicitlyDoesNotClaimRevocation = explicitlyNoRevocationClaim,
            PredecessorPublicKeyFingerprintSha256 = predecessorFingerprint,
            SuccessorPublicKeyFingerprintSha256 = successorFingerprint,

            RotationAloneRevocationInferenceRefused = rotationInferenceRefused,
            SuccessorPossessionRevocationInferenceRefused = successorInferenceRefused,
            OrdinalTrustedTimeInferenceRefused = ordinalTrustedTimeInferenceRefused,

            HistoricalClaimSha256 = ExpectedHistoricalClaimSha256,
            HistoricalSignatureSha256 = ExpectedHistoricalSignatureSha256,
            HistoricalSignatureVerified = historicalSignatureVerified,
            HistoricalClaimByteDriftRefused = historicalClaimDriftRefused,
            HistoricalPublicKeySubstitutionRefused = historicalPublicKeySubstitutionRefused,
            HistoricalEvidencePreserved = historicalEvidencePreserved,
            HistoricalEvidenceInvalidated = false,

            FuturePredecessorPolicyStatus = futurePolicyStatus,
            PredecessorRevocationProven = false,
            TrustedTimestampValidated = false,
            TrustedTemporalOrderingProven = false,
            FuturePredecessorAcceptanceAuthorized = futureAcceptanceAuthorized,
            FuturePredecessorRejectionAuthorized = futureRejectionAuthorized,
            RevocationEnforcementAuthorized = revocationEnforcementAuthorized,
            KeyRegistryMutationAuthorized = keyRegistryMutationAuthorized,

            PrivateKeyMaterialLoadedByBoundary = false,
            SigningOperationAttempted = false,
            ProducerIdentityProven = false,
            ProducerAuthenticationProven = false,
            CommonControllerProven = false,
            TrustAnchorEstablished = false,
            CertificateChainValidated = false,
            AuthorityExpansionDetected = false,

            Authority = authority,

            SourceMutationAuthorized = false,
            BuildAuthorized = false,
            CheckpointAuthorized = false,
            NetworkAccessAuthorized = false,
            CatalogMutationAuthorized = false,
            AgentExecuteAuthorized = false,
            StableCorePromotionAuthorized = false,

            NonEffects = nonEffects,
            Note = "v0.31 refuses to infer predecessor-key revocation or trusted time from the exact accepted v0.30 rotation-continuity fixture, re-verifies and preserves exact historical v0.29 predecessor-key evidence, and deliberately leaves future predecessor-key acceptance/rejection policy unresolved. This is not revocation enforcement, trust establishment, historical evidence erasure, producer identity/authentication, portability proof, canonical UU-AAP conformance, or Stable Core promotion."
        };

        var artifactDir = Path.Combine(repositoryRoot, "artifacts", "key-revocation-inference-boundaries");
        Directory.CreateDirectory(artifactDir);
        var artifactPath = Path.Combine(
            artifactDir,
            $"key-revocation-inference-boundary-v0.31-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");

        await File.WriteAllTextAsync(
            artifactPath,
            JsonSerializer.Serialize(receipt, JsonOptions),
            Utf8NoBom,
            cancellationToken).ConfigureAwait(false);

        return (receipt, artifactPath);
    }

    private static void VerifySourceContinuity(ProducerKeyRotationContinuityBoundaryReceipt source)
    {
        if (!string.Equals(source.Schema, ExpectedV030ContinuitySchema, StringComparison.Ordinal) ||
            !string.Equals(source.Version, "0.30.0", StringComparison.Ordinal) ||
            !source.Passed ||
            !string.Equals(source.Status, "VERIFIED_FIXTURE_KEY_ROTATION_CONTINUITY_IDENTITY_TRUST_AUTHORITY_UNPROVEN", StringComparison.Ordinal) ||
            !source.MainRepositoryUnchanged ||
            !string.Equals(source.MainHeadBefore, ExpectedV030Head, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(source.MainHeadAfter, ExpectedV030Head, StringComparison.OrdinalIgnoreCase) ||
            !source.MainTagsAfter.Contains(ExpectedV030Tag, StringComparer.Ordinal) ||
            source.MainDirtyPathsBefore.Count != 0 ||
            source.MainDirtyPathsAfter.Count != 0 ||
            !source.ExplicitUiConfirmationRequired ||
            !source.ExplicitUiConfirmationObserved ||
            !source.SourceProvenanceVerified ||
            !source.PredecessorRotationSignatureVerified ||
            !source.SuccessorPossessionSignatureVerified ||
            !source.PredecessorSourceBindingVerified ||
            !source.PredecessorToSuccessorBindingVerified ||
            !source.SuccessorPossessionBindingVerified ||
            !source.RotationClaimByteDriftRefused ||
            !source.PredecessorSignatureByteDriftRefused ||
            !source.SuccessorPossessionClaimByteDriftRefused ||
            !source.SuccessorSignatureByteDriftRefused ||
            !source.SuccessorPublicKeySubstitutionRefused ||
            !source.KeyRotationContinuityFixtureDemonstrated ||
            source.PrivateKeyMaterialLoadedByBoundary ||
            source.SigningOperationAttempted ||
            source.ProducerIdentityProven ||
            source.ProducerAuthenticationProven ||
            source.CommonControllerProven ||
            source.TrustAnchorEstablished ||
            source.CertificateChainValidated ||
            source.TrustedTimestampValidated ||
            source.TrustedTemporalOrderingProven ||
            source.PredecessorRevocationProven ||
            source.DelegationAuthorityGranted ||
            source.SuccessorOperationalAuthorityGranted ||
            source.CrossMachinePortabilityProven ||
            source.CrossOsPortabilityProven ||
            source.AuthorityExpansionDetected ||
            !string.Equals(source.PredecessorPublicKeyFingerprintSha256, ExpectedPredecessorPublicKeyFingerprintSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(source.SuccessorPublicKeyFingerprintSha256, ExpectedSuccessorPublicKeyFingerprintSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(source.RotationClaimSha256, ExpectedRotationClaimSha256, StringComparison.OrdinalIgnoreCase) ||
            source.SourceMutationAuthorized ||
            source.BuildAuthorized ||
            source.CheckpointAuthorized ||
            source.NetworkAccessAuthorized ||
            source.CatalogMutationAuthorized ||
            source.AgentExecuteAuthorized ||
            source.StableCorePromotionAuthorized)
            throw new InvalidDataException("Retained v0.30 key-continuity receipt does not match the exact bounded predecessor contract.");
    }

    private static string FindExactContinuityArtifact(string repositoryRoot)
    {
        var root = Path.GetFullPath(Path.Combine(repositoryRoot, "artifacts", "key-rotation-continuity-boundaries"));
        if (!Directory.Exists(root))
            throw new InvalidDataException("No retained v0.30 key-continuity artifact directory exists.");

        var rootPrefix = root + Path.DirectorySeparatorChar;
        var matches = new List<string>();

        foreach (var file in Directory.GetFiles(
                     root,
                     "key-rotation-continuity-boundary-v0.30-*.json",
                     SearchOption.TopDirectoryOnly))
        {
            try
            {
                var full = Path.GetFullPath(file);
                if (!full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                var sha = HashBytes(File.ReadAllBytes(full));
                if (string.Equals(sha, ExpectedV030ContinuityReceiptSha256, StringComparison.OrdinalIgnoreCase))
                    matches.Add(full);
            }
            catch
            {
                // Unreadable retained evidence cannot support the boundary.
            }
        }

        if (matches.Count == 0)
            throw new InvalidDataException("The exact retained v0.30 key-continuity receipt is not available.");
        if (matches.Count != 1)
            throw new InvalidDataException("More than one exact v0.30 key-continuity artifact is available; refusing ambiguous selection.");

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

    private static void RequireAcceptedV031(GitState state)
    {
        if (state.DirtyPaths.Count != 0)
            throw new InvalidDataException("Revocation inference boundary requires a clean accepted main Workbench repository.");
        if (!state.Tags.Contains(ExpectedTag, StringComparer.Ordinal))
            throw new InvalidDataException($"Revocation inference boundary is enabled only after {ExpectedTag} points at current HEAD.");
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
                throw new InvalidDataException($"Unexpected git status porcelain line in v0.31 revocation inference boundary: {raw}");

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
            throw new InvalidDataException("Only fixed read-only Git operations are permitted in v0.31 revocation inference boundary.");

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
            throw new InvalidDataException("Failed to start fixed read-only Git process for v0.31 revocation inference boundary.");

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
            throw new TimeoutException("Fixed read-only Git operation timed out in v0.31 revocation inference boundary.");
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
            throw new InvalidDataException($"Fixed read-only Git operation failed in v0.31 revocation inference boundary: {stderr.Trim()}");

        return stdout;
    }
}
