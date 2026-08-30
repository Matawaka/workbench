using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record ProducerKeyRotationContinuityAuthorityReceipt(
    string Schema,
    string Subject,
    string Operation,
    string MainRepositoryRoot,
    string AuthoritySource,
    bool ExplicitUiConfirmationRequired,
    bool SourceProvenanceReceiptReadAllowed,
    bool PublicKeyVerificationAllowed,
    bool InMemoryNegativeVerificationAllowed,
    bool SigningAllowed,
    bool PrivateKeyAccessAllowed,
    bool KeyRevocationMutationAllowed,
    bool TrustAnchorMutationAllowed,
    bool SourceMutationAllowed,
    bool BuildAllowed,
    bool CheckpointAllowed,
    bool NetworkAccessAllowed,
    bool CatalogMutationAllowed,
    bool AgentExecuteAllowed,
    IReadOnlyList<string> AllowedEffects,
    IReadOnlyList<string> NonEffects);

public sealed record ProducerKeyRotationClaimFixture(
    string Schema,
    string Version,
    string Relation,
    string AcceptedWorkbenchCommit,
    string AcceptedWorkbenchTag,
    string SourceProvenanceReceiptSha256,
    string PredecessorPublicKeyFingerprintSha256,
    string SuccessorPublicKeyFingerprintSha256,
    string Ordinal,
    bool PredecessorRevocationClaimed);

public sealed record ProducerSuccessorPossessionClaimFixture(
    string Schema,
    string Version,
    string Role,
    string RotationClaimSha256,
    string SuccessorPublicKeyFingerprintSha256);

public sealed record ProducerKeyRotationContinuityBoundaryReceipt(
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
    string SourceProvenanceArtifactPath,
    string SourceProvenanceArtifactSha256,
    bool SourceProvenanceVerified,
    string PredecessorPublicKeyFingerprintSha256,
    string SuccessorPublicKeyFingerprintSha256,
    string RotationClaimSchema,
    string RotationClaimVersion,
    string RotationClaimCanonicalUtf8,
    string RotationClaimSha256,
    string PredecessorRotationSignatureSha256,
    bool PredecessorRotationSignatureVerified,
    string SuccessorPossessionClaimSchema,
    string SuccessorPossessionClaimVersion,
    string SuccessorPossessionClaimCanonicalUtf8,
    string SuccessorPossessionClaimSha256,
    string SuccessorPossessionSignatureSha256,
    bool SuccessorPossessionSignatureVerified,
    bool PredecessorSourceBindingVerified,
    bool PredecessorToSuccessorBindingVerified,
    bool SuccessorPossessionBindingVerified,
    bool RotationClaimByteDriftRefused,
    bool PredecessorSignatureByteDriftRefused,
    bool SuccessorPossessionClaimByteDriftRefused,
    bool SuccessorSignatureByteDriftRefused,
    bool SuccessorPublicKeySubstitutionRefused,
    bool KeyRotationContinuityFixtureDemonstrated,
    bool PrivateKeyMaterialLoadedByBoundary,
    bool SigningOperationAttempted,
    bool ProducerIdentityProven,
    bool ProducerAuthenticationProven,
    bool CommonControllerProven,
    bool TrustAnchorEstablished,
    bool CertificateChainValidated,
    bool TrustedTimestampValidated,
    bool TrustedTemporalOrderingProven,
    bool PredecessorRevocationProven,
    bool DelegationAuthorityGranted,
    bool SuccessorOperationalAuthorityGranted,
    bool CrossMachinePortabilityProven,
    bool CrossOsPortabilityProven,
    bool AuthorityExpansionDetected,
    ProducerKeyRotationContinuityAuthorityReceipt Authority,
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
/// Post-acceptance fixture demonstrating only a cryptographic predecessor-to-
/// successor key relationship over the exact accepted v0.29 provenance receipt.
///
/// The predecessor fixture key signs one exact successor-binding claim and the
/// successor fixture key separately signs one exact possession claim bound to
/// that rotation claim. Workbench contains only public keys and detached
/// signatures. No private key is embedded or loaded and no signing occurs.
///
/// Passing this boundary demonstrates a fixture key-continuity relation only.
/// It does not prove real-world producer identity, common controller, trust,
/// revocation, trusted time ordering, delegation authority, portability,
/// canonical UU-AAP conformance, or Stable Core promotion.
/// </summary>
public sealed class ProducerKeyRotationContinuityBoundaryService
{
    public const string Version = "0.30.0";
    public const string ReceiptSchema = "matawaka.workbench-producer-key-rotation-continuity-boundary/v0.30";
    public const string AuthoritySchema = "matawaka.workbench-producer-key-rotation-continuity-boundary-authority/v0.30";

    private const string ExpectedTag = "workbench-v0.30-accepted";
    private const string ExpectedV029Head = "c45581a0f93be150cd7c1ac88d0d5296fbcc03bf";
    private const string ExpectedV029Tag = "workbench-v0.29-accepted";
    private const string ExpectedV029ProvenanceSchema = "matawaka.workbench-producer-key-provenance-boundary/v0.29";
    private const string ExpectedV029ProvenanceReceiptSha256 = "4a17aebda73c8d24907597449ba95712bd4622228254a040fb89d6f67f06af56";
    private const string ExpectedPredecessorPublicKeyFingerprintSha256 = "1048a67242e8d24db9fb900ae1d54275710831623b0ad30c811030a2bb86c734";
    private const string ExpectedSuccessorPublicKeyFingerprintSha256 = "ccce3e9dc674eac4633d348f1c19c307b1b55730974875c9e733e24f1a4e53ea";
    private const string ExpectedRotationClaimSha256 = "38fbca126115d9af594e088d9cce626315c8c8dfda679396bb65325d27bfe9c7";
    private const string ExpectedPredecessorRotationSignatureSha256 = "e052acb3ccc6a320d7341c16f7cf8066981527e7d4657519b4317806b550397b";
    private const string ExpectedSuccessorPossessionClaimSha256 = "de4aa4a3ffb8eb7da7c12db0a0caebab0e777769a84616b43dd3388449d521ba";
    private const string ExpectedSuccessorPossessionSignatureSha256 = "fe2c2eab3313528f320827dcf732c79f588df92bfe526ac15f5423810632b3d3";

    private const string CanonicalRotationClaimJson = "{\"Schema\":\"matawaka.workbench-key-rotation-continuity-claim-fixture/v0.30\",\"Version\":\"0.30.0\",\"Relation\":\"fixture-successor-key-continuity\",\"AcceptedWorkbenchCommit\":\"c45581a0f93be150cd7c1ac88d0d5296fbcc03bf\",\"AcceptedWorkbenchTag\":\"workbench-v0.29-accepted\",\"SourceProvenanceReceiptSha256\":\"4a17aebda73c8d24907597449ba95712bd4622228254a040fb89d6f67f06af56\",\"PredecessorPublicKeyFingerprintSha256\":\"1048a67242e8d24db9fb900ae1d54275710831623b0ad30c811030a2bb86c734\",\"SuccessorPublicKeyFingerprintSha256\":\"ccce3e9dc674eac4633d348f1c19c307b1b55730974875c9e733e24f1a4e53ea\",\"Ordinal\":\"successor-1\",\"PredecessorRevocationClaimed\":false}";

    private const string CanonicalSuccessorPossessionClaimJson = "{\"Schema\":\"matawaka.workbench-successor-key-possession-claim-fixture/v0.30\",\"Version\":\"0.30.0\",\"Role\":\"fixture-successor-key-holder\",\"RotationClaimSha256\":\"38fbca126115d9af594e088d9cce626315c8c8dfda679396bb65325d27bfe9c7\",\"SuccessorPublicKeyFingerprintSha256\":\"ccce3e9dc674eac4633d348f1c19c307b1b55730974875c9e733e24f1a4e53ea\"}";

    private const string PredecessorRotationSignatureBase64 = "JbJ1FosH5kXc6EnN9mdnOnvnZV7T5flXaWWKp3/m2gyx5qP5+scPGXsYbrRCRy4yFBs+uy07sxxjM8/w7JE+Axnc4L12nKzyFd1unZy5jvNq4BIMJi7Ql2+eV0dEAPAdZifsMQYXvwQaUsoGJau+6MdLlMqZP4ZXSE6RYabicdz6AuDtsX1FX4+rdOMsoT1sLrT5xetDTrs7WjCtmlCUdM9Di9YxBsLTtvRUY73pHuVB/V0vVEw8kyW8/0HyleeYGAmhSjXMJMGP+Mz1RNQCcLHlFhNfsEW4E06O0ei7LygJa7M+wz9oGHk90+6/SsxjXyfL6p0Wp6Av17rkayPjZg==";
    private const string SuccessorPossessionSignatureBase64 = "gzPLI0BSOCuTHK+R6qKJ8eTl8JBUQAHwiHhiIshdtmI618in51NsLjaVN87hQVD9UmH8qYYf4k0FGIcDnOry7J11E2o5f3L+jBhsis/HM5cDrEm+/BO3onadywHlh8uyS+J4K8hnDQiwE+xbA1YLmsyRVt2YrUqSxQEh5CxrAr9jz+iyoMZJoHB7JX52FJhqdXsQanvN6Wa5+ClMDcf3e1hxXofAxA9A+gHkbi3W8C+ZvVQrHmOrR+y/bNTTiOZboMZXNBUvKS8FiMlW9osc+t1DUoIqbNxK9Obu7aSxpm9z30z6wQhpd8bH26sBwWflqxOQDgY+zCMIBXDKyVg0ZQ==";

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

    private const string SubstitutionPublicKeyPem =
        "-----BEGIN PUBLIC KEY-----\n" +
        "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAmY9If7RfQ0hei+KjmHE3\n" +
        "AVdMhcJoinDtB7MHwsYtjCBpvA29/Lp0ero3ycaCPNnIQzomXUb1/4UM0pGUBiWR\n" +
        "x/5ar/gAt0opMbkqPWVMNPBy7lUmiXPh0Q0pPocMYMU+qelAYzn2nButOsBbg3Un\n" +
        "osPtOY8hgMlm56agsRDRbnetmNCKmGl05mE8BZ9OBN8sI3Kl2scHupHDk8MqkRUE\n" +
        "CYODcWqWb1NWjs0pEhihCCjNIyA+MpalQ9sVB4H6zzHRYATaesZIbeg9Oo8eN2Vk\n" +
        "t4YzN9+H3u3PvbBtF7dPouGSNSL1HncA4bkdf4MEG8/ZAeTKpZon+FD2B31zth2z\n" +
        "mQIDAQAB\n" +
        "-----END PUBLIC KEY-----\n";

    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(20);
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task<(ProducerKeyRotationContinuityBoundaryReceipt Receipt, string ArtifactPath)> VerifyAsync(
        string workspaceRoot,
        bool explicitUiConfirmation,
        CancellationToken cancellationToken)
    {
        if (!explicitUiConfirmation)
            throw new InvalidDataException("Key rotation continuity boundary requires explicit UI confirmation.");

        var repositoryRoot = ResolveRepositoryRoot(workspaceRoot);
        var before = await ObserveGitStateAsync(repositoryRoot, cancellationToken).ConfigureAwait(false);
        RequireAcceptedV030(before);

        var sourcePath = FindExactSourceProvenanceArtifact(repositoryRoot);
        var sourceBytes = await File.ReadAllBytesAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        var sourceSha = HashBytes(sourceBytes);
        if (!string.Equals(sourceSha, ExpectedV029ProvenanceReceiptSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The retained v0.29 provenance receipt bytes drifted.");

        var source = JsonSerializer.Deserialize<ProducerKeyProvenanceBoundaryReceipt>(sourceBytes, JsonOptions)
            ?? throw new InvalidDataException("The exact retained v0.29 provenance receipt could not be parsed.");
        VerifySourceProvenance(source);

        var rotationBytes = Utf8NoBom.GetBytes(CanonicalRotationClaimJson);
        var rotationSha = HashBytes(rotationBytes);
        if (!string.Equals(rotationSha, ExpectedRotationClaimSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Embedded v0.30 rotation claim bytes drifted.");

        var rotationClaim = JsonSerializer.Deserialize<ProducerKeyRotationClaimFixture>(rotationBytes, JsonOptions)
            ?? throw new InvalidDataException("Embedded v0.30 rotation claim could not be parsed.");

        var successorPossessionBytes = Utf8NoBom.GetBytes(CanonicalSuccessorPossessionClaimJson);
        var successorPossessionSha = HashBytes(successorPossessionBytes);
        if (!string.Equals(successorPossessionSha, ExpectedSuccessorPossessionClaimSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Embedded v0.30 successor possession claim bytes drifted.");

        var successorPossessionClaim = JsonSerializer.Deserialize<ProducerSuccessorPossessionClaimFixture>(
            successorPossessionBytes,
            JsonOptions) ?? throw new InvalidDataException("Embedded v0.30 successor possession claim could not be parsed.");

        var predecessorSignature = Convert.FromBase64String(PredecessorRotationSignatureBase64);
        var successorSignature = Convert.FromBase64String(SuccessorPossessionSignatureBase64);

        if (!string.Equals(HashBytes(predecessorSignature), ExpectedPredecessorRotationSignatureSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Embedded predecessor rotation signature bytes drifted.");
        if (!string.Equals(HashBytes(successorSignature), ExpectedSuccessorPossessionSignatureSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Embedded successor possession signature bytes drifted.");

        string predecessorFingerprint;
        string successorFingerprint;
        bool predecessorSignatureVerified;
        bool successorSignatureVerified;
        bool rotationClaimDriftRefused;
        bool predecessorSignatureDriftRefused;
        bool successorClaimDriftRefused;
        bool successorSignatureDriftRefused;
        bool successorKeySubstitutionRefused;

        using (var predecessor = RSA.Create())
        {
            predecessor.ImportFromPem(PredecessorPublicKeyPem);
            predecessorFingerprint = HashBytes(predecessor.ExportSubjectPublicKeyInfo());
            if (!string.Equals(predecessorFingerprint, ExpectedPredecessorPublicKeyFingerprintSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Embedded predecessor public key fingerprint drifted.");

            predecessorSignatureVerified = predecessor.VerifyData(
                rotationBytes,
                predecessorSignature,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            var driftedRotation = rotationBytes.ToArray();
            driftedRotation[^1] ^= 0x01;
            rotationClaimDriftRefused = !predecessor.VerifyData(
                driftedRotation,
                predecessorSignature,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            var driftedPredecessorSignature = predecessorSignature.ToArray();
            driftedPredecessorSignature[^1] ^= 0x01;
            predecessorSignatureDriftRefused = !predecessor.VerifyData(
                rotationBytes,
                driftedPredecessorSignature,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
        }

        using (var successor = RSA.Create())
        {
            successor.ImportFromPem(SuccessorPublicKeyPem);
            successorFingerprint = HashBytes(successor.ExportSubjectPublicKeyInfo());
            if (!string.Equals(successorFingerprint, ExpectedSuccessorPublicKeyFingerprintSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Embedded successor public key fingerprint drifted.");

            successorSignatureVerified = successor.VerifyData(
                successorPossessionBytes,
                successorSignature,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            var driftedSuccessorClaim = successorPossessionBytes.ToArray();
            driftedSuccessorClaim[^1] ^= 0x01;
            successorClaimDriftRefused = !successor.VerifyData(
                driftedSuccessorClaim,
                successorSignature,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            var driftedSuccessorSignature = successorSignature.ToArray();
            driftedSuccessorSignature[^1] ^= 0x01;
            successorSignatureDriftRefused = !successor.VerifyData(
                successorPossessionBytes,
                driftedSuccessorSignature,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
        }

        using (var substitution = RSA.Create())
        {
            substitution.ImportFromPem(SubstitutionPublicKeyPem);
            successorKeySubstitutionRefused = !substitution.VerifyData(
                successorPossessionBytes,
                successorSignature,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
        }

        var predecessorSourceBinding =
            string.Equals(source.PublicKeyFingerprintSha256, predecessorFingerprint, StringComparison.OrdinalIgnoreCase) &&
            source.KeyPossessionFixtureDemonstrated &&
            source.DetachedSignatureVerified &&
            !source.ProducerIdentityProven &&
            !source.ProducerAuthenticationProven &&
            !source.TrustAnchorEstablished;

        var predecessorToSuccessorBinding =
            string.Equals(rotationClaim.Schema, "matawaka.workbench-key-rotation-continuity-claim-fixture/v0.30", StringComparison.Ordinal) &&
            string.Equals(rotationClaim.Version, Version, StringComparison.Ordinal) &&
            string.Equals(rotationClaim.Relation, "fixture-successor-key-continuity", StringComparison.Ordinal) &&
            string.Equals(rotationClaim.AcceptedWorkbenchCommit, ExpectedV029Head, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(rotationClaim.AcceptedWorkbenchTag, ExpectedV029Tag, StringComparison.Ordinal) &&
            string.Equals(rotationClaim.SourceProvenanceReceiptSha256, sourceSha, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(rotationClaim.PredecessorPublicKeyFingerprintSha256, predecessorFingerprint, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(rotationClaim.SuccessorPublicKeyFingerprintSha256, successorFingerprint, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(rotationClaim.Ordinal, "successor-1", StringComparison.Ordinal) &&
            !rotationClaim.PredecessorRevocationClaimed;

        var successorPossessionBinding =
            string.Equals(successorPossessionClaim.Schema, "matawaka.workbench-successor-key-possession-claim-fixture/v0.30", StringComparison.Ordinal) &&
            string.Equals(successorPossessionClaim.Version, Version, StringComparison.Ordinal) &&
            string.Equals(successorPossessionClaim.Role, "fixture-successor-key-holder", StringComparison.Ordinal) &&
            string.Equals(successorPossessionClaim.RotationClaimSha256, rotationSha, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(successorPossessionClaim.SuccessorPublicKeyFingerprintSha256, successorFingerprint, StringComparison.OrdinalIgnoreCase);

        var fixtureDemonstrated =
            predecessorSourceBinding &&
            predecessorToSuccessorBinding &&
            successorPossessionBinding &&
            predecessorSignatureVerified &&
            successorSignatureVerified &&
            rotationClaimDriftRefused &&
            predecessorSignatureDriftRefused &&
            successorClaimDriftRefused &&
            successorSignatureDriftRefused &&
            successorKeySubstitutionRefused;

        if (!fixtureDemonstrated)
            throw new InvalidDataException("The v0.30 key rotation continuity fixture or one of its exact negative controls did not verify.");

        var nonEffects = new[]
        {
            "no private key material is embedded, loaded, requested, generated, imported, or persisted by Workbench",
            "no signing operation",
            "no key revocation operation or revocation proof",
            "no successor key installation, activation, registry mutation, or operational authority",
            "no producer identity, producer authentication, or common-controller claim",
            "no certificate-store access or certificate-chain validation",
            "no trust-anchor establishment",
            "no trusted timestamp or trusted temporal-ordering claim",
            "no delegation authority or ActionPermit creation",
            "no mutation of the v0.29 source provenance receipt",
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
            "no cross-machine or cross-OS portability claim",
            "no canonical UU-AAP conformance claim",
            "no Stable Core or interface-registry promotion",
            "receipt write is limited to Workbench/artifacts/key-rotation-continuity-boundaries"
        };

        var authority = new ProducerKeyRotationContinuityAuthorityReceipt(
            AuthoritySchema,
            "human-operator-at-workbench-ui",
            "workbench.maintenance.fixture-key-rotation-continuity-boundary",
            repositoryRoot,
            "explicit Key continuity button + confirmation dialog after v0.30 accepted",
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
            false,
            false,
            new[]
            {
                "read and hash the one exact retained v0.29 producer-key provenance receipt",
                "verify one predecessor-key detached signature over one exact successor-binding claim",
                "verify one successor-key detached signature over one exact successor-possession claim",
                "run in-memory claim/signature/substitution negative controls",
                "write one bounded key-rotation continuity receipt under the fixed Workbench artifact root"
            },
            nonEffects);

        var after = await ObserveGitStateAsync(repositoryRoot, cancellationToken).ConfigureAwait(false);
        var mainUnchanged = GitStatesEqual(before, after);
        if (!mainUnchanged)
            throw new InvalidDataException("Main Workbench Git state changed during the v0.30 key rotation continuity boundary.");

        var passed = fixtureDemonstrated && mainUnchanged;

        var receipt = new ProducerKeyRotationContinuityBoundaryReceipt(
            ReceiptSchema,
            Version,
            DateTimeOffset.Now,
            passed,
            passed
                ? "VERIFIED_FIXTURE_KEY_ROTATION_CONTINUITY_IDENTITY_TRUST_AUTHORITY_UNPROVEN"
                : "FAILED_FIXTURE_KEY_ROTATION_CONTINUITY",
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
            sourcePath,
            sourceSha,
            true,
            predecessorFingerprint,
            successorFingerprint,
            rotationClaim.Schema,
            rotationClaim.Version,
            CanonicalRotationClaimJson,
            rotationSha,
            ExpectedPredecessorRotationSignatureSha256,
            predecessorSignatureVerified,
            successorPossessionClaim.Schema,
            successorPossessionClaim.Version,
            CanonicalSuccessorPossessionClaimJson,
            successorPossessionSha,
            ExpectedSuccessorPossessionSignatureSha256,
            successorSignatureVerified,
            predecessorSourceBinding,
            predecessorToSuccessorBinding,
            successorPossessionBinding,
            rotationClaimDriftRefused,
            predecessorSignatureDriftRefused,
            successorClaimDriftRefused,
            successorSignatureDriftRefused,
            successorKeySubstitutionRefused,
            fixtureDemonstrated,
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
            "v0.30 demonstrates only a fixture cryptographic relationship from the exact v0.29 predecessor key to one exact successor public key, plus independent successor-key possession. It does not prove real-world producer identity, common controller, trust, revocation, trusted time ordering, delegation/action authority, portability, canonical UU-AAP conformance, or Stable Core promotion.");

        var artifactDir = Path.Combine(repositoryRoot, "artifacts", "key-rotation-continuity-boundaries");
        Directory.CreateDirectory(artifactDir);
        var artifactPath = Path.Combine(
            artifactDir,
            $"key-rotation-continuity-boundary-v0.30-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");

        await File.WriteAllTextAsync(
            artifactPath,
            JsonSerializer.Serialize(receipt, JsonOptions),
            Utf8NoBom,
            cancellationToken).ConfigureAwait(false);

        return (receipt, artifactPath);
    }

    private static void VerifySourceProvenance(ProducerKeyProvenanceBoundaryReceipt source)
    {
        if (!string.Equals(source.Schema, ExpectedV029ProvenanceSchema, StringComparison.Ordinal) ||
            !string.Equals(source.Version, "0.29.0", StringComparison.Ordinal) ||
            !source.Passed ||
            !string.Equals(source.Status, "VERIFIED_DETACHED_KEY_POSSESSION_FIXTURE_IDENTITY_UNPROVEN", StringComparison.Ordinal) ||
            !source.MainRepositoryUnchanged ||
            !string.Equals(source.MainHeadBefore, ExpectedV029Head, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(source.MainHeadAfter, ExpectedV029Head, StringComparison.OrdinalIgnoreCase) ||
            !source.MainTagsAfter.Contains(ExpectedV029Tag, StringComparer.Ordinal) ||
            source.MainDirtyPathsBefore.Count != 0 ||
            source.MainDirtyPathsAfter.Count != 0 ||
            !source.ExplicitUiConfirmationRequired ||
            !source.ExplicitUiConfirmationObserved ||
            !source.SourceClosureVerified ||
            !source.DetachedSignatureVerified ||
            !source.ClaimByteDriftRefused ||
            !source.SignatureByteDriftRefused ||
            !source.PublicKeySubstitutionRefused ||
            !source.ExactClaimToClosureBindingVerified ||
            !source.KeyPossessionFixtureDemonstrated ||
            source.PrivateKeyMaterialLoadedByBoundary ||
            source.SigningOperationAttempted ||
            source.ProducerIdentityProven ||
            source.ProducerAuthenticationProven ||
            source.TrustAnchorEstablished ||
            source.CertificateChainValidated ||
            source.TrustedTimestampValidated ||
            source.CrossMachinePortabilityProven ||
            source.CrossOsPortabilityProven ||
            source.AuthorityExpansionDetected ||
            !string.Equals(source.PublicKeyFingerprintSha256, ExpectedPredecessorPublicKeyFingerprintSha256, StringComparison.OrdinalIgnoreCase) ||
            source.SourceMutationAuthorized ||
            source.BuildAuthorized ||
            source.CheckpointAuthorized ||
            source.NetworkAccessAuthorized ||
            source.CatalogMutationAuthorized ||
            source.AgentExecuteAuthorized ||
            source.StableCorePromotionAuthorized)
            throw new InvalidDataException("Retained v0.29 producer-key provenance receipt does not match the exact bounded predecessor contract.");
    }

    private static string FindExactSourceProvenanceArtifact(string repositoryRoot)
    {
        var root = Path.GetFullPath(Path.Combine(repositoryRoot, "artifacts", "producer-key-provenance-boundaries"));
        if (!Directory.Exists(root))
            throw new InvalidDataException("No retained v0.29 producer-key provenance artifact directory exists.");

        var matches = new List<string>();
        foreach (var file in Directory.GetFiles(
                     root,
                     "producer-key-provenance-boundary-v0.29-*.json",
                     SearchOption.TopDirectoryOnly))
        {
            try
            {
                var full = Path.GetFullPath(file);
                var rootPrefix = root + Path.DirectorySeparatorChar;
                if (!full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                var sha = HashBytes(File.ReadAllBytes(full));
                if (string.Equals(sha, ExpectedV029ProvenanceReceiptSha256, StringComparison.OrdinalIgnoreCase))
                    matches.Add(full);
            }
            catch
            {
                // Unreadable retained evidence cannot support continuity verification.
            }
        }

        if (matches.Count == 0)
            throw new InvalidDataException("The exact retained v0.29 producer-key provenance receipt is not available.");
        if (matches.Count != 1)
            throw new InvalidDataException("More than one exact v0.29 producer-key provenance artifact is available; refusing ambiguous selection.");

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

    private static void RequireAcceptedV030(GitState state)
    {
        if (state.DirtyPaths.Count != 0)
            throw new InvalidDataException("Key rotation continuity boundary requires a clean accepted main Workbench repository.");
        if (!state.Tags.Contains(ExpectedTag, StringComparer.Ordinal))
            throw new InvalidDataException($"Key rotation continuity boundary is enabled only after {ExpectedTag} points at current HEAD.");
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
                throw new InvalidDataException($"Unexpected git status porcelain line in v0.30 key rotation continuity boundary: {raw}");

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
            throw new InvalidDataException("Only fixed read-only Git operations are permitted in v0.30 key rotation continuity boundary.");

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
            throw new InvalidDataException("Failed to start fixed read-only Git process for v0.30 key rotation continuity boundary.");

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
            throw new TimeoutException("Fixed read-only Git operation timed out in v0.30 key rotation continuity boundary.");
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
            throw new InvalidDataException($"Fixed read-only Git operation failed in v0.30 key rotation continuity boundary: {stderr.Trim()}");

        return stdout;
    }
}
