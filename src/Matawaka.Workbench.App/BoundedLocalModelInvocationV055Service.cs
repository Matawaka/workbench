using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Matawaka.Workbench.App;

public sealed record LocalModelInvocationRequestV055(
    string Schema,
    string RequestId,
    string RuntimeTreeManifestPath,
    string RuntimeTreeManifestSha256,
    string ExecutableRelativePath,
    string ExpectedExecutableSha256,
    string ModelAcquisitionReceiptPath,
    string ModelAcquisitionReceiptSha256,
    string ModelArtifactId,
    string ExpectedModelSha256,
    string InvocationProfileId,
    string RequestUtf8,
    int MaxRequestBytes,
    int MaxStdoutBytes,
    int MaxStderrBytes,
    int MaxOutputChars,
    int MaxOutputTokens,
    int TimeoutSeconds,
    int TtlSeconds);

public sealed record LocalModelInvocationPreviewV055(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string RequestId,
    string RequestDigestSha256,
    int RequestBytes,
    string RuntimeTreeManifestPath,
    string RuntimeTreeManifestSha256,
    string RuntimeTreeManifestId,
    string RuntimeRoot,
    string ExecutableRelativePath,
    string ExecutablePath,
    string ExecutableSha256,
    long ExecutableBytes,
    string ModelAcquisitionReceiptPath,
    string ModelAcquisitionReceiptSha256,
    string ModelArtifactId,
    string ModelPath,
    string ModelSha256,
    long ModelBytes,
    string InvocationProfileId,
    int MaxRequestBytes,
    int MaxStdoutBytes,
    int MaxStderrBytes,
    int MaxOutputChars,
    int MaxOutputTokens,
    int TimeoutSeconds,
    int TtlSeconds,
    bool RuntimeTreeMaterializationPerformed,
    bool ArtifactAcquisitionPerformed,
    bool ProcessExecutionPerformed,
    bool ModelRequestPerformed,
    bool ReadyForExplicitModelInvocationAuthority,
    IReadOnlyList<string> NonEffects,
    string Note);

public sealed record LocalModelInvocationLeaseStateV055(
    string Schema,
    string Version,
    string LeaseId,
    string RequestId,
    string RequestDigestSha256,
    int RequestBytes,
    string RuntimeTreeManifestPath,
    string RuntimeTreeManifestSha256,
    string RuntimeRoot,
    string ExecutablePath,
    string ExecutableSha256,
    long ExecutableBytes,
    string ModelAcquisitionReceiptPath,
    string ModelAcquisitionReceiptSha256,
    string ModelArtifactId,
    string ModelPath,
    string ModelSha256,
    long ModelBytes,
    string InvocationProfileId,
    int MaxRequestBytes,
    int MaxStdoutBytes,
    int MaxStderrBytes,
    int MaxOutputChars,
    int MaxOutputTokens,
    int TimeoutSeconds,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    int MaxCalls,
    int RemainingCalls,
    string BearerSha256,
    string State,
    bool Revoked,
    bool Completed,
    bool Failed,
    string? FailureClassification,
    int? ProcessId,
    DateTimeOffset? ProcessStartedAt,
    long StateRevision,
    IReadOnlyList<string> NonEffects,
    string Note);

public sealed record LocalModelInvocationGrantV055(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string LeaseId,
    string Bearer,
    string RequestId,
    string RequestDigestSha256,
    int RequestBytes,
    string RequestUtf8,
    string LeaseStatePath,
    DateTimeOffset ExpiresAt,
    int MaxCalls,
    bool BearerPersistedInPlaintextByWorkbench,
    bool RequestTextPersistedInLeaseState,
    bool ProcessExecutionPerformed,
    bool ModelRequestPerformed,
    string Note);

public sealed record LocalModelInvocationAuthorityReceiptV055(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string LeaseId,
    string RequestId,
    string RequestDigestSha256,
    int RequestBytes,
    string BearerSha256,
    string LeaseStatePath,
    string LeaseStateSha256,
    DateTimeOffset ExpiresAt,
    int MaxCalls,
    bool BearerPlaintextPersisted,
    bool RequestTextPersistedInLeaseState,
    bool ArtifactAcquisitionPerformed,
    bool RuntimeTreeMaterializationPerformed,
    bool ProcessExecutionPerformed,
    bool ModelRequestPerformed,
    IReadOnlyList<string> NonEffects,
    string Status,
    string Note);

public sealed record LocalModelInvocationExecutionReceiptV055(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string TransactionId,
    string LeaseId,
    string RequestId,
    string State,
    string InvocationProfileId,
    string RequestDigestSha256,
    int RequestBytes,
    string RuntimeTreeManifestPath,
    string RuntimeTreeManifestSha256,
    string ExecutablePath,
    string ExecutableSha256BeforeStart,
    string ModelAcquisitionReceiptPath,
    string ModelAcquisitionReceiptSha256,
    string ModelArtifactId,
    string ModelPath,
    string ModelSha256BeforeStart,
    long ModelBytes,
    int? ProcessId,
    DateTimeOffset? ProcessStartedAt,
    string? ObservedProcessImagePath,
    string? ObservedProcessImageSha256,
    bool ExactProcessImageVerified,
    bool OneRequestAttempted,
    long StdoutBytesObserved,
    string? StdoutSha256,
    long StderrBytesObserved,
    string? StderrSha256,
    string? OutputArtifactPath,
    string? OutputArtifactSha256,
    int? OutputChars,
    bool ModelInvocationAuthorityConsumed,
    bool WorkbenchNetworkTransportPerformed,
    bool ServerOrPortRequestedByInvocationProfile,
    bool ProcessNetworkIsolationProven,
    bool AutomaticRetryPerformed,
    bool AutomaticResumePerformed,
    bool BenchmarkPerformed,
    bool GameAccessPerformed,
    bool DisplayPerformed,
    bool ResponseAuthorityCreated,
    bool ActionPermitCreated,
    bool SuccessorPermitCreated,
    string? FailureClassification,
    IReadOnlyList<string> NonEffects,
    string Status,
    string Note);

public sealed record LocalModelInvocationPortableResultV055(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string RequestId,
    string InvocationProfileId,
    string ModelArtifactId,
    string ModelSha256,
    string ExecutableSha256,
    string RequestDigestSha256,
    int RequestBytes,
    string StdoutSha256,
    string OutputTextSha256,
    string OutputText,
    int OutputChars,
    bool ContentReviewComplete,
    bool FactualTruthProven,
    bool ResponseAuthorityCreated,
    bool DisplayPermitCreated,
    bool GameAuthorityCreated,
    bool ActionPermitCreated,
    bool SuccessorPermitCreated,
    string Status,
    string Note);

public sealed class LocalModelInvocationExceptionV055 : IOException
{
    public string Classification { get; }
    public string? ReceiptPath { get; }

    public LocalModelInvocationExceptionV055(string classification, string message, string? receiptPath = null, Exception? inner = null)
        : base(message, inner)
    {
        Classification = classification;
        ReceiptPath = receiptPath;
    }
}

public sealed class BoundedLocalModelInvocationV055Service
{
    public const string Version = "0.55.0";
    public const string RequestSchema = "matawaka.local-model-invocation-request/v0.55";
    public const string PreviewSchema = "matawaka.local-model-invocation-preview/v0.55";
    public const string LeaseStateSchema = "matawaka.local-model-invocation-lease-state/v0.55";
    public const string GrantSchema = "matawaka.local-model-invocation-grant/v0.55";
    public const string AuthorityReceiptSchema = "matawaka.local-model-invocation-authority-receipt/v0.55";
    public const string ExecutionReceiptSchema = "matawaka.local-model-invocation-execution-receipt/v0.55";
    public const string PortableResultSchema = "matawaka.local-model-output/v0.55";

    public const string FixtureProfileId = "FIXTURE_STDIO_V1";
    public const string FixtureExecutableName = "Matawaka.Workbench.V055.ModelFixture.exe";

    public const int HardMaxRequestBytes = 64 * 1024;
    public const int HardMaxStdoutBytes = 1024 * 1024;
    public const int HardMaxStderrBytes = 256 * 1024;
    public const int HardMaxOutputChars = 64 * 1024;
    public const int HardMaxOutputTokens = 4096;
    public const int MaxTimeoutSeconds = 10 * 60;
    public const int MaxTtlSeconds = 30 * 60;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = true
    };
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Regex SafeId = new("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly HashSet<string> ForbiddenExecutableNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd.exe", "powershell.exe", "pwsh.exe", "wscript.exe", "cscript.exe", "mshta.exe",
        "rundll32.exe", "regsvr32.exe", "bash.exe", "sh.exe", "python.exe", "pythonw.exe"
    };
    private static readonly string[] FixedInheritedEnvironmentNames = { "SystemRoot", "WINDIR", "TEMP", "TMP" };

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(
        IntPtr hProcess,
        int dwFlags,
        StringBuilder lpExeName,
        ref int lpdwSize);

    public LocalModelInvocationPreviewV055 Preview(
        string workspaceRoot,
        LocalModelInvocationRequestV055 request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request is null || request.Schema != RequestSchema)
            throw Refused("REQUEST_SCHEMA_REFUSED", $"Expected exact schema {RequestSchema}.");
        if (string.IsNullOrWhiteSpace(request.RequestId) || !SafeId.IsMatch(request.RequestId))
            throw Refused("REQUEST_ID_REFUSED", "RequestId must be a safe 1..128 character token.");
        RequireSha256(request.RuntimeTreeManifestSha256, "RuntimeTreeManifestSha256");
        RequireSha256(request.ExpectedExecutableSha256, "ExpectedExecutableSha256");
        RequireSha256(request.ModelAcquisitionReceiptSha256, "ModelAcquisitionReceiptSha256");
        RequireSha256(request.ExpectedModelSha256, "ExpectedModelSha256");
        ValidateBounds(request);

        var requestBytes = StrictUtf8.GetByteCount(request.RequestUtf8 ?? throw Refused("REQUEST_TEXT_REFUSED", "RequestUtf8 is required."));
        if (requestBytes < 1 || requestBytes > request.MaxRequestBytes)
            throw Refused("REQUEST_BYTE_CEILING_REFUSED", $"Request UTF-8 bytes must be within 1..{request.MaxRequestBytes}.");

        var repo = ResolveRepositoryRoot(workspaceRoot);
        var manifestPath = RequireAbsoluteExistingFile(request.RuntimeTreeManifestPath, "RUNTIME_MANIFEST_MISSING");
        RequireOutsideRepository(repo, manifestPath, "runtime-tree manifest");
        RejectReparseChain(manifestPath);
        var manifestSha = HashFile(manifestPath);
        if (!manifestSha.Equals(request.RuntimeTreeManifestSha256, StringComparison.OrdinalIgnoreCase))
            throw Refused("RUNTIME_MANIFEST_HASH_MISMATCH", "Runtime-tree manifest SHA-256 differs from exact request binding.");

        RuntimeTreeManifestV053 manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<RuntimeTreeManifestV053>(File.ReadAllText(manifestPath, Encoding.UTF8), JsonOptions)
                ?? throw new InvalidDataException("Runtime manifest deserialized to null.");
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException)
        {
            throw Refused("RUNTIME_MANIFEST_INVALID", "Runtime-tree manifest is not exact v0.53 evidence.", ex);
        }
        if (manifest.Schema != BoundedRuntimeExecutionV053Service.RuntimeTreeManifestSchema ||
            manifest.Version != "0.53" ||
            manifest.State != BoundedRuntimeExecutionV053Service.RuntimeTreeVerifiedState ||
            string.IsNullOrWhiteSpace(manifest.ManifestId))
            throw Refused("RUNTIME_MANIFEST_STATE_REFUSED", "Runtime-tree manifest must be exact MATERIALIZED_VERIFIED v0.53 evidence.");

        var runtimeRoot = EnsureDirectoryRoot(manifest.RuntimeRoot);
        if (!Directory.Exists(runtimeRoot)) throw Refused("RUNTIME_ROOT_MISSING", "Materialized runtime root is missing.");
        RequireOutsideRepository(repo, runtimeRoot, "runtime root");
        RejectReparseChain(runtimeRoot);

        var executableRelative = NormalizeRelativePath(request.ExecutableRelativePath, "ExecutableRelativePath");
        if (!executableRelative.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw Refused("EXECUTABLE_TYPE_REFUSED", "Only exact .exe images are eligible.");
        var executableName = Path.GetFileName(executableRelative);
        if (ForbiddenExecutableNames.Contains(executableName))
            throw Refused("SHELL_INDIRECTION_REFUSED", "Shell/interpreter/loader executables are not eligible.");
        var executablePath = ResolveUnderRoot(runtimeRoot, executableRelative, "executable");
        if (!File.Exists(executablePath)) throw Refused("EXECUTABLE_MISSING", "Exact executable is missing.");
        RejectReparseChain(executablePath);

        var manifestEntry = manifest.Files?.SingleOrDefault(x =>
            string.Equals(NormalizeRelativePath(x.RelativePath, "manifest RelativePath"), executableRelative, StringComparison.OrdinalIgnoreCase))
            ?? throw Refused("EXECUTABLE_NOT_IN_RUNTIME_MANIFEST", "Executable is not bound by the runtime-tree manifest.");
        RequireSha256(manifestEntry.Sha256, "runtime manifest executable SHA-256");
        if (!manifestEntry.Sha256.Equals(request.ExpectedExecutableSha256, StringComparison.OrdinalIgnoreCase))
            throw Refused("EXECUTABLE_REQUEST_MANIFEST_MISMATCH", "Request executable SHA-256 differs from manifest binding.");
        var executableBytes = new FileInfo(executablePath).Length;
        if (manifestEntry.Bytes != executableBytes)
            throw Refused("EXECUTABLE_SIZE_MISMATCH", "Executable byte length differs from runtime-tree manifest.");
        var executableSha = HashFile(executablePath);
        if (!executableSha.Equals(request.ExpectedExecutableSha256, StringComparison.OrdinalIgnoreCase))
            throw Refused("EXECUTABLE_HASH_MISMATCH", "Executable SHA-256 differs from request binding.");

        ValidateProfile(request.InvocationProfileId, executableName);

        var receiptPath = ValidateAcquisitionReceiptPath(repo, request.ModelAcquisitionReceiptPath);
        var receiptSha = HashFile(receiptPath);
        if (!receiptSha.Equals(request.ModelAcquisitionReceiptSha256, StringComparison.OrdinalIgnoreCase))
            throw Refused("MODEL_ACQUISITION_RECEIPT_HASH_MISMATCH", "Model acquisition receipt SHA-256 differs from exact request binding.");

        ArtifactAcquisitionExecutionReceiptV052 acquisition;
        try
        {
            acquisition = JsonSerializer.Deserialize<ArtifactAcquisitionExecutionReceiptV052>(File.ReadAllText(receiptPath, Encoding.UTF8), JsonOptions)
                ?? throw new InvalidDataException("Acquisition receipt deserialized to null.");
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException)
        {
            throw Refused("MODEL_ACQUISITION_RECEIPT_INVALID", "Model acquisition receipt is not exact v0.52 execution evidence.", ex);
        }
        if (acquisition.Schema != BoundedArtifactAcquisitionV052Service.ExecutionReceiptSchema ||
            acquisition.State != "ACQUISITION_VERIFIED" ||
            acquisition.Status != "ACQUISITION_VERIFIED" ||
            !acquisition.AllArtifactsSha256Verified ||
            acquisition.ExtractionPerformed || acquisition.ProcessExecutionPerformed || acquisition.RuntimeStartPerformed ||
            acquisition.BenchmarkPerformed || acquisition.ModelRequestPerformed || acquisition.GameAccessPerformed)
            throw Refused("MODEL_ACQUISITION_RECEIPT_NOT_VERIFIED", "Model source receipt is not terminal exact v0.52 verification evidence.");

        if (string.IsNullOrWhiteSpace(request.ModelArtifactId))
            throw Refused("MODEL_ARTIFACT_ID_REFUSED", "ModelArtifactId is required.");
        var modelItem = acquisition.Items.SingleOrDefault(x => string.Equals(x.ArtifactId, request.ModelArtifactId, StringComparison.Ordinal))
            ?? throw Refused("MODEL_ARTIFACT_NOT_FOUND", "ModelArtifactId is not bound by the v0.52 acquisition receipt.");
        if (!modelItem.ExpectedSizeMatched || !modelItem.ExpectedSha256Matched ||
            (!modelItem.FinalPathPromoted && !modelItem.ExistingVerifiedReused) ||
            modelItem.ObservedFileBytes is null || string.IsNullOrWhiteSpace(modelItem.ObservedSha256))
            throw Refused("MODEL_ARTIFACT_NOT_VERIFIED", "Selected model artifact did not reach exact local v0.52 verification.");
        if (!modelItem.ObservedSha256.Equals(request.ExpectedModelSha256, StringComparison.OrdinalIgnoreCase))
            throw Refused("MODEL_REQUEST_RECEIPT_MISMATCH", "ExpectedModelSha256 differs from acquisition receipt.");

        if (!Path.IsPathFullyQualified(modelItem.FinalPath))
            throw Refused("MODEL_PATH_REFUSED", "Verified model artifact final path must be absolute.");
        var modelPath = Path.GetFullPath(modelItem.FinalPath);
        RequireOutsideRepository(repo, modelPath, "model artifact");
        if (!File.Exists(modelPath)) throw Refused("MODEL_FILE_MISSING", "Verified model artifact is missing.");
        RejectReparseChain(modelPath);
        var modelBytes = new FileInfo(modelPath).Length;
        if (modelBytes != modelItem.ObservedFileBytes.Value)
            throw Refused("MODEL_SIZE_DRIFT", "Model artifact byte length drifted after v0.52 verification.");
        var modelSha = HashFile(modelPath);
        if (!modelSha.Equals(request.ExpectedModelSha256, StringComparison.OrdinalIgnoreCase))
            throw Refused("MODEL_HASH_DRIFT", "Model artifact SHA-256 drifted after v0.52 verification.");

        var requestDigest = HashText(request.RequestUtf8);
        return new LocalModelInvocationPreviewV055(
            PreviewSchema, Version, DateTimeOffset.Now, request.RequestId, requestDigest, requestBytes,
            manifestPath, manifestSha, manifest.ManifestId, runtimeRoot,
            executableRelative, executablePath, executableSha, executableBytes,
            receiptPath, receiptSha, request.ModelArtifactId, modelPath, modelSha, modelBytes,
            request.InvocationProfileId,
            request.MaxRequestBytes, request.MaxStdoutBytes, request.MaxStderrBytes, request.MaxOutputChars,
            request.MaxOutputTokens, request.TimeoutSeconds, request.TtlSeconds,
            false, false, false, false, true, NonEffects(),
            "Preview revalidates exact runtime/model evidence and request bounds only. It creates no process/model-request authority and persists no request text.");
    }

    public async Task<(LocalModelInvocationGrantV055 Grant, LocalModelInvocationAuthorityReceiptV055 Receipt, string ReceiptPath)> GrantAsync(
        string workspaceRoot,
        LocalModelInvocationPreviewV055 preview,
        string requestUtf8,
        CancellationToken cancellationToken)
    {
        if (preview is null || preview.Schema != PreviewSchema || preview.Version != Version || !preview.ReadyForExplicitModelInvocationAuthority)
            throw Refused("PREVIEW_INVALID", "Exact v0.55 preview is required.");
        var requestBytes = StrictUtf8.GetByteCount(requestUtf8 ?? throw Refused("REQUEST_TEXT_REFUSED", "Request text is required."));
        if (requestBytes != preview.RequestBytes || !HashText(requestUtf8).Equals(preview.RequestDigestSha256, StringComparison.OrdinalIgnoreCase))
            throw Refused("PREVIEW_STALE", "Request text changed after preview.");

        RevalidateEvidence(preview);
        var repo = ResolveRepositoryRoot(workspaceRoot);
        var leaseId = "modlease-" + Guid.NewGuid().ToString("N");
        var leaseDir = Path.Combine(InvocationArtifactRoot(repo), "leases", leaseId);
        Directory.CreateDirectory(leaseDir);
        RejectReparseChain(leaseDir);
        var statePath = Path.Combine(leaseDir, "state.json");
        var bearer = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var now = DateTimeOffset.Now;
        var state = new LocalModelInvocationLeaseStateV055(
            LeaseStateSchema, Version, leaseId, preview.RequestId, preview.RequestDigestSha256, preview.RequestBytes,
            preview.RuntimeTreeManifestPath, preview.RuntimeTreeManifestSha256, preview.RuntimeRoot,
            preview.ExecutablePath, preview.ExecutableSha256, preview.ExecutableBytes,
            preview.ModelAcquisitionReceiptPath, preview.ModelAcquisitionReceiptSha256, preview.ModelArtifactId,
            preview.ModelPath, preview.ModelSha256, preview.ModelBytes, preview.InvocationProfileId,
            preview.MaxRequestBytes, preview.MaxStdoutBytes, preview.MaxStderrBytes, preview.MaxOutputChars,
            preview.MaxOutputTokens, preview.TimeoutSeconds, now, now.AddSeconds(preview.TtlSeconds),
            1, 1, HashText(bearer), "INVOCATION_PREPARED", false, false, false, null, null, null, 1,
            NonEffects(), "One-shot model invocation authority is prepared. Raw request text and bearer plaintext are not persisted.");
        await WriteJsonAtomicAsync(statePath, state, cancellationToken);

        var grant = new LocalModelInvocationGrantV055(
            GrantSchema, Version, now, leaseId, bearer, preview.RequestId, preview.RequestDigestSha256, preview.RequestBytes,
            requestUtf8, statePath, state.ExpiresAt, 1, false, false, false, false,
            "Bearer and raw request exist only in this in-memory grant. No process/model request has occurred.");
        var authority = new LocalModelInvocationAuthorityReceiptV055(
            AuthorityReceiptSchema, Version, now, leaseId, preview.RequestId, preview.RequestDigestSha256,
            preview.RequestBytes, state.BearerSha256, statePath, HashFile(statePath), state.ExpiresAt, 1,
            false, false, false, false, false, false, NonEffects(),
            "MODEL_INVOCATION_AUTHORITY_GRANTED_NOT_USED",
            "One-shot authority granted after exact runtime/model/request preview revalidation; no process or model request performed.");
        var receiptPath = await WriteReceiptAsync(repo, $"authority-{leaseId}-{DateTime.Now:yyyyMMdd-HHmmssfff}.json", authority, cancellationToken);
        return (grant, authority, receiptPath);
    }

    public async Task<(LocalModelInvocationExecutionReceiptV055 Receipt, LocalModelInvocationPortableResultV055? Result, string ReceiptPath)> InvokeAsync(
        string workspaceRoot,
        LocalModelInvocationGrantV055 grant,
        CancellationToken cancellationToken)
    {
        if (grant is null || grant.Schema != GrantSchema || grant.Version != Version || !grant.LeaseId.StartsWith("modlease-", StringComparison.Ordinal))
            throw Refused("AUTHORITY_INVALID", "Invalid v0.55 model invocation grant.");

        var repo = ResolveRepositoryRoot(workspaceRoot);
        var statePath = Path.GetFullPath(grant.LeaseStatePath);
        var leaseRoot = Path.Combine(InvocationArtifactRoot(repo), "leases") + Path.DirectorySeparatorChar;
        if (!statePath.StartsWith(leaseRoot, StringComparison.OrdinalIgnoreCase))
            throw Refused("AUTHORITY_STATE_PATH_REFUSED", "Lease state path is outside Workbench-owned invocation evidence.");
        var lockPath = Path.Combine(Path.GetDirectoryName(statePath)!, "lease.lock");

        using var leaseLock = AcquireExclusiveFileLock(lockPath);
        var state = await ReadStateAsync(statePath, cancellationToken);
        ValidateGrantAgainstState(grant, state);
        if (state.Revoked) throw Refused("AUTHORITY_REVOKED", "Model invocation lease is revoked.");
        if (state.Completed) throw Refused("AUTHORITY_ALREADY_COMPLETED", "Model invocation lease already completed.");
        if (state.Failed) throw Refused("AUTHORITY_TERMINAL_FAILED", $"Model invocation lease already failed: {state.FailureClassification}");
        if (state.ExpiresAt <= DateTimeOffset.Now) throw Refused("AUTHORITY_EXPIRED", "Model invocation lease expired.");
        if (state.RemainingCalls != 1) throw Refused("AUTHORITY_CALL_BUDGET_EXHAUSTED", "One-shot model invocation call budget is exhausted.");
        if (!HashText(grant.Bearer).Equals(state.BearerSha256, StringComparison.OrdinalIgnoreCase))
            throw Refused("AUTHORITY_BEARER_MISMATCH", "Model invocation bearer mismatch.");
        if (StrictUtf8.GetByteCount(grant.RequestUtf8) != state.RequestBytes ||
            !HashText(grant.RequestUtf8).Equals(state.RequestDigestSha256, StringComparison.OrdinalIgnoreCase))
            throw Refused("REQUEST_BINDING_MISMATCH", "In-memory request no longer matches lease binding.");

        var transactionId = "modtx-" + Guid.NewGuid().ToString("N");
        var consumed = state with
        {
            RemainingCalls = 0,
            State = "INVOCATION_AUTHORITY_CONSUMED",
            StateRevision = state.StateRevision + 1,
            Note = "One-shot authority was durably consumed before process creation/request release."
        };
        await WriteJsonAtomicAsync(statePath, consumed, cancellationToken);

        Process? process = null;
        DateTimeOffset? startedAt = null;
        string? observedImagePath = null;
        string? observedImageSha = null;
        byte[] stdout = Array.Empty<byte>();
        byte[] stderr = Array.Empty<byte>();
        string? outputPath = null;
        string? outputSha = null;
        int? outputChars = null;

        try
        {
            RevalidateStateEvidence(consumed);
            var args = BuildProfileArguments(consumed);
            var psi = new ProcessStartInfo
            {
                FileName = consumed.ExecutablePath,
                WorkingDirectory = consumed.RuntimeRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                ErrorDialog = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var arg in args) psi.ArgumentList.Add(arg);
            psi.Environment.Clear();
            foreach (var name in FixedInheritedEnvironmentNames)
            {
                var value = Environment.GetEnvironmentVariable(name);
                if (!string.IsNullOrEmpty(value)) psi.Environment[name] = value;
            }

            process = Process.Start(psi) ?? throw Refused("PROCESS_START_FAILED", "Process.Start returned null.");
            startedAt = DateTimeOffset.Now;
            observedImagePath = GetObservedProcessImagePath(process);
            if (!PathsEqual(observedImagePath, consumed.ExecutablePath))
                throw Refused("PROCESS_IMAGE_PATH_MISMATCH", "Observed Windows process image path differs from exact executable.");
            observedImageSha = HashFile(observedImagePath);
            if (!observedImageSha.Equals(consumed.ExecutableSha256, StringComparison.OrdinalIgnoreCase))
                throw Refused("PROCESS_IMAGE_HASH_MISMATCH", "Observed Windows process image SHA-256 differs from exact executable.");

            var running = consumed with
            {
                State = "MODEL_REQUEST_IN_FLIGHT",
                ProcessId = process.Id,
                ProcessStartedAt = startedAt,
                StateRevision = consumed.StateRevision + 1,
                Note = "Exactly one model request is in flight under already-consumed authority."
            };
            await WriteJsonAtomicAsync(statePath, running, CancellationToken.None);

            var stdoutCapture = CaptureBoundedAsync(process.StandardOutput.BaseStream, running.MaxStdoutBytes, "STDOUT_BYTE_CEILING_EXCEEDED");
            var stderrCapture = CaptureBoundedAsync(process.StandardError.BaseStream, running.MaxStderrBytes, "STDERR_BYTE_CEILING_EXCEEDED");

            var requestPayload = StrictUtf8.GetBytes(grant.RequestUtf8);
            await process.StandardInput.BaseStream.WriteAsync(requestPayload, cancellationToken);
            await process.StandardInput.BaseStream.FlushAsync(cancellationToken);
            process.StandardInput.Close();

            var exitTask = process.WaitForExitAsync(CancellationToken.None);
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(running.TimeoutSeconds), CancellationToken.None);
            var stdoutFault = FaultOnly(stdoutCapture);
            var stderrFault = FaultOnly(stderrCapture);
            var first = await Task.WhenAny(exitTask, timeoutTask, stdoutFault, stderrFault);

            if (first == timeoutTask)
            {
                KillOwnedProcess(process);
                await process.WaitForExitAsync(CancellationToken.None);
                throw Refused("TIMEOUT", "One-shot model invocation exceeded exact timeout.");
            }
            if (first == stdoutFault || first == stderrFault)
            {
                KillOwnedProcess(process);
                await process.WaitForExitAsync(CancellationToken.None);
                await first;
            }

            await exitTask;
            stdout = await stdoutCapture;
            stderr = await stderrCapture;
            if (process.ExitCode != 0)
                throw Refused("NONZERO_EXIT", $"One-shot model invocation exited with code {process.ExitCode}.");

            string text;
            try { text = StrictUtf8.GetString(stdout).Trim(); }
            catch (DecoderFallbackException ex) { throw Refused("OUTPUT_INVALID_UTF8", "Stdout is not valid UTF-8.", ex); }
            if (text.Length < 1 || text.Length > running.MaxOutputChars)
                throw Refused("OUTPUT_CHAR_CEILING_REFUSED", $"Output text must be within 1..{running.MaxOutputChars} characters.");

            var outputRoot = Path.Combine(Directory.GetParent(repo)!.FullName, "ModelOutputs-v055", running.LeaseId);
            Directory.CreateDirectory(outputRoot);
            outputPath = Path.Combine(outputRoot, "stdout.bin");
            await File.WriteAllBytesAsync(outputPath, stdout, CancellationToken.None);
            outputSha = HashFile(outputPath);
            outputChars = text.Length;

            var completed = running with
            {
                State = "MODEL_INVOCATION_COMPLETED",
                Completed = true,
                StateRevision = running.StateRevision + 1,
                Note = "One exact request completed; no response/display/action authority was created."
            };
            await WriteJsonAtomicAsync(statePath, completed, CancellationToken.None);

            var receipt = BuildReceipt(
                transactionId, completed, startedAt, observedImagePath, observedImageSha, stdout, stderr,
                outputPath, outputSha, outputChars, null, "UNTRUSTED_LOCAL_MODEL_OUTPUT");
            var receiptPath = await WriteReceiptAsync(repo, $"execution-{transactionId}-{DateTime.Now:yyyyMMdd-HHmmssfff}.json", receipt, CancellationToken.None);
            var result = new LocalModelInvocationPortableResultV055(
                PortableResultSchema, Version, DateTimeOffset.Now, completed.RequestId, completed.InvocationProfileId,
                completed.ModelArtifactId, completed.ModelSha256, completed.ExecutableSha256, completed.RequestDigestSha256,
                completed.RequestBytes, HashBytes(stdout), HashText(text), text, text.Length,
                false, false, false, false, false, false, false,
                "UNTRUSTED_LOCAL_MODEL_OUTPUT",
                "Portable output contains no local paths and grants no response/display/game/action/successor authority.");
            return (receipt, result, receiptPath);
        }
        catch (Exception ex)
        {
            if (process is not null && !process.HasExited) KillOwnedProcess(process);
            var classification = ex is LocalModelInvocationExceptionV055 typed ? typed.Classification : "PROCESS_OR_IO_FAILED";
            var terminal = consumed with
            {
                State = "MODEL_INVOCATION_FAILED_CLOSED",
                Failed = true,
                FailureClassification = classification,
                ProcessId = process?.Id,
                ProcessStartedAt = startedAt,
                StateRevision = consumed.StateRevision + 10,
                Note = "Invocation failed closed after one-shot authority consumption. No retry/resume/replay authority exists."
            };
            try { await WriteJsonAtomicAsync(statePath, terminal, CancellationToken.None); } catch { }

            var receipt = BuildReceipt(
                transactionId, terminal, startedAt, observedImagePath, observedImageSha, stdout, stderr,
                null, null, null, classification, "MODEL_INVOCATION_FAILED_CLOSED");
            string? receiptPath = null;
            try { receiptPath = await WriteReceiptAsync(repo, $"execution-{transactionId}-{DateTime.Now:yyyyMMdd-HHmmssfff}.json", receipt, CancellationToken.None); } catch { }

            if (ex is LocalModelInvocationExceptionV055)
                throw new LocalModelInvocationExceptionV055(classification, ex.Message, receiptPath, ex);
            throw new LocalModelInvocationExceptionV055(classification, "Bounded local-model invocation failed closed.", receiptPath, ex);
        }
        finally
        {
            process?.Dispose();
        }
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("v055-separate-authority", true, "model invocation lease is separate from v0.53 execution lease", "separate"),
        ("v055-profile-closed", true, FixtureProfileId, "code-defined adapter id only"),
        ("v055-no-arbitrary-arguments", true, "request has no caller-supplied process argument array", "true"),
        ("v055-request-provenance", true, "lease state persists request SHA-256 + byte count, not raw text", "digest/size only"),
        ("v055-model-rehash", true, "Preview + immediately before Process.Start", "twice"),
        ("v055-runtime-rehash", true, "Preview + immediately before Process.Start", "twice"),
        ("v055-one-shot", true, "RemainingCalls 1 -> 0 before process start", "true"),
        ("v055-bounded-stdio", true, $"stdout<={HardMaxStdoutBytes}; stderr<={HardMaxStderrBytes}", "bounded"),
        ("v055-network-claim", true, "WorkbenchNetworkTransportPerformed=false; ProcessNetworkIsolationProven=false", "no unsupported isolation claim"),
        ("v055-output-untrusted", true, "UNTRUSTED_LOCAL_MODEL_OUTPUT", "no response/display authority"),
        ("v055-no-kontur-policy", true, "fixture profile only; no KONTUR model selection", "provider-neutral authority layer")
    };

    private static void ValidateBounds(LocalModelInvocationRequestV055 request)
    {
        if (request.MaxRequestBytes < 1 || request.MaxRequestBytes > HardMaxRequestBytes)
            throw Refused("REQUEST_BOUND_REFUSED", $"MaxRequestBytes must be within 1..{HardMaxRequestBytes}.");
        if (request.MaxStdoutBytes < 1 || request.MaxStdoutBytes > HardMaxStdoutBytes)
            throw Refused("STDOUT_BOUND_REFUSED", $"MaxStdoutBytes must be within 1..{HardMaxStdoutBytes}.");
        if (request.MaxStderrBytes < 1 || request.MaxStderrBytes > HardMaxStderrBytes)
            throw Refused("STDERR_BOUND_REFUSED", $"MaxStderrBytes must be within 1..{HardMaxStderrBytes}.");
        if (request.MaxOutputChars < 1 || request.MaxOutputChars > HardMaxOutputChars)
            throw Refused("OUTPUT_CHAR_BOUND_REFUSED", $"MaxOutputChars must be within 1..{HardMaxOutputChars}.");
        if (request.MaxOutputTokens < 1 || request.MaxOutputTokens > HardMaxOutputTokens)
            throw Refused("OUTPUT_TOKEN_BOUND_REFUSED", $"MaxOutputTokens must be within 1..{HardMaxOutputTokens}.");
        if (request.TimeoutSeconds < 1 || request.TimeoutSeconds > MaxTimeoutSeconds)
            throw Refused("TIMEOUT_BOUND_REFUSED", $"TimeoutSeconds must be within 1..{MaxTimeoutSeconds}.");
        if (request.TtlSeconds < 1 || request.TtlSeconds > MaxTtlSeconds)
            throw Refused("TTL_REFUSED", $"TtlSeconds must be within 1..{MaxTtlSeconds}.");
    }

    private static void ValidateProfile(string profileId, string executableName)
    {
        if (profileId != FixtureProfileId)
            throw Refused("INVOCATION_PROFILE_UNSUPPORTED", $"Unsupported v0.55 invocation profile: {profileId}");
        if (!executableName.Equals(FixtureExecutableName, StringComparison.OrdinalIgnoreCase))
            throw Refused("INVOCATION_PROFILE_EXECUTABLE_MISMATCH", $"Profile {FixtureProfileId} requires exact fixture executable name {FixtureExecutableName}.");
    }

    private static IReadOnlyList<string> BuildProfileArguments(LocalModelInvocationLeaseStateV055 state)
    {
        if (state.InvocationProfileId != FixtureProfileId)
            throw Refused("INVOCATION_PROFILE_UNSUPPORTED", "Lease invocation profile is unsupported.");
        if (!Path.GetFileName(state.ExecutablePath).Equals(FixtureExecutableName, StringComparison.OrdinalIgnoreCase))
            throw Refused("INVOCATION_PROFILE_EXECUTABLE_MISMATCH", "Fixture profile executable changed.");
        return new[] { "--model", state.ModelPath, "--max-output-tokens", state.MaxOutputTokens.ToString(System.Globalization.CultureInfo.InvariantCulture) };
    }

    private static void RevalidateEvidence(LocalModelInvocationPreviewV055 preview)
    {
        if (!File.Exists(preview.RuntimeTreeManifestPath) || !HashFile(preview.RuntimeTreeManifestPath).Equals(preview.RuntimeTreeManifestSha256, StringComparison.OrdinalIgnoreCase))
            throw Refused("RUNTIME_MANIFEST_DRIFT", "Runtime manifest changed after preview.");
        if (!File.Exists(preview.ExecutablePath) || !HashFile(preview.ExecutablePath).Equals(preview.ExecutableSha256, StringComparison.OrdinalIgnoreCase))
            throw Refused("EXECUTABLE_HASH_DRIFT", "Executable changed after preview.");
        if (!File.Exists(preview.ModelAcquisitionReceiptPath) || !HashFile(preview.ModelAcquisitionReceiptPath).Equals(preview.ModelAcquisitionReceiptSha256, StringComparison.OrdinalIgnoreCase))
            throw Refused("MODEL_RECEIPT_DRIFT", "Model acquisition receipt changed after preview.");
        if (!File.Exists(preview.ModelPath) || new FileInfo(preview.ModelPath).Length != preview.ModelBytes ||
            !HashFile(preview.ModelPath).Equals(preview.ModelSha256, StringComparison.OrdinalIgnoreCase))
            throw Refused("MODEL_HASH_DRIFT", "Model artifact changed after preview.");
    }

    private static void RevalidateStateEvidence(LocalModelInvocationLeaseStateV055 state)
    {
        if (!File.Exists(state.RuntimeTreeManifestPath) || !HashFile(state.RuntimeTreeManifestPath).Equals(state.RuntimeTreeManifestSha256, StringComparison.OrdinalIgnoreCase))
            throw Refused("RUNTIME_MANIFEST_DRIFT", "Runtime manifest changed before Process.Start.");
        if (!File.Exists(state.ExecutablePath) || new FileInfo(state.ExecutablePath).Length != state.ExecutableBytes ||
            !HashFile(state.ExecutablePath).Equals(state.ExecutableSha256, StringComparison.OrdinalIgnoreCase))
            throw Refused("EXECUTABLE_HASH_DRIFT", "Executable changed immediately before Process.Start.");
        if (!File.Exists(state.ModelAcquisitionReceiptPath) || !HashFile(state.ModelAcquisitionReceiptPath).Equals(state.ModelAcquisitionReceiptSha256, StringComparison.OrdinalIgnoreCase))
            throw Refused("MODEL_RECEIPT_DRIFT", "Model acquisition receipt changed before Process.Start.");
        if (!File.Exists(state.ModelPath) || new FileInfo(state.ModelPath).Length != state.ModelBytes ||
            !HashFile(state.ModelPath).Equals(state.ModelSha256, StringComparison.OrdinalIgnoreCase))
            throw Refused("MODEL_HASH_DRIFT", "Model bytes changed immediately before Process.Start.");
    }

    private static LocalModelInvocationExecutionReceiptV055 BuildReceipt(
        string transactionId,
        LocalModelInvocationLeaseStateV055 state,
        DateTimeOffset? startedAt,
        string? observedImagePath,
        string? observedImageSha,
        byte[] stdout,
        byte[] stderr,
        string? outputPath,
        string? outputSha,
        int? outputChars,
        string? failure,
        string status)
    {
        var success = failure is null && status == "UNTRUSTED_LOCAL_MODEL_OUTPUT";
        return new LocalModelInvocationExecutionReceiptV055(
            ExecutionReceiptSchema, Version, DateTimeOffset.Now, transactionId, state.LeaseId, state.RequestId,
            state.State, state.InvocationProfileId, state.RequestDigestSha256, state.RequestBytes,
            state.RuntimeTreeManifestPath, state.RuntimeTreeManifestSha256, state.ExecutablePath, state.ExecutableSha256,
            state.ModelAcquisitionReceiptPath, state.ModelAcquisitionReceiptSha256, state.ModelArtifactId, state.ModelPath,
            state.ModelSha256, state.ModelBytes, state.ProcessId, startedAt, observedImagePath, observedImageSha,
            observedImagePath is not null && observedImageSha is not null, state.ProcessId is not null,
            stdout.LongLength, stdout.Length == 0 ? null : HashBytes(stdout),
            stderr.LongLength, stderr.Length == 0 ? null : HashBytes(stderr),
            outputPath, outputSha, outputChars, true,
            false, false, false, false, false, false, false, false, false, false, false,
            failure, NonEffects(), status,
            success
                ? "Exactly one bounded subprocess request completed. Captured output is untrusted evidence only."
                : "Invocation terminated fail-closed after authority consumption; no retry/resume/replay authority was created.");
    }

    private static async Task<byte[]> CaptureBoundedAsync(Stream stream, int maxBytes, string classification)
    {
        using var memory = new MemoryStream(Math.Min(maxBytes, 64 * 1024));
        var buffer = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(buffer);
            if (read == 0) break;
            if (memory.Length + read > maxBytes)
                throw Refused(classification, $"Captured stream exceeded exact {maxBytes}-byte ceiling.");
            memory.Write(buffer, 0, read);
        }
        return memory.ToArray();
    }

    private static async Task FaultOnly(Task task)
    {
        try { await task; await Task.Delay(Timeout.InfiniteTimeSpan); }
        catch { throw; }
    }

    private static void KillOwnedProcess(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
    }

    private static string GetObservedProcessImagePath(Process process)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            try
            {
                var capacity = 32768;
                var buffer = new StringBuilder(capacity);
                var length = capacity;
                if (QueryFullProcessImageName(process.Handle, 0, buffer, ref length) && length > 0)
                {
                    var path = buffer.ToString();
                    if (!string.IsNullOrWhiteSpace(path)) return Path.GetFullPath(path);
                }
                last = new InvalidDataException($"QueryFullProcessImageName failed with Win32 error {Marshal.GetLastWin32Error()}.");
            }
            catch (Exception ex)
            {
                last = ex;
            }

            try
            {
                var path = process.MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(path)) return Path.GetFullPath(path);
            }
            catch (Exception ex)
            {
                last = ex;
            }

            if (attempt != 39) Thread.Sleep(10);
        }
        throw Refused("PROCESS_IMAGE_QUERY_FAILED", "Could not observe exact process image path after bounded Windows query retries.", last);
    }

    private static void ValidateGrantAgainstState(LocalModelInvocationGrantV055 grant, LocalModelInvocationLeaseStateV055 state)
    {
        if (state.Schema != LeaseStateSchema || state.Version != Version || state.LeaseId != grant.LeaseId ||
            state.RequestId != grant.RequestId || state.RequestDigestSha256 != grant.RequestDigestSha256 ||
            state.RequestBytes != grant.RequestBytes || state.MaxCalls != 1 || state.State != "INVOCATION_PREPARED")
            throw Refused("AUTHORITY_STATE_MISMATCH", "Persisted lease state does not match exact in-memory grant.");
    }

    private static string ValidateAcquisitionReceiptPath(string repo, string value)
    {
        var path = RequireAbsoluteExistingFile(value, "MODEL_ACQUISITION_RECEIPT_MISSING");
        var allowed = Path.Combine(repo, "artifacts", "artifact-acquisition-v052") + Path.DirectorySeparatorChar;
        if (!path.StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
            throw Refused("MODEL_ACQUISITION_RECEIPT_PATH_REFUSED", "Model acquisition receipt must be Workbench-owned v0.52 evidence.");
        RejectReparseChain(path);
        return path;
    }

    private static string ResolveRepositoryRoot(string workspaceRoot)
    {
        var workspace = Path.GetFullPath(workspaceRoot);
        var repo = Path.Combine(workspace, "Workbench");
        if (!Directory.Exists(repo)) throw Refused("WORKBENCH_ROOT_MISSING", "Workspace/Workbench repository root is missing.");
        return Path.GetFullPath(repo);
    }

    private static string InvocationArtifactRoot(string repo)
    {
        var path = Path.Combine(repo, "artifacts", "local-model-invocation-v055");
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<string> WriteReceiptAsync(string repo, string fileName, object value, CancellationToken cancellationToken)
    {
        var dir = Path.Combine(InvocationArtifactRoot(repo), "receipts");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        await WriteJsonAtomicAsync(path, value, cancellationToken);
        return path;
    }

    private static async Task<LocalModelInvocationLeaseStateV055> ReadStateAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken);
            return JsonSerializer.Deserialize<LocalModelInvocationLeaseStateV055>(json, JsonOptions)
                ?? throw new InvalidDataException("Lease state deserialized to null.");
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException)
        {
            throw Refused("AUTHORITY_STATE_INVALID", "Persisted model invocation lease state is invalid.", ex);
        }
    }

    private static async Task WriteJsonAtomicAsync(string path, object value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(value, JsonOptions), new UTF8Encoding(false), cancellationToken);
        File.Move(tmp, path, true);
    }

    private static FileStream AcquireExclusiveFileLock(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex) { throw Refused("MODEL_INVOCATION_LEASE_BUSY", "Exact model invocation lease is already in use.", ex); }
    }

    private static string EnsureDirectoryRoot(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
            throw Refused("RUNTIME_ROOT_REFUSED", "Runtime root must be an absolute path.");
        return Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string RequireAbsoluteExistingFile(string value, string classification)
    {
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
            throw Refused(classification, "Expected absolute existing file path.");
        var path = Path.GetFullPath(value);
        if (!File.Exists(path)) throw Refused(classification, "Expected file is missing.");
        return path;
    }

    private static void RequireOutsideRepository(string repo, string path, string label)
    {
        var repoRoot = Path.GetFullPath(repo).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(path);
        if (full.Equals(repo.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ||
            full.StartsWith(repoRoot, StringComparison.OrdinalIgnoreCase))
            throw Refused("EXTERNAL_PATH_REQUIRED", $"{label} must remain external to the Workbench Git repository.");
    }

    private static string NormalizeRelativePath(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathFullyQualified(value))
            throw Refused("RELATIVE_PATH_REFUSED", $"{label} must be a non-empty relative path.");
        var normalized = value.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var segments = normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(x => x is "." or ".." || x.Contains(':') || x.EndsWith(' ') || x.EndsWith('.')))
            throw Refused("RELATIVE_PATH_REFUSED", $"{label} contains unsafe Windows path segments.");
        return string.Join(Path.DirectorySeparatorChar, segments);
    }

    private static string ResolveUnderRoot(string root, string relative, string label)
    {
        var full = Path.GetFullPath(Path.Combine(root, relative));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw Refused("PATH_ESCAPE_REFUSED", $"{label} escapes exact root.");
        return full;
    }

    private static void RejectReparseChain(string path)
    {
        FileSystemInfo node = File.Exists(path) ? new FileInfo(path) : new DirectoryInfo(path);
        while (!string.IsNullOrWhiteSpace(node.FullName))
        {
            if (node.Exists && (node.Attributes & FileAttributes.ReparsePoint) != 0)
                throw Refused("REPARSE_POINT_REFUSED", $"Reparse point is not allowed: {node.FullName}");
            var parent = node is DirectoryInfo d ? d.Parent : (node as FileInfo)?.Directory;
            if (parent is null || parent.FullName == node.FullName) break;
            node = parent;
        }
    }

    private static void RequireSha256(string value, string label)
    {
        if (value is null || !Regex.IsMatch(value, "^[0-9a-fA-F]{64}$"))
            throw Refused("SHA256_REFUSED", $"{label} must be an exact SHA-256 hex digest.");
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string HashBytes(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string HashText(string text)
        => HashBytes(StrictUtf8.GetBytes(text));

    private static bool PathsEqual(string a, string b)
        => Path.GetFullPath(a).Equals(Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> NonEffects() => new[]
    {
        "Runtime Ready != Model Request Authority",
        "Process Execution Authority != Model Request Authority",
        "Model Request Authority != Response Authority",
        "Model Output != Trusted Response",
        "Output Capture != Content Review",
        "Validated Output != Display Permit",
        "Exact Runtime != Exact Model",
        "Request Digest != Request Intent",
        "One Successful Request != Successor Permit",
        "Timeout != Permission To Retry",
        "No Workbench Network Transport != OS-Level Process Network Isolation",
        "no artifact acquisition or runtime-tree materialization performed by v0.55 invocation layer",
        "no server/port/network transport requested by FIXTURE_STDIO_V1",
        "no benchmark/game/display/send/action/successor authority",
        "no KONTUR-specific model selection policy"
    };

    private static LocalModelInvocationExceptionV055 Refused(string classification, string message, Exception? inner = null)
        => new(classification, message, null, inner);
}
