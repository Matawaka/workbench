using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.App;

public sealed record RecoveryTransportAdversarialEvidenceClosureItem(
    string Role,
    string ArtifactPath,
    string Sha256,
    string Schema,
    string Version,
    bool Verified);

public sealed record RecoveryTransportAdversarialEvidenceClosureScenarioBinding(
    string Id,
    string InitialBoundSha256,
    string CandidateSha256AtAttempt,
    bool RefusedBeforeEvidenceMaterialization,
    bool CandidateTransportPreservedAfterRefusal,
    bool SourceTransportUnchanged,
    bool Verified);

public sealed record RecoveryTransportAdversarialEvidenceClosureAuthorityReceipt(
    string Schema,
    string Subject,
    string Operation,
    string MainRepositoryRoot,
    string AuthoritySource,
    bool ExplicitUiConfirmationRequired,
    bool InputReceiptMutationAllowed,
    bool TransportInspectionAllowed,
    bool EvidenceMaterializationAllowed,
    bool RecoveryExecutionAllowed,
    bool SourceMutationAllowed,
    bool BuildAllowed,
    bool CheckpointAllowed,
    bool NetworkAccessAllowed,
    bool CatalogMutationAllowed,
    bool AgentExecuteAllowed,
    IReadOnlyList<string> AllowedEffects,
    IReadOnlyList<string> NonEffects);

public sealed record RecoveryTransportAdversarialEvidenceClosureReceipt(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    bool Closed,
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
    string PositiveIndependenceArtifactPath,
    string PositiveIndependenceArtifactSha256,
    string AdversarialControlMatrixArtifactPath,
    string AdversarialControlMatrixArtifactSha256,
    string CommonSourceTransportSha256,
    string SourceTransportManifestSha256,
    IReadOnlyList<RecoveryTransportAdversarialEvidenceClosureItem> Evidence,
    string EvidenceEnvelopeDigest,
    bool PositiveIndependenceReceiptVerified,
    bool AdversarialControlMatrixVerified,
    bool MatrixToPositiveByteBindingVerified,
    bool CommonSourceTransportBindingVerified,
    IReadOnlyList<RecoveryTransportAdversarialEvidenceClosureScenarioBinding> ScenarioBindings,
    bool AllAdversarialControlsRefusedBeforeEvidenceMaterialization,
    bool PositiveNegativeEvidencePairClosed,
    bool AuthorityLimitationsPreserved,
    bool AuthorityExpansionDetected,
    RecoveryTransportAdversarialEvidenceClosureAuthorityReceipt Authority,
    bool ProducerAuthenticationProven,
    bool CrossMachinePortabilityProven,
    bool CrossOsPortabilityProven,
    bool ProductionMainRepositoryRecoveryProven,
    bool GeneralFailureRecoveryClaimAllowed,
    bool AutomaticRecoveryAuthorized,
    bool RecoveryExecutionAuthorized,
    bool RollbackAuthorized,
    bool DeletionAuthorized,
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
/// Post-acceptance byte-bound closure over one exact v0.26 positive transport
/// independence receipt and one exact v0.27 adversarial refusal matrix. The
/// closure reads and hashes retained receipt bytes only. It does not inspect,
/// import, materialize, mutate, or execute the bound transport.
/// </summary>
public sealed class RecoveryTransportAdversarialEvidenceClosureService
{
    public const string Version = "0.28.0";
    public const string ReceiptSchema = "matawaka.workbench-recovery-transport-adversarial-evidence-closure/v0.28";
    public const string AuthoritySchema = "matawaka.workbench-recovery-transport-adversarial-evidence-closure-authority/v0.28";

    private const string ExpectedTag = "workbench-v0.28-accepted";
    private const string ExpectedV027Tag = "workbench-v0.27-accepted";
    private const string ExpectedV026Tag = "workbench-v0.26-accepted";
    private const string ExpectedV027Head = "8cdea04c2304f8589e9120d0451efa9e7e6b2f2b";
    private const string ExpectedV026Head = "e252f850bd87f0ad11e1a20097991b0480c812d5";
    private const string ExpectedPositiveSchema = "matawaka.workbench-recovery-evidence-transport-independence-drill/v0.26";
    private const string ExpectedMatrixSchema = "matawaka.workbench-recovery-transport-adversarial-control-matrix/v0.27";
    private const string ExpectedPositiveSha256 = "c94bbb3ec3b7ec577f1199bffadde02ac84bac9c52139b74ccb73e064793a543";
    private const string ExpectedCommonTransportSha256 = "692d0dfb375dd07c482f80accb0bf3250fe6f10332506dcb6fb35fee250ecdf8";
    private const string ExpectedTransportManifestSha256 = "22aa0903566cab24bc8cfbd08f49df66ff584b7d90328d045b410c6422f46ad4";
    private const string ExpectedCopyDriftSha256 = "60bebb261744358a4e07d7b6672ea705a0328b981d071a354cd6dccead77c53b";
    private const string ExpectedExtraEntrySha256 = "6fdba0636740aae212b71be7ba2b91dfe84b3defd39cbb52a8a83eb622ee7177";
    private const string ExpectedManifestDriftSha256 = "9c93f08da1a82add2d632c1c8fc6ed89dfe81de8fd5f89675459c2af9f4bf599";

    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(20);
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task<(RecoveryTransportAdversarialEvidenceClosureReceipt Receipt, string ArtifactPath)> CloseAsync(
        string workspaceRoot,
        bool explicitUiConfirmation,
        CancellationToken cancellationToken)
    {
        if (!explicitUiConfirmation)
            throw new InvalidDataException("Transport adversarial evidence closure requires explicit UI confirmation.");

        var repositoryRoot = ResolveRepositoryRoot(workspaceRoot);
        var before = await ObserveGitStateAsync(repositoryRoot, cancellationToken).ConfigureAwait(false);
        RequireAcceptedV028(before);

        var matrixPath = FindSinglePassingMatrixArtifact(repositoryRoot);
        var matrixBytes = await File.ReadAllBytesAsync(matrixPath, cancellationToken).ConfigureAwait(false);
        var matrixSha = HashBytes(matrixBytes);
        var matrix = JsonSerializer.Deserialize<RecoveryTransportAdversarialControlMatrixReceipt>(matrixBytes, JsonOptions)
            ?? throw new InvalidDataException("Retained v0.27 transport adversarial matrix could not be parsed.");
        VerifyMatrix(matrix);

        var positivePath = ValidateEvidencePath(
            repositoryRoot,
            matrix.SourceIndependenceArtifactPath,
            Path.Combine("artifacts", "recovery-transport-independence"),
            "v0.26 transport-independence receipt");

        var positiveBytes = await File.ReadAllBytesAsync(positivePath, cancellationToken).ConfigureAwait(false);
        var positiveSha = HashBytes(positiveBytes);
        if (!string.Equals(positiveSha, ExpectedPositiveSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(positiveSha, matrix.SourceIndependenceArtifactSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The v0.26 positive transport-independence receipt bytes do not match the exact v0.27 matrix binding.");

        var positive = JsonSerializer.Deserialize<RecoveryEvidenceTransportIndependenceDrillReceipt>(positiveBytes, JsonOptions)
            ?? throw new InvalidDataException("Retained v0.26 transport-independence receipt could not be parsed.");
        VerifyPositive(positive);

        var matrixToPositiveBinding =
            string.Equals(matrix.SourceIndependenceArtifactSha256, positiveSha, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Path.GetFullPath(matrix.SourceIndependenceArtifactPath), positivePath, StringComparison.OrdinalIgnoreCase);

        var commonTransportBinding =
            string.Equals(matrix.SourceTransportZipSha256, ExpectedCommonTransportSha256, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(positive.SourceTransportZipSha256, ExpectedCommonTransportSha256, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(positive.CopiedTransportZipSha256, ExpectedCommonTransportSha256, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(positive.SourceTransportManifestSha256, ExpectedTransportManifestSha256, StringComparison.OrdinalIgnoreCase);

        if (!matrixToPositiveBinding)
            throw new InvalidDataException("The exact v0.27 matrix no longer resolves to its exact v0.26 positive receipt.");
        if (!commonTransportBinding)
            throw new InvalidDataException("The v0.26 positive receipt and v0.27 negative matrix do not bind the same exact transport identity.");

        var scenarioBindings = BuildScenarioBindings(matrix);
        var allRefused = scenarioBindings.Count == 3 && scenarioBindings.All(x => x.Verified);
        if (!allRefused)
            throw new InvalidDataException("The exact v0.27 adversarial refusal scenario set is incomplete or no longer verifies.");

        var evidence = new[]
        {
            new RecoveryTransportAdversarialEvidenceClosureItem(
                "positive-transport-independence-receipt",
                positivePath,
                positiveSha,
                positive.Schema,
                positive.Version,
                true),
            new RecoveryTransportAdversarialEvidenceClosureItem(
                "adversarial-transport-control-matrix",
                matrixPath,
                matrixSha,
                matrix.Schema,
                matrix.Version,
                true),
            new RecoveryTransportAdversarialEvidenceClosureItem(
                "common-source-transport",
                matrix.SourceTransportZipPath,
                ExpectedCommonTransportSha256,
                "matawaka.workbench-recovery-transport-byte-binding/v0.28",
                "sha256",
                commonTransportBinding)
        };
        var envelopeDigest = HashEnvelope(evidence);

        var nonEffects = new[]
        {
            "no mutation of the v0.26 positive receipt",
            "no mutation of the v0.27 adversarial matrix",
            "no transport ZIP copy, mutation, inspection, import, or materialization",
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
            "no producer-authentication claim",
            "no cross-machine or cross-OS portability claim",
            "no production-main-repository recovery claim",
            "no general failure-recovery claim",
            "no automatic recovery authority",
            "no canonical UU-AAP conformance claim",
            "no Stable Core or interface-registry promotion",
            "closure artifact write is limited to Workbench/artifacts/recovery-transport-adversarial-evidence-closures"
        };

        var authority = new RecoveryTransportAdversarialEvidenceClosureAuthorityReceipt(
            AuthoritySchema,
            "human-operator-at-workbench-ui",
            "workbench.maintenance.transport-adversarial-evidence-closure",
            repositoryRoot,
            "explicit Transport closure button + confirmation dialog after v0.28 accepted",
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
                "read and hash the one exact retained v0.27 adversarial matrix receipt",
                "resolve, read and hash the exact v0.26 positive receipt bound by that matrix",
                "verify fixed positive, negative, common-transport, and authority-limit assertions",
                "write one byte-bound closure receipt under the fixed Workbench artifact root"
            },
            nonEffects);

        var authorityPreserved =
            positive.AuthorityLimitationsPreserved &&
            !positive.ProducerAuthenticationProven &&
            !positive.CrossMachinePortabilityProven &&
            !positive.CrossOsPortabilityProven &&
            !positive.ProductionMainRepositoryRecoveryProven &&
            !positive.GeneralFailureRecoveryClaimAllowed &&
            !positive.AutomaticRecoveryAuthorized &&
            !positive.RecoveryExecutionAuthorized &&
            !positive.RollbackAuthorized &&
            !positive.DeletionAuthorized &&
            !positive.SourceMutationAuthorized &&
            !positive.BuildAuthorized &&
            !positive.CheckpointAuthorized &&
            !positive.NetworkAccessAuthorized &&
            !positive.CatalogMutationAuthorized &&
            !positive.AgentExecuteAuthorized &&
            !positive.StableCorePromotionAuthorized &&
            !matrix.ProducerAuthenticationProven &&
            !matrix.CrossMachinePortabilityProven &&
            !matrix.CrossOsPortabilityProven &&
            !matrix.ProductionMainRepositoryRecoveryProven &&
            !matrix.GeneralFailureRecoveryClaimAllowed &&
            !matrix.AutomaticRecoveryAuthorized &&
            !matrix.RecoveryExecutionAuthorized &&
            !matrix.RollbackAuthorized &&
            !matrix.DeletionAuthorized &&
            !matrix.SourceMutationAuthorized &&
            !matrix.BuildAuthorized &&
            !matrix.CheckpointAuthorized &&
            !matrix.NetworkAccessAuthorized &&
            !matrix.CatalogMutationAuthorized &&
            !matrix.AgentExecuteAuthorized &&
            !matrix.StableCorePromotionAuthorized;

        if (!authorityPreserved)
            throw new InvalidDataException("Constituent evidence claims authority beyond the v0.28 closure ceiling.");

        var after = await ObserveGitStateAsync(repositoryRoot, cancellationToken).ConfigureAwait(false);
        var mainUnchanged = GitStatesEqual(before, after);
        if (!mainUnchanged)
            throw new InvalidDataException("Main Workbench Git state changed during v0.28 transport adversarial evidence closure.");

        var pairClosed =
            matrixToPositiveBinding &&
            commonTransportBinding &&
            allRefused &&
            authorityPreserved &&
            mainUnchanged;

        var receipt = new RecoveryTransportAdversarialEvidenceClosureReceipt(
            ReceiptSchema,
            Version,
            DateTimeOffset.Now,
            pairClosed,
            pairClosed
                ? "CLOSED_BYTE_BOUND_TRANSPORT_ADVERSARIAL_EVIDENCE_ENVELOPE"
                : "OPEN_TRANSPORT_ADVERSARIAL_EVIDENCE_BINDING_INCOMPLETE",
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
            positivePath,
            positiveSha,
            matrixPath,
            matrixSha,
            ExpectedCommonTransportSha256,
            ExpectedTransportManifestSha256,
            evidence,
            envelopeDigest,
            true,
            true,
            matrixToPositiveBinding,
            commonTransportBinding,
            scenarioBindings,
            allRefused,
            pairClosed,
            authorityPreserved,
            false,
            authority,
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
            false,
            nonEffects,
            "v0.28 closes one exact v0.26 same-machine transport-independence receipt and one exact v0.27 adversarial refusal matrix into a byte-bound evidence envelope over the same exact transport SHA-256. Closure reads receipt bytes only and does not reopen the transport. It is not producer authentication, cross-machine/cross-OS portability proof, evidence materialization authority, live or automatic recovery authority, production recovery proof, canonical UU-AAP conformance, or Stable Core promotion.");

        var artifactDir = Path.Combine(repositoryRoot, "artifacts", "recovery-transport-adversarial-evidence-closures");
        Directory.CreateDirectory(artifactDir);
        var artifactPath = Path.Combine(
            artifactDir,
            $"recovery-transport-adversarial-evidence-closure-v0.28-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(
            artifactPath,
            JsonSerializer.Serialize(receipt, JsonOptions),
            Utf8NoBom,
            cancellationToken).ConfigureAwait(false);
        return (receipt, artifactPath);
    }

    private static void VerifyPositive(RecoveryEvidenceTransportIndependenceDrillReceipt receipt)
    {
        if (!string.Equals(receipt.Schema, ExpectedPositiveSchema, StringComparison.Ordinal) ||
            !string.Equals(receipt.Version, "0.26.0", StringComparison.Ordinal) ||
            !receipt.Passed ||
            !string.Equals(receipt.Status, "INDEPENDENT_LOCAL_TRANSPORT_CAPSULE_VERIFIED", StringComparison.Ordinal) ||
            !receipt.MainRepositoryUnchanged ||
            !string.Equals(receipt.MainHeadBefore, ExpectedV026Head, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(receipt.MainHeadAfter, ExpectedV026Head, StringComparison.OrdinalIgnoreCase) ||
            !receipt.MainTagsAfter.Contains(ExpectedV026Tag, StringComparer.Ordinal) ||
            receipt.MainDirtyPathsBefore.Count != 0 ||
            receipt.MainDirtyPathsAfter.Count != 0 ||
            !receipt.ExplicitUiConfirmationRequired ||
            !receipt.ExplicitUiConfirmationObserved ||
            !receipt.SourceImportReceiptVerified ||
            !receipt.CopiedTransportByteIdentical ||
            !receipt.CopiedTransportSeparatedFromSourceTransportRoot ||
            !receipt.CopiedTransportInspectionVerified ||
            !receipt.ExactTransportFileSetVerified ||
            !receipt.TransportPayloadDigestsVerified ||
            !receipt.TransportManifestDigestReproduced ||
            !receipt.CapsuleManifestDigestReproduced ||
            !receipt.EvidenceEnvelopeDigestReproduced ||
            !receipt.IndependentMaterializedCopiesVerified ||
            !receipt.PositiveRecoveryDrillReplayed ||
            !receipt.RecoveryCapabilityAdmissionReplayed ||
            !receipt.NegativeControlMatrixReplayed ||
            !receipt.AdmissionToDrillBindingReplayed ||
            !receipt.NegativeRefusalSemanticsReplayed ||
            !receipt.AuthorityLimitationsPreserved ||
            !receipt.TransportOnlyEvidenceReplayPathGuardEnabled ||
            receipt.OriginalEvidencePathAccessAttemptsDuringTransportReplay != 0 ||
            !receipt.ReplayUsedOnlyCopiedTransportBytes ||
            receipt.OriginalTransportZipRequiredAfterCopy ||
            receipt.OriginalRelocationRootRequiredForDrill ||
            receipt.OriginalReplayRootRequiredForDrill ||
            receipt.OriginalEvidenceArtifactsRequiredForDrill ||
            receipt.HistoricalAbsolutePathsDereferencedDuringTransportReplay ||
            !receipt.LocalTransportIndependenceDemonstrated ||
            !string.Equals(receipt.SourceTransportZipSha256, ExpectedCommonTransportSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(receipt.CopiedTransportZipSha256, ExpectedCommonTransportSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(receipt.SourceTransportManifestSha256, ExpectedTransportManifestSha256, StringComparison.OrdinalIgnoreCase) ||
            receipt.ProducerAuthenticationProven ||
            receipt.CrossMachinePortabilityProven ||
            receipt.CrossOsPortabilityProven ||
            receipt.ProductionMainRepositoryRecoveryProven ||
            receipt.GeneralFailureRecoveryClaimAllowed ||
            receipt.AutomaticRecoveryAuthorized ||
            receipt.RecoveryExecutionAuthorized ||
            receipt.RollbackAuthorized ||
            receipt.DeletionAuthorized ||
            receipt.SourceMutationAuthorized ||
            receipt.BuildAuthorized ||
            receipt.CheckpointAuthorized ||
            receipt.NetworkAccessAuthorized ||
            receipt.CatalogMutationAuthorized ||
            receipt.AgentExecuteAuthorized ||
            receipt.StableCorePromotionAuthorized)
            throw new InvalidDataException("Retained v0.26 transport-independence receipt does not match the exact bounded positive evidence contract.");
    }

    private static void VerifyMatrix(RecoveryTransportAdversarialControlMatrixReceipt matrix)
    {
        if (!string.Equals(matrix.Schema, ExpectedMatrixSchema, StringComparison.Ordinal) ||
            !string.Equals(matrix.Version, "0.27.0", StringComparison.Ordinal) ||
            !matrix.Passed ||
            !string.Equals(matrix.Status, "TRANSPORT_ADVERSARIAL_CONTROLS_PASSED", StringComparison.Ordinal) ||
            !matrix.MainRepositoryUnchanged ||
            !string.Equals(matrix.MainHeadBefore, ExpectedV027Head, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(matrix.MainHeadAfter, ExpectedV027Head, StringComparison.OrdinalIgnoreCase) ||
            !matrix.MainTagsAfter.Contains(ExpectedV027Tag, StringComparer.Ordinal) ||
            matrix.MainDirtyPathsBefore.Count != 0 ||
            matrix.MainDirtyPathsAfter.Count != 0 ||
            !matrix.ExplicitUiConfirmationRequired ||
            !matrix.ExplicitUiConfirmationObserved ||
            !matrix.SourceIndependenceReceiptVerified ||
            !string.Equals(matrix.SourceIndependenceArtifactSha256, ExpectedPositiveSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(matrix.SourceTransportZipSha256, ExpectedCommonTransportSha256, StringComparison.OrdinalIgnoreCase) ||
            matrix.Scenarios.Count != 3 ||
            !matrix.CopyByteDriftAfterBindingRefused ||
            !matrix.ExtraZipEntryRefused ||
            !matrix.TransportManifestDriftRefused ||
            !matrix.AllControlsRefusedBeforeEvidenceMaterialization ||
            !matrix.SourceTransportUnchanged ||
            !matrix.Authority.ExplicitUiConfirmationRequired ||
            matrix.Authority.MainRepositoryMutationAllowed ||
            matrix.Authority.SourceTransportMutationAllowed ||
            !matrix.Authority.IsolatedTransportCopyMutationAllowed ||
            !matrix.Authority.VerifyOnlyInspectionAllowed ||
            matrix.Authority.EvidenceMaterializationAllowed ||
            matrix.Authority.RecoveryExecutionAllowed ||
            matrix.Authority.BuildAllowed ||
            matrix.Authority.CheckpointAllowed ||
            matrix.Authority.NetworkAccessAllowed ||
            matrix.Authority.CatalogMutationAllowed ||
            matrix.Authority.AgentExecuteAllowed ||
            matrix.ProducerAuthenticationProven ||
            matrix.CrossMachinePortabilityProven ||
            matrix.CrossOsPortabilityProven ||
            matrix.ProductionMainRepositoryRecoveryProven ||
            matrix.GeneralFailureRecoveryClaimAllowed ||
            matrix.AutomaticRecoveryAuthorized ||
            matrix.RecoveryExecutionAuthorized ||
            matrix.RollbackAuthorized ||
            matrix.DeletionAuthorized ||
            matrix.SourceMutationAuthorized ||
            matrix.BuildAuthorized ||
            matrix.CheckpointAuthorized ||
            matrix.NetworkAccessAuthorized ||
            matrix.CatalogMutationAuthorized ||
            matrix.AgentExecuteAuthorized ||
            matrix.StableCorePromotionAuthorized)
            throw new InvalidDataException("Retained v0.27 transport adversarial matrix does not match the exact bounded negative evidence contract.");
    }

    private static IReadOnlyList<RecoveryTransportAdversarialEvidenceClosureScenarioBinding> BuildScenarioBindings(
        RecoveryTransportAdversarialControlMatrixReceipt matrix)
    {
        var expected = new Dictionary<string, (string Sha256, bool InspectionAttempted)>(StringComparer.Ordinal)
        {
            ["copy-byte-drift-after-binding-refused"] = (ExpectedCopyDriftSha256, false),
            ["extra-zip-entry-refused"] = (ExpectedExtraEntrySha256, true),
            ["transport-manifest-drift-refused"] = (ExpectedManifestDriftSha256, true)
        };

        var bindings = new List<RecoveryTransportAdversarialEvidenceClosureScenarioBinding>();
        foreach (var pair in expected.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var scenario = matrix.Scenarios.SingleOrDefault(x => string.Equals(x.Id, pair.Key, StringComparison.Ordinal));
            var verified =
                scenario is not null &&
                scenario.Passed &&
                string.Equals(scenario.InitialBoundSha256, ExpectedCommonTransportSha256, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(scenario.CandidateSha256AtAttempt, pair.Value.Sha256, StringComparison.OrdinalIgnoreCase) &&
                scenario.InspectionAttempted == pair.Value.InspectionAttempted &&
                scenario.Rejected &&
                !scenario.EvidenceMaterializationAttempted &&
                !scenario.EvidenceMaterializationRootCreated &&
                scenario.CandidateTransportPreservedAfterRefusal &&
                scenario.SourceTransportUnchanged;

            bindings.Add(new RecoveryTransportAdversarialEvidenceClosureScenarioBinding(
                pair.Key,
                scenario?.InitialBoundSha256 ?? string.Empty,
                scenario?.CandidateSha256AtAttempt ?? string.Empty,
                scenario is not null && scenario.Rejected &&
                    !scenario.EvidenceMaterializationAttempted &&
                    !scenario.EvidenceMaterializationRootCreated,
                scenario?.CandidateTransportPreservedAfterRefusal ?? false,
                scenario?.SourceTransportUnchanged ?? false,
                verified));
        }

        return bindings;
    }

    private static string FindSinglePassingMatrixArtifact(string repositoryRoot)
    {
        var root = Path.GetFullPath(Path.Combine(repositoryRoot, "artifacts", "recovery-transport-adversarial-controls"));
        if (!Directory.Exists(root))
            throw new InvalidDataException("No retained v0.27 transport adversarial matrix artifact directory exists.");

        var rootPrefix = root + Path.DirectorySeparatorChar;
        var matches = new List<string>();
        foreach (var file in Directory.GetFiles(
                     root,
                     "recovery-transport-adversarial-control-matrix-v0.27-*.json",
                     SearchOption.TopDirectoryOnly))
        {
            try
            {
                var full = Path.GetFullPath(file);
                if (!full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                var bytes = File.ReadAllBytes(full);
                var parsed = JsonSerializer.Deserialize<RecoveryTransportAdversarialControlMatrixReceipt>(bytes, JsonOptions);
                if (parsed is not null &&
                    parsed.Passed &&
                    string.Equals(parsed.Schema, ExpectedMatrixSchema, StringComparison.Ordinal) &&
                    string.Equals(parsed.Status, "TRANSPORT_ADVERSARIAL_CONTROLS_PASSED", StringComparison.Ordinal) &&
                    string.Equals(parsed.MainHeadAfter, ExpectedV027Head, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(parsed.SourceIndependenceArtifactSha256, ExpectedPositiveSha256, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(parsed.SourceTransportZipSha256, ExpectedCommonTransportSha256, StringComparison.OrdinalIgnoreCase))
                    matches.Add(full);
            }
            catch
            {
                // Unreadable retained evidence cannot support closure.
            }
        }

        if (matches.Count == 0)
            throw new InvalidDataException("No passing exact v0.27 transport adversarial matrix artifact is available.");
        if (matches.Count != 1)
            throw new InvalidDataException("More than one passing exact v0.27 transport adversarial matrix is available; refusing ambiguous closure selection.");
        return matches[0];
    }

    private static string ValidateEvidencePath(
        string repositoryRoot,
        string candidate,
        string relativeRoot,
        string label)
    {
        if (string.IsNullOrWhiteSpace(candidate) || !File.Exists(candidate))
            throw new InvalidDataException($"Retained {label} file is missing.");

        var root = Path.GetFullPath(Path.Combine(repositoryRoot, relativeRoot));
        var rootPrefix = root + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(candidate);
        if (!full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Retained {label} escapes its allowed artifact root.");
        return full;
    }

    private static string HashEnvelope(IReadOnlyList<RecoveryTransportAdversarialEvidenceClosureItem> evidence)
    {
        var expectedOrder = new[]
        {
            "positive-transport-independence-receipt",
            "adversarial-transport-control-matrix",
            "common-source-transport"
        };

        if (evidence.Count != expectedOrder.Length ||
            !evidence.Select(x => x.Role).SequenceEqual(expectedOrder, StringComparer.Ordinal))
            throw new InvalidDataException("Transport adversarial closure evidence role order is invalid.");

        var canonical = string.Join(
            "\n",
            evidence.Select(x => $"{x.Role}|{x.Sha256}|{x.Schema}|{x.Version}")) + "\n";
        return HashBytes(Encoding.UTF8.GetBytes(canonical));
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

    private static void RequireAcceptedV028(GitState state)
    {
        if (state.DirtyPaths.Count != 0)
            throw new InvalidDataException("Transport adversarial evidence closure requires a clean accepted main Workbench repository.");
        if (!state.Tags.Contains(ExpectedTag, StringComparer.Ordinal))
            throw new InvalidDataException($"Transport adversarial evidence closure is enabled only after {ExpectedTag} points at the current HEAD.");
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
                throw new InvalidDataException($"Unexpected git status porcelain line in v0.28 transport adversarial evidence closure: {raw}");
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
            throw new InvalidDataException("Only fixed read-only Git operations are permitted in v0.28 transport adversarial evidence closure.");

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
            throw new InvalidDataException("Failed to start fixed read-only Git process for v0.28 transport adversarial evidence closure.");

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
            throw new TimeoutException("Fixed read-only Git operation timed out in v0.28 transport adversarial evidence closure.");
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidDataException($"Fixed read-only Git operation failed in v0.28 transport adversarial evidence closure: {stderr.Trim()}");
        return stdout;
    }
}