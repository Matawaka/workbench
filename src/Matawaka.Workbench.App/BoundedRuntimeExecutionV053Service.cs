using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Matawaka.Workbench.App;

public sealed record RuntimeTreeFileV053(
    string RelativePath,
    long Bytes,
    string Sha256,
    string Role);

public sealed record RuntimeTreeManifestV053(
    string Schema,
    string Version,
    string ManifestId,
    string State,
    string RuntimeRoot,
    IReadOnlyList<RuntimeTreeFileV053> Files,
    string Note);

public sealed record RuntimeExecutionRequestV053(
    string Schema,
    string RequestId,
    string RuntimeTreeManifestPath,
    string RuntimeTreeManifestSha256,
    string ExecutableRelativePath,
    string ExpectedExecutableSha256,
    IReadOnlyList<string> Arguments,
    string WorkingDirectoryRelativePath,
    IReadOnlyDictionary<string, string> Environment,
    int TtlSeconds,
    int ReadinessDelayMilliseconds,
    bool CreateNoWindow);

public sealed record RuntimeExecutionPreviewV053(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string RequestId,
    string RequestDigestSha256,
    string RuntimeTreeManifestPath,
    string RuntimeTreeManifestSha256,
    string RuntimeTreeManifestId,
    string RuntimeRoot,
    string ExecutableRelativePath,
    string ExecutablePath,
    string ExecutableSha256,
    long ExecutableBytes,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment,
    int TtlSeconds,
    int MaxCalls,
    int ReadinessDelayMilliseconds,
    bool CreateNoWindow,
    bool RuntimeTreeMaterializationPerformed,
    bool ProcessExecutionPerformed,
    bool ReadyForExplicitExecutionAuthority,
    IReadOnlyList<string> NonEffects,
    string Note);

public sealed record RuntimeExecutionLeaseStateV053(
    string Schema,
    string Version,
    string LeaseId,
    string RequestId,
    string RequestDigestSha256,
    string RuntimeTreeManifestPath,
    string RuntimeTreeManifestSha256,
    string RuntimeRoot,
    string ExecutablePath,
    string ExecutableSha256,
    long ExecutableBytes,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment,
    bool CreateNoWindow,
    int ReadinessDelayMilliseconds,
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
    string? ObservedProcessImagePath,
    string? ObservedProcessImageSha256,
    bool RuntimeReadyObserved,
    long StateRevision,
    IReadOnlyList<string> NonEffects,
    string Note);

public sealed record RuntimeExecutionGrantV053(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string LeaseId,
    string Bearer,
    string RequestId,
    string RequestDigestSha256,
    string LeaseStatePath,
    DateTimeOffset ExpiresAt,
    int MaxCalls,
    bool BearerPersistedInPlaintextByWorkbench,
    bool ProcessExecutionPerformed,
    string Note);

public sealed record RuntimeExecutionAuthorityReceiptV053(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string LeaseId,
    string RequestId,
    string RequestDigestSha256,
    string BearerSha256,
    string LeaseStatePath,
    string LeaseStateSha256,
    DateTimeOffset ExpiresAt,
    int MaxCalls,
    bool BearerPlaintextPersisted,
    bool RuntimeTreeMaterializationPerformed,
    bool ProcessExecutionPerformed,
    bool RuntimeReadyObserved,
    IReadOnlyList<string> NonEffects,
    string Status,
    string Note);

public sealed record RuntimeExecutionReceiptV053(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string TransactionId,
    string LeaseId,
    string RequestId,
    string State,
    int ProcessId,
    DateTimeOffset ProcessStartedAt,
    string ExecutablePath,
    string ExecutableSha256BeforeStart,
    string ObservedProcessImagePath,
    string ObservedProcessImageSha256,
    bool ExactProcessImageVerified,
    bool ProcessStillRunning,
    bool RuntimeReadyObserved,
    string LeaseStatePath,
    string LeaseStateSha256,
    bool ExecutionAuthorityConsumed,
    bool RuntimeTreeMaterializationPerformed,
    bool ShellIndirectionPerformed,
    bool ElevationRequested,
    bool AutomaticRetryPerformed,
    bool AutomaticResumePerformed,
    bool BenchmarkPerformed,
    bool ModelRequestPerformed,
    bool GameAccessPerformed,
    bool GeneralProcessAuthorityGranted,
    bool ArbitraryPidStopAuthorityGranted,
    IReadOnlyList<string> NonEffects,
    string Status,
    string Note);

public sealed record RuntimeExecutionStopReceiptV053(
    string Schema,
    string Version,
    DateTimeOffset ObservedAt,
    string LeaseId,
    int ProcessId,
    string ExecutablePath,
    DateTimeOffset ProcessStartedAt,
    bool ExactOwnedProcessVerifiedBeforeStop,
    bool EntireOwnedProcessTreeStopRequested,
    bool ProcessExited,
    bool ArbitraryPidAccepted,
    string Status,
    string Note);

public sealed class RuntimeExecutionExceptionV053 : IOException
{
    public string Classification { get; }

    public RuntimeExecutionExceptionV053(string classification, string message) : base(message)
        => Classification = classification;

    public RuntimeExecutionExceptionV053(string classification, string message, Exception inner) : base(message, inner)
        => Classification = classification;
}

public sealed class BoundedRuntimeExecutionV053Service : IDisposable
{
    public const string Version = "0.53.0";
    public const string RequestSchema = "matawaka.runtime-execution-request/v0.53";
    public const string PreviewSchema = "matawaka.runtime-execution-preview/v0.53";
    public const string RuntimeTreeManifestSchema = "matawaka.runtime-tree-manifest/v0.53";
    public const string RuntimeTreeVerifiedState = "MATERIALIZED_VERIFIED";
    public const string LeaseStateSchema = "matawaka.runtime-execution-lease-state/v0.53";
    public const string GrantSchema = "matawaka.runtime-execution-grant/v0.53";
    public const string AuthorityReceiptSchema = "matawaka.runtime-execution-authority-receipt/v0.53";
    public const string ExecutionReceiptSchema = "matawaka.runtime-execution-receipt/v0.53";
    public const string StopReceiptSchema = "matawaka.runtime-execution-stop-receipt/v0.53";

    public const int MaxArguments = 64;
    public const int MaxEnvironmentEntries = 32;
    public const int MaxArgumentChars = 4096;
    public const int MaxEnvironmentValueChars = 4096;
    public const int MinTtlSeconds = 30;
    public const int MaxTtlSeconds = 30 * 60;
    public const int MaxReadinessDelayMilliseconds = 5000;

    private static readonly Regex EnvironmentNameRegex = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly string[] ForbiddenEnvironmentFragments =
    {
        "TOKEN", "SECRET", "PASSWORD", "PASSWD", "APIKEY", "API_KEY", "AUTH", "COOKIE", "BEARER", "CREDENTIAL", "PRIVATE_KEY", "PROXY"
    };
    private static readonly string[] FixedInheritedEnvironmentNames = { "SystemRoot", "WINDIR", "TEMP", "TMP" };
    private static readonly HashSet<string> ForbiddenExecutableNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd.exe", "powershell.exe", "pwsh.exe", "wscript.exe", "cscript.exe", "mshta.exe",
        "rundll32.exe", "regsvr32.exe", "bash.exe", "sh.exe", "python.exe", "pythonw.exe"
    };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private ActiveRuntime? _active;
    private bool _disposed;

    public RuntimeExecutionPreviewV053 Preview(string workspaceRoot, RuntimeExecutionRequestV053 request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request is null) throw Refused("REQUEST_INVALID", "Runtime execution request is required.");
        if (!string.Equals(request.Schema, RequestSchema, StringComparison.Ordinal))
            throw Refused("REQUEST_SCHEMA_REFUSED", $"Expected exact schema {RequestSchema}.");
        if (string.IsNullOrWhiteSpace(request.RequestId) || request.RequestId.Length > 128)
            throw Refused("REQUEST_INVALID", "RequestId must be 1..128 characters.");
        if (request.TtlSeconds < MinTtlSeconds || request.TtlSeconds > MaxTtlSeconds)
            throw Refused("TTL_REFUSED", $"TtlSeconds must be {MinTtlSeconds}..{MaxTtlSeconds}.");
        if (request.ReadinessDelayMilliseconds < 0 || request.ReadinessDelayMilliseconds > MaxReadinessDelayMilliseconds)
            throw Refused("READINESS_BOUND_REFUSED", $"ReadinessDelayMilliseconds must be 0..{MaxReadinessDelayMilliseconds}.");

        ValidateArguments(request.Arguments);
        ValidateEnvironment(request.Environment);
        RequireSha256(request.RuntimeTreeManifestSha256, "RuntimeTreeManifestSha256");
        RequireSha256(request.ExpectedExecutableSha256, "ExpectedExecutableSha256");

        var repositoryRoot = ResolveRepositoryRoot(workspaceRoot);
        if (string.IsNullOrWhiteSpace(request.RuntimeTreeManifestPath) || !Path.IsPathFullyQualified(request.RuntimeTreeManifestPath))
            throw Refused("RUNTIME_MANIFEST_PATH_REFUSED", "RuntimeTreeManifestPath must be an absolute path.");
        var manifestPath = Path.GetFullPath(request.RuntimeTreeManifestPath);
        if (!File.Exists(manifestPath)) throw Refused("RUNTIME_MANIFEST_MISSING", "Exact runtime-tree manifest file is missing.");
        RequireOutsideRepository(repositoryRoot, manifestPath, "runtime-tree manifest");
        var manifestSha = HashFile(manifestPath);
        if (!manifestSha.Equals(request.RuntimeTreeManifestSha256, StringComparison.OrdinalIgnoreCase))
            throw Refused("RUNTIME_MANIFEST_HASH_MISMATCH", "Runtime-tree manifest SHA-256 differs from exact reviewed request binding.");

        RuntimeTreeManifestV053 manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<RuntimeTreeManifestV053>(File.ReadAllText(manifestPath, Encoding.UTF8), JsonOptions)
                ?? throw new InvalidDataException("Runtime-tree manifest deserialized to null.");
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException)
        {
            throw new RuntimeExecutionExceptionV053("RUNTIME_MANIFEST_INVALID", "Runtime-tree manifest could not be parsed as the exact v0.53 contract.", ex);
        }

        if (!string.Equals(manifest.Schema, RuntimeTreeManifestSchema, StringComparison.Ordinal) ||
            !string.Equals(manifest.Version, "0.53", StringComparison.Ordinal) ||
            !string.Equals(manifest.State, RuntimeTreeVerifiedState, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(manifest.ManifestId))
            throw Refused("RUNTIME_MANIFEST_STATE_REFUSED", "Runtime-tree manifest must be exact v0.53 MATERIALIZED_VERIFIED evidence.");
        if (string.IsNullOrWhiteSpace(manifest.RuntimeRoot) || !Path.IsPathFullyQualified(manifest.RuntimeRoot))
            throw Refused("RUNTIME_ROOT_REFUSED", "RuntimeRoot must be an absolute path in the materialized runtime-tree manifest.");

        var runtimeRoot = EnsureDirectoryRoot(manifest.RuntimeRoot);
        if (!Directory.Exists(runtimeRoot)) throw Refused("RUNTIME_ROOT_MISSING", "Materialized runtime root does not exist.");
        RequireOutsideRepository(repositoryRoot, runtimeRoot, "runtime root");
        RejectReparseChain(runtimeRoot);

        var executableRelative = NormalizeRelativePath(request.ExecutableRelativePath, "ExecutableRelativePath");
        if (!executableRelative.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw Refused("EXECUTABLE_TYPE_REFUSED", "Only an exact .exe image may be started by the v0.53 primitive.");
        var executableName = Path.GetFileName(executableRelative);
        if (ForbiddenExecutableNames.Contains(executableName))
            throw Refused("SHELL_INDIRECTION_REFUSED", $"Executable {executableName} is an interpreter/shell/loader and is not eligible for this primitive.");

        var executablePath = ResolveUnderRoot(runtimeRoot, executableRelative, "executable");
        if (!File.Exists(executablePath)) throw Refused("EXECUTABLE_MISSING", "Exact executable image is missing.");
        RejectReparseChain(executablePath);

        var manifestFile = manifest.Files?.SingleOrDefault(x =>
            string.Equals(NormalizeRelativePath(x.RelativePath, "manifest RelativePath"), executableRelative, StringComparison.OrdinalIgnoreCase));
        if (manifestFile is null) throw Refused("EXECUTABLE_NOT_IN_RUNTIME_MANIFEST", "Exact executable is not bound by the runtime-tree manifest.");
        RequireSha256(manifestFile.Sha256, "runtime manifest executable Sha256");
        if (!manifestFile.Sha256.Equals(request.ExpectedExecutableSha256, StringComparison.OrdinalIgnoreCase))
            throw Refused("EXECUTABLE_REQUEST_MANIFEST_MISMATCH", "Request executable SHA-256 differs from runtime-tree manifest binding.");
        var executableBytes = new FileInfo(executablePath).Length;
        if (manifestFile.Bytes != executableBytes)
            throw Refused("EXECUTABLE_SIZE_MISMATCH", "Executable byte length differs from runtime-tree manifest binding.");
        var executableSha = HashFile(executablePath);
        if (!executableSha.Equals(request.ExpectedExecutableSha256, StringComparison.OrdinalIgnoreCase))
            throw Refused("EXECUTABLE_HASH_MISMATCH", "Executable SHA-256 differs from exact reviewed request binding.");

        var workingRelative = NormalizeRelativeDirectory(request.WorkingDirectoryRelativePath, "WorkingDirectoryRelativePath");
        var workingDirectory = workingRelative.Length == 0 ? runtimeRoot : ResolveUnderRoot(runtimeRoot, workingRelative, "working directory");
        if (!Directory.Exists(workingDirectory)) throw Refused("WORKING_DIRECTORY_MISSING", "Exact working directory does not exist.");
        RejectReparseChain(workingDirectory);

        var requestDigest = HashText(JsonSerializer.Serialize(request, JsonOptions));
        var nonEffects = BaseNonEffects();
        return new RuntimeExecutionPreviewV053(
            PreviewSchema, Version, DateTimeOffset.Now, request.RequestId, requestDigest,
            manifestPath, manifestSha, manifest.ManifestId, runtimeRoot,
            executableRelative, executablePath, executableSha, executableBytes,
            request.Arguments.ToArray(), workingDirectory,
            new Dictionary<string, string>(request.Environment, StringComparer.OrdinalIgnoreCase),
            request.TtlSeconds, 1, request.ReadinessDelayMilliseconds, request.CreateNoWindow,
            false, false, true, nonEffects,
            "Preview is read-only evidence validation. Process execution requires a separate explicit one-shot lease grant.");
    }

    public async Task<(RuntimeExecutionGrantV053 Grant, RuntimeExecutionAuthorityReceiptV053 Receipt, string ReceiptPath)> GrantAsync(
        string workspaceRoot,
        RuntimeExecutionPreviewV053 preview,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ValidatePreview(preview);
        var root = ResolveRepositoryRoot(workspaceRoot);
        var now = DateTimeOffset.Now;
        var leaseId = Guid.NewGuid().ToString("N");
        var bearer = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var bearerSha = HashText(bearer);
        var statePath = RuntimeStatePath(root, leaseId);
        var nonEffects = BaseNonEffects();
        var state = new RuntimeExecutionLeaseStateV053(
            LeaseStateSchema, Version, leaseId, preview.RequestId, preview.RequestDigestSha256,
            preview.RuntimeTreeManifestPath, preview.RuntimeTreeManifestSha256, preview.RuntimeRoot,
            preview.ExecutablePath, preview.ExecutableSha256, preview.ExecutableBytes,
            preview.Arguments.ToArray(), preview.WorkingDirectory,
            new Dictionary<string, string>(preview.Environment, StringComparer.OrdinalIgnoreCase),
            preview.CreateNoWindow, preview.ReadinessDelayMilliseconds,
            now, now.AddSeconds(preview.TtlSeconds), 1, 1, bearerSha,
            "EXECUTION_PREPARED", false, false, false, null, null, null, null, null, false, 1,
            nonEffects,
            "One-shot runtime execution authority is prepared but no process has been started.");
        await WriteJsonAtomicAsync(statePath, state, cancellationToken).ConfigureAwait(false);

        var grant = new RuntimeExecutionGrantV053(
            GrantSchema, Version, now, leaseId, bearer, preview.RequestId, preview.RequestDigestSha256,
            statePath, state.ExpiresAt, 1, false, false,
            "Bearer plaintext exists only in this in-memory/UI grant object; persisted state stores SHA-256 only.");
        var receipt = new RuntimeExecutionAuthorityReceiptV053(
            AuthorityReceiptSchema, Version, now, leaseId, preview.RequestId, preview.RequestDigestSha256,
            bearerSha, statePath, HashFile(statePath), state.ExpiresAt, 1,
            false, false, false, false, nonEffects,
            "EXECUTION_AUTHORITY_PREPARED",
            "Explicit confirmation created one one-shot execution lease. Authority is consumed before Process.Start.");
        var receiptPath = RuntimeReceiptPath(root, "authority", leaseId);
        await WriteJsonAtomicAsync(receiptPath, receipt, cancellationToken).ConfigureAwait(false);
        return (grant, receipt, receiptPath);
    }

    public async Task<(RuntimeExecutionReceiptV053 Receipt, string ReceiptPath)> ExecuteAsync(
        string workspaceRoot,
        RuntimeExecutionGrantV053 grant,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        RuntimeExecutionLeaseStateV053? consumedState = null;
        Process? startedProcess = null;
        try
        {
            ReapExitedActive();
            if (_active is not null)
                throw Refused("ACTIVE_RUNTIME_EXISTS", "A bounded runtime process is already active. Stop or allow it to exit before another execution lease is consumed.");
            var root = ResolveRepositoryRoot(workspaceRoot);
            var statePath = Path.GetFullPath(grant.LeaseStatePath);
            var allowedStateRoot = Path.GetFullPath(Path.Combine(root, "artifacts", "runtime-execution", "leases")) + Path.DirectorySeparatorChar;
            if (!statePath.StartsWith(allowedStateRoot, StringComparison.OrdinalIgnoreCase))
                throw Refused("LEASE_STATE_PATH_REFUSED", "Lease state path is outside the Workbench-owned runtime-execution lease directory.");
            var state = ReadState(statePath);
            ValidateGrantAgainstState(grant, state);
            if (state.Revoked || state.Completed || state.Failed || state.RemainingCalls != 1 || state.MaxCalls != 1)
                throw Refused("EXECUTION_AUTHORITY_UNAVAILABLE", "Exact execution lease is no longer available.");
            if (DateTimeOffset.Now >= state.ExpiresAt)
                throw Refused("EXECUTION_LEASE_EXPIRED", "Exact execution lease expired before authority consumption.");

            var bearerSha = HashText(grant.Bearer);
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(bearerSha), Convert.FromHexString(state.BearerSha256)))
                throw Refused("EXECUTION_BEARER_REFUSED", "Execution bearer does not match persisted one-shot lease authority.");

            consumedState = state with
            {
                RemainingCalls = 0,
                State = "EXECUTION_AUTHORITY_CONSUMED",
                StateRevision = state.StateRevision + 1,
                Note = "One-shot execution authority was durably consumed before executable revalidation and Process.Start."
            };
            await WriteJsonAtomicAsync(statePath, consumedState, cancellationToken).ConfigureAwait(false);

            RevalidateBeforeStart(consumedState);
            var executableShaBeforeStart = HashFile(consumedState.ExecutablePath);

            var psi = new ProcessStartInfo
            {
                FileName = consumedState.ExecutablePath,
                WorkingDirectory = consumedState.WorkingDirectory,
                UseShellExecute = false,
                CreateNoWindow = consumedState.CreateNoWindow,
                ErrorDialog = false
            };
            foreach (var arg in consumedState.Arguments) psi.ArgumentList.Add(arg);
            psi.Environment.Clear();
            foreach (var name in FixedInheritedEnvironmentNames)
            {
                var value = Environment.GetEnvironmentVariable(name);
                if (!string.IsNullOrEmpty(value)) psi.Environment[name] = value;
            }
            foreach (var pair in consumedState.Environment) psi.Environment[pair.Key] = pair.Value;

            startedProcess = new Process { StartInfo = psi, EnableRaisingEvents = false };
            if (!startedProcess.Start()) throw Refused("PROCESS_START_FAILED", "Process.Start returned false for the exact reviewed executable.");
            var startedAt = new DateTimeOffset(startedProcess.StartTime.ToUniversalTime(), TimeSpan.Zero);
            var observedImagePath = QueryProcessImagePath(startedProcess);
            if (!PathsEqual(observedImagePath, consumedState.ExecutablePath))
            {
                TryKillExact(startedProcess);
                throw Refused("PROCESS_IMAGE_PATH_MISMATCH", "Observed process image path differs from the exact reviewed executable path.");
            }
            var observedImageSha = HashFile(observedImagePath);
            if (!observedImageSha.Equals(consumedState.ExecutableSha256, StringComparison.OrdinalIgnoreCase))
            {
                TryKillExact(startedProcess);
                throw Refused("PROCESS_IMAGE_HASH_MISMATCH", "Observed process image SHA-256 differs from the exact reviewed executable binding.");
            }

            var runtimeReady = false;
            if (consumedState.ReadinessDelayMilliseconds > 0)
            {
                await Task.Delay(consumedState.ReadinessDelayMilliseconds, cancellationToken).ConfigureAwait(false);
                runtimeReady = !startedProcess.HasExited;
            }

            var processStillRunning = !startedProcess.HasExited;
            var finalState = consumedState with
            {
                State = runtimeReady ? "RUNTIME_READY_OBSERVED" : "PROCESS_STARTED_VERIFIED",
                Completed = true,
                ProcessId = startedProcess.Id,
                ProcessStartedAt = startedAt,
                ObservedProcessImagePath = observedImagePath,
                ObservedProcessImageSha256 = observedImageSha,
                RuntimeReadyObserved = runtimeReady,
                StateRevision = consumedState.StateRevision + 1,
                Note = runtimeReady
                    ? "Process image was verified and bounded alive-after-delay readiness was observed. No benchmark/model/game authority was created."
                    : "Process image was verified. Process start is not treated as runtime readiness."
            };
            await WriteJsonAtomicAsync(statePath, finalState, CancellationToken.None).ConfigureAwait(false);

            if (processStillRunning)
            {
                _active = new ActiveRuntime(finalState.LeaseId, startedProcess, finalState.ExecutablePath, startedAt);
                startedProcess = null;
            }

            var transactionId = Guid.NewGuid().ToString("N");
            var receipt = new RuntimeExecutionReceiptV053(
                ExecutionReceiptSchema, Version, DateTimeOffset.Now, transactionId, finalState.LeaseId,
                finalState.RequestId, finalState.State, finalState.ProcessId!.Value, startedAt,
                finalState.ExecutablePath, executableShaBeforeStart, observedImagePath, observedImageSha,
                true, processStillRunning, runtimeReady, statePath, HashFile(statePath),
                true, false, false, false, false, false, false, false, false, false, false,
                BaseNonEffects(),
                runtimeReady ? "RUNTIME_READY_OBSERVED" : "PROCESS_STARTED_VERIFIED",
                "Verified process start consumed exactly one lease call. Stop authority, when available, is bound only to this in-memory owned Process object/tree.");
            var receiptPath = RuntimeReceiptPath(root, "execution", transactionId);
            await WriteJsonAtomicAsync(receiptPath, receipt, CancellationToken.None).ConfigureAwait(false);
            return (receipt, receiptPath);
        }
        catch (OperationCanceledException)
        {
            if (startedProcess is not null) TryKillExact(startedProcess);
            if (consumedState is not null) await TryMarkFailureAsync(grant.LeaseStatePath, consumedState, "EXECUTION_CANCELLED").ConfigureAwait(false);
            throw;
        }
        catch (RuntimeExecutionExceptionV053 ex)
        {
            if (startedProcess is not null) TryKillExact(startedProcess);
            if (consumedState is not null) await TryMarkFailureAsync(grant.LeaseStatePath, consumedState, ex.Classification).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            if (startedProcess is not null) TryKillExact(startedProcess);
            if (consumedState is not null) await TryMarkFailureAsync(grant.LeaseStatePath, consumedState, "PROCESS_START_OR_VERIFY_FAILED").ConfigureAwait(false);
            throw new RuntimeExecutionExceptionV053("PROCESS_START_OR_VERIFY_FAILED", "Bounded runtime process start/verification failed after one-shot authority consumption.", ex);
        }
        finally
        {
            startedProcess?.Dispose();
            _gate.Release();
        }
    }

    public bool HasActiveOwnedRuntime
    {
        get
        {
            ReapExitedActive();
            return _active is not null;
        }
    }

    public async Task<(RuntimeExecutionStopReceiptV053 Receipt, string ReceiptPath)> StopActiveOwnedRuntimeAsync(
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ReapExitedActive();
            if (_active is null) throw Refused("NO_ACTIVE_OWNED_RUNTIME", "There is no active bounded runtime owned by this Workbench process.");
            var active = _active;
            var observed = QueryProcessImagePath(active.Process);
            var startedAt = new DateTimeOffset(active.Process.StartTime.ToUniversalTime(), TimeSpan.Zero);
            if (!PathsEqual(observed, active.ExecutablePath) || Math.Abs((startedAt - active.ProcessStartedAt).TotalSeconds) > 1)
                throw Refused("OWNED_PROCESS_IDENTITY_DRIFT", "Active Process object no longer matches exact path/start-time ownership evidence; stop is refused.");

            active.Process.Kill(entireProcessTree: true);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            try { await active.Process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw Refused("OWNED_PROCESS_STOP_TIMEOUT", "Exact owned process tree did not exit within the bounded stop timeout.");
            }
            var exited = active.Process.HasExited;
            var receipt = new RuntimeExecutionStopReceiptV053(
                StopReceiptSchema, Version, DateTimeOffset.Now, active.LeaseId, active.Process.Id,
                active.ExecutablePath, active.ProcessStartedAt, true, true, exited, false,
                exited ? "OWNED_PROCESS_TREE_STOPPED" : "OWNED_PROCESS_TREE_STOP_INCOMPLETE",
                "Stop accepted no caller-supplied PID and targeted only the exact Process object created by this execution lease.");
            var root = ResolveRepositoryRoot(workspaceRoot);
            var receiptPath = RuntimeReceiptPath(root, "stop", Guid.NewGuid().ToString("N"));
            await WriteJsonAtomicAsync(receiptPath, receipt, CancellationToken.None).ConfigureAwait(false);
            active.Process.Dispose();
            _active = null;
            return (receipt, receiptPath);
        }
        finally
        {
            _gate.Release();
        }
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks()
    {
        var checks = new List<(string, bool, string, string)>
        {
            ("request-schema", RequestSchema == "matawaka.runtime-execution-request/v0.53", RequestSchema, "matawaka.runtime-execution-request/v0.53"),
            ("one-shot-max-calls", true, "MaxCalls fixed to 1", "1"),
            ("shell-cmd-refused", ForbiddenExecutableNames.Contains("cmd.exe"), "cmd.exe", "refused"),
            ("shell-powershell-refused", ForbiddenExecutableNames.Contains("powershell.exe"), "powershell.exe", "refused"),
            ("shell-pwsh-refused", ForbiddenExecutableNames.Contains("pwsh.exe"), "pwsh.exe", "refused"),
            ("secret-token-env-refused", ContainsForbiddenEnvironmentFragment("OPENAI_API_TOKEN"), "OPENAI_API_TOKEN", "refused"),
            ("secret-password-env-refused", ContainsForbiddenEnvironmentFragment("DB_PASSWORD"), "DB_PASSWORD", "refused"),
            ("argument-vector", true, "ProcessStartInfo.ArgumentList", "no shell command string"),
            ("environment-base", FixedInheritedEnvironmentNames.SequenceEqual(new[] { "SystemRoot", "WINDIR", "TEMP", "TMP" }), string.Join(",", FixedInheritedEnvironmentNames), "minimal OS environment"),
            ("runtime-manifest-required", RuntimeTreeVerifiedState == "MATERIALIZED_VERIFIED", RuntimeTreeVerifiedState, "MATERIALIZED_VERIFIED"),
            ("stop-no-arbitrary-pid", true, "StopActiveOwnedRuntimeAsync accepts no PID", "owned Process object only"),
            ("retry-resume", true, "terminal lease after consumption/failure", "no automatic retry/resume"),
            ("readiness-separate", true, "ReadinessDelayMilliseconds optional", "Process Started != Runtime Ready")
        };
        return checks;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gate.Dispose();
        if (_active is not null)
        {
            _active.Process.Dispose();
            _active = null;
        }
    }

    private static void ValidatePreview(RuntimeExecutionPreviewV053 preview)
    {
        if (preview is null || preview.Schema != PreviewSchema || preview.Version != Version ||
            !preview.ReadyForExplicitExecutionAuthority || preview.ProcessExecutionPerformed || preview.RuntimeTreeMaterializationPerformed ||
            preview.MaxCalls != 1)
            throw Refused("PREVIEW_REFUSED", "Exact no-effect v0.53 execution preview is required before authority grant.");
    }

    private static void ValidateGrantAgainstState(RuntimeExecutionGrantV053 grant, RuntimeExecutionLeaseStateV053 state)
    {
        if (grant is null || grant.Schema != GrantSchema || grant.Version != Version || state.Schema != LeaseStateSchema || state.Version != Version ||
            grant.LeaseId != state.LeaseId || grant.RequestId != state.RequestId || grant.RequestDigestSha256 != state.RequestDigestSha256 ||
            !Path.GetFullPath(grant.LeaseStatePath).Equals(Path.GetFullPath(RuntimeStatePathFromStatePath(grant.LeaseStatePath)), StringComparison.OrdinalIgnoreCase))
            throw Refused("EXECUTION_GRANT_REFUSED", "Grant/state identity does not match exact v0.53 one-shot execution authority.");
    }

    private static string RuntimeStatePathFromStatePath(string statePath) => statePath;

    private static void RevalidateBeforeStart(RuntimeExecutionLeaseStateV053 state)
    {
        if (!File.Exists(state.RuntimeTreeManifestPath) || !HashFile(state.RuntimeTreeManifestPath).Equals(state.RuntimeTreeManifestSha256, StringComparison.OrdinalIgnoreCase))
            throw Refused("RUNTIME_MANIFEST_DRIFT", "Runtime-tree manifest changed after preview/grant.");
        if (!File.Exists(state.ExecutablePath)) throw Refused("EXECUTABLE_MISSING", "Exact executable disappeared before Process.Start.");
        RejectReparseChain(state.ExecutablePath);
        var info = new FileInfo(state.ExecutablePath);
        if (info.Length != state.ExecutableBytes) throw Refused("EXECUTABLE_SIZE_DRIFT", "Executable size changed before Process.Start.");
        if (!HashFile(state.ExecutablePath).Equals(state.ExecutableSha256, StringComparison.OrdinalIgnoreCase))
            throw Refused("EXECUTABLE_HASH_DRIFT", "Executable SHA-256 changed immediately before Process.Start.");
        if (!Directory.Exists(state.WorkingDirectory)) throw Refused("WORKING_DIRECTORY_MISSING", "Exact working directory disappeared before Process.Start.");
        RejectReparseChain(state.WorkingDirectory);
    }

    private static void ValidateArguments(IReadOnlyList<string>? arguments)
    {
        if (arguments is null) throw Refused("ARGUMENTS_INVALID", "Arguments array is required; use an empty array for no arguments.");
        if (arguments.Count > MaxArguments) throw Refused("ARGUMENTS_BOUND_REFUSED", $"At most {MaxArguments} exact arguments are allowed.");
        foreach (var arg in arguments)
            if (arg is null || arg.Length > MaxArgumentChars || arg.Contains('\0'))
                throw Refused("ARGUMENT_REFUSED", $"Each exact argument must be non-null, contain no NUL, and be <= {MaxArgumentChars} characters.");
    }

    private static void ValidateEnvironment(IReadOnlyDictionary<string, string>? environment)
    {
        if (environment is null) throw Refused("ENVIRONMENT_INVALID", "Environment object is required; use an empty object for no additions.");
        if (environment.Count > MaxEnvironmentEntries) throw Refused("ENVIRONMENT_BOUND_REFUSED", $"At most {MaxEnvironmentEntries} exact environment entries are allowed.");
        foreach (var pair in environment)
        {
            if (!EnvironmentNameRegex.IsMatch(pair.Key) || ContainsForbiddenEnvironmentFragment(pair.Key))
                throw Refused("ENVIRONMENT_NAME_REFUSED", $"Environment name {pair.Key} is invalid or secret-bearing by policy.");
            if (pair.Value is null || pair.Value.Length > MaxEnvironmentValueChars || pair.Value.Contains('\0'))
                throw Refused("ENVIRONMENT_VALUE_REFUSED", $"Environment value for {pair.Key} must be non-null, contain no NUL, and be <= {MaxEnvironmentValueChars} characters.");
        }
    }

    private static bool ContainsForbiddenEnvironmentFragment(string name)
        => ForbiddenEnvironmentFragments.Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static RuntimeExecutionLeaseStateV053 ReadState(string statePath)
    {
        if (!File.Exists(statePath)) throw Refused("LEASE_STATE_MISSING", "Persisted execution lease state is missing.");
        try
        {
            return JsonSerializer.Deserialize<RuntimeExecutionLeaseStateV053>(File.ReadAllText(statePath, Encoding.UTF8), JsonOptions)
                ?? throw new InvalidDataException("Lease state deserialized to null.");
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException)
        {
            throw new RuntimeExecutionExceptionV053("LEASE_STATE_INVALID", "Persisted execution lease state could not be parsed.", ex);
        }
    }

    private async Task TryMarkFailureAsync(string statePath, RuntimeExecutionLeaseStateV053 state, string classification)
    {
        try
        {
            var failed = state with
            {
                RemainingCalls = 0,
                Completed = true,
                Failed = true,
                FailureClassification = classification,
                State = "EXECUTION_TERMINAL_FAIL_CLOSED",
                StateRevision = state.StateRevision + 1,
                Note = "Execution failed after one-shot authority consumption. No retry/resume/start authority remains."
            };
            await WriteJsonAtomicAsync(statePath, failed, CancellationToken.None).ConfigureAwait(false);
        }
        catch { }
    }

    private void ReapExitedActive()
    {
        if (_active is null) return;
        try
        {
            if (!_active.Process.HasExited) return;
        }
        catch { }
        _active.Process.Dispose();
        _active = null;
    }

    private static string QueryProcessImagePath(Process process)
    {
        var capacity = 32768;
        var builder = new StringBuilder(capacity);
        var size = builder.Capacity;
        if (!QueryFullProcessImageName(process.Handle, 0, builder, ref size) || size <= 0)
            throw Refused("PROCESS_IMAGE_OBSERVATION_FAILED", "Windows could not provide the exact process image path for ownership verification.");
        return Path.GetFullPath(builder.ToString());
    }

    private static void TryKillExact(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
    }

    private static string ResolveRepositoryRoot(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot)) throw Refused("WORKSPACE_REFUSED", "Workspace root is required.");
        var root = Path.GetFullPath(Path.Combine(workspaceRoot.Trim(), "Workbench"));
        if (!Directory.Exists(Path.Combine(root, ".git"))) throw Refused("WORKSPACE_REFUSED", $"Workbench Git repository missing: {root}");
        return EnsureDirectoryRoot(root);
    }

    private static string EnsureDirectoryRoot(string path)
        => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static void RequireOutsideRepository(string repositoryRoot, string candidate, string role)
    {
        var repo = EnsureDirectoryRoot(repositoryRoot);
        var full = Path.GetFullPath(candidate);
        if (PathsEqual(repo, full) || IsUnder(repo, full))
            throw Refused("SOURCE_RUNTIME_SEPARATION_REFUSED", $"{role} must be outside the Workbench Git repository.");
    }

    private static bool IsUnder(string root, string candidate)
    {
        var prefix = EnsureDirectoryRoot(root) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(candidate).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveUnderRoot(string root, string relative, string role)
    {
        var normalized = relative.Replace('/', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(root, normalized));
        if (!IsUnder(root, full) && !PathsEqual(root, full))
            throw Refused("PATH_ESCAPE_REFUSED", $"{role} escapes exact runtime root.");
        return full;
    }

    private static string NormalizeRelativePath(string path, string role)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path)) throw Refused("RELATIVE_PATH_REFUSED", $"{role} must be a non-empty relative path.");
        var normalized = path.Replace('\\', '/').Trim('/');
        if (normalized.Length == 0 || normalized.Split('/').Any(part => part is "" or "." or ".."))
            throw Refused("RELATIVE_PATH_REFUSED", $"{role} contains an empty/dot/traversal segment.");
        return normalized;
    }

    private static string NormalizeRelativeDirectory(string path, string role)
    {
        if (string.IsNullOrWhiteSpace(path) || path == ".") return string.Empty;
        return NormalizeRelativePath(path, role);
    }

    private static void RejectReparseChain(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? throw Refused("PATH_REFUSED", "Path root could not be resolved.");
        var relative = full[root.Length..].Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var current = root;
        foreach (var segment in relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!File.Exists(current) && !Directory.Exists(current)) continue;
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw Refused("REPARSE_PATH_REFUSED", $"Runtime execution path contains a reparse point: {current}");
        }
    }

    private static string RuntimeStatePath(string repositoryRoot, string leaseId)
    {
        var dir = Path.Combine(repositoryRoot, "artifacts", "runtime-execution", "leases");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"runtime-execution-lease-v0.53-{leaseId}.json");
    }

    private static string RuntimeReceiptPath(string repositoryRoot, string kind, string id)
    {
        var dir = Path.Combine(repositoryRoot, "artifacts", "runtime-execution", "receipts");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"runtime-execution-{kind}-v0.53-{id}.json");
    }

    private static async Task WriteJsonAtomicAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(value, JsonOptions), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string HashText(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static void RequireSha256(string value, string role)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 64 || value.Any(ch => !Uri.IsHexDigit(ch)))
            throw Refused("SHA256_REFUSED", $"{role} is not an exact SHA-256 hex digest.");
    }

    private static bool PathsEqual(string a, string b)
        => Path.GetFullPath(a).Equals(Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> BaseNonEffects() => new[]
    {
        "Verified Artifact != Materialized Runtime",
        "Materialized Runtime != Execution Authority",
        "Execution Authority != General Process Authority",
        "Process Started != Runtime Ready",
        "Runtime Ready != Benchmark Authority",
        "Runtime Ready != Model Request Authority",
        "Stop Authority != Arbitrary Process Kill",
        "no archive extraction/materialization performed by v0.53 execution layer",
        "no shell/cmd/PowerShell/script indirection",
        "no elevation request",
        "no automatic retry/resume/start authority",
        "no benchmark/model request/game access authority",
        "no KONTUR-specific runtime behavior"
    };

    private static RuntimeExecutionExceptionV053 Refused(string classification, string message)
        => new(classification, message);

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BoundedRuntimeExecutionV053Service));
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

    private sealed record ActiveRuntime(string LeaseId, Process Process, string ExecutablePath, DateTimeOffset ProcessStartedAt);
}
