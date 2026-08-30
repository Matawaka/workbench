using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Matawaka.Workbench.Protocol;

namespace Matawaka.Workbench.AgentHost;

public sealed record SemanticRepositoryRef(
    string Name,
    string Branch,
    string Head,
    int SelectedEvidenceItems,
    IReadOnlyList<string> TopTerms);

public sealed record SemanticEvidencePacket(
    string Schema,
    string CommandId,
    string Target,
    IReadOnlyList<SemanticRepositoryRef> Repositories,
    AgentEvidenceCoverage Coverage,
    IReadOnlyList<AgentEvidence> Evidence,
    CapabilityReceipt AuthorityReceipt);

public sealed record SemanticSignal(
    string Id,
    int EvidenceItems,
    IReadOnlyList<string> Repositories,
    IReadOnlyList<string> Terms);

public sealed record SemanticAnalysisReceipt(
    string Schema,
    string Provider,
    string InputDigest,
    string OutputDigest,
    int EvidenceItemCount,
    int RepositoryCount,
    IReadOnlyList<SemanticSignal> Signals,
    IReadOnlyList<string> Invariants,
    IReadOnlyList<string> NonEffects);

public sealed record SemanticProviderSelectionReceipt(
    string Schema,
    string RegistryVersion,
    string RequestedProvider,
    string SelectedProvider,
    bool ProviderFound,
    bool OfflineOnly,
    bool DynamicProviderLoadingAllowed,
    string IsolationLevel,
    bool SeparateProcess,
    bool EnvironmentAllowlisted,
    bool OsSandbox,
    bool SameUserSecurityContext,
    int TimeoutMilliseconds,
    int MaxOutputBytes,
    IReadOnlyList<string> AvailableProviders,
    IReadOnlyList<string> NonEffects);

public sealed record SemanticProcessBoundaryReceipt(
    string Schema,
    string HostExecutable,
    bool SeparateProcess,
    bool StandardInputPacketOnly,
    bool StandardOutputReceiptOnly,
    bool EnvironmentAllowlisted,
    IReadOnlyList<string> EnvironmentAllowlist,
    bool RepositoryRootArgumentProvided,
    bool FileHandleProvided,
    bool DynamicProviderPathLoadingAllowed,
    bool NetworkClientOrCredentialProvided,
    bool MutationCapabilityProvided,
    bool OsSandbox,
    bool SameUserSecurityContext,
    int TimeoutMilliseconds,
    int MaxInputBytes,
    int MaxOutputBytes);

public sealed record SemanticProviderBoundaryReceipt(
    string Schema,
    string Provider,
    string RegistryVersion,
    string InputSchema,
    string InputDigest,
    string OutputDigest,
    string ExpectedUuAapFrontier,
    string? ObservedUuAapFrontier,
    bool SourceFrontierMatched,
    IReadOnlyList<ProtocolSourceBinding> SourceBindings,
    bool OfflineOnly,
    bool DynamicProviderLoadingAllowed,
    string IsolationLevel,
    bool RepositoryRootsProvided,
    bool FileHandlesProvided,
    bool ArbitraryProcessExecutionProvided,
    bool NetworkAccessProvided,
    bool MutationAuthorityProvided,
    SemanticProcessBoundaryReceipt ProcessBoundary,
    IReadOnlyList<string> NonEffects);

public sealed record SemanticProviderResult(
    AgentProposal Proposal,
    SemanticProviderBoundaryReceipt Boundary,
    SemanticAnalysisReceipt Analysis,
    string Note);

public sealed record SemanticHostRequest(
    string Schema,
    string Provider,
    string InputDigest,
    SemanticEvidencePacket Packet);

public sealed record SemanticHostResponse(
    string Schema,
    bool Success,
    string Provider,
    string InputDigest,
    string? OutputDigest,
    AgentProposal? Proposal,
    IReadOnlyList<SemanticSignal>? Signals,
    string? Note,
    string? Error);

public static class SemanticProviderCatalog
{
    public const string RegistryVersion = "workbench-local-semantic-provider-registry/v0.4";
    public const string LocalContractSynthesisId = "local-contract-synthesis-v0.3";
    public const string DeterministicEvidenceId = "deterministic-evidence-semantic-v0.2";
    public const string DefaultProvider = LocalContractSynthesisId;

    public static readonly IReadOnlyList<string> ProviderIds = new[]
    {
        DeterministicEvidenceId,
        LocalContractSynthesisId
    };
}

public interface ISemanticProviderClient
{
    string RegistryVersion { get; }
    IReadOnlyList<string> ProviderIds { get; }
    SemanticProviderSelectionReceipt Select(string? requestedProvider);

    Task<SemanticProviderResult> AnalyzeAsync(
        SemanticProviderSelectionReceipt selection,
        SemanticEvidencePacket packet,
        IProgress<WorkbenchProgress>? progress,
        CancellationToken cancellationToken);
}

/// <summary>
/// v0.4 invokes only one fixed built-in semantic host executable. The provider
/// id is data, not an executable path. The child receives one sanitized JSON
/// packet on stdin and returns one JSON receipt on stdout. This is a stronger
/// process boundary than v0.3 but is explicitly not an AppContainer, VM, ACL
/// sandbox, network sandbox, or separate Windows security token.
/// </summary>
public sealed class ProcessSemanticProviderClient : ISemanticProviderClient
{
    public const int ProviderTimeoutMilliseconds = 8000;
    public const int MaxInputBytes = 2 * 1024 * 1024;
    public const int MaxOutputBytes = 1024 * 1024;

    private static readonly string[] EnvironmentAllowlist =
    [
        "SystemRoot",
        "WINDIR",
        "DOTNET_ROOT",
        "DOTNET_MULTILEVEL_LOOKUP",
        "TEMP",
        "TMP"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public string RegistryVersion => SemanticProviderCatalog.RegistryVersion;
    public IReadOnlyList<string> ProviderIds => SemanticProviderCatalog.ProviderIds;

    public SemanticProviderSelectionReceipt Select(string? requestedProvider)
    {
        var requested = string.IsNullOrWhiteSpace(requestedProvider)
            ? SemanticProviderCatalog.DefaultProvider
            : requestedProvider.Trim();

        var selected = ProviderIds.FirstOrDefault(item =>
            string.Equals(item, requested, StringComparison.OrdinalIgnoreCase));

        if (selected is null)
            throw new InvalidDataException(
                $"Unknown semanticProvider '{requested}'. Available: {string.Join(", ", ProviderIds)}");

        return new SemanticProviderSelectionReceipt(
            "matawaka.semantic-provider-selection-receipt/v0.4",
            RegistryVersion,
            requested,
            selected,
            true,
            true,
            false,
            "separate fixed semantic host process; same-user security context; not an OS sandbox",
            true,
            true,
            false,
            true,
            ProviderTimeoutMilliseconds,
            MaxOutputBytes,
            ProviderIds,
            SemanticProviderSupport.ProviderNonEffects);
    }

    public async Task<SemanticProviderResult> AnalyzeAsync(
        SemanticProviderSelectionReceipt selection,
        SemanticEvidencePacket packet,
        IProgress<WorkbenchProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!selection.ProviderFound ||
            !ProviderIds.Contains(selection.SelectedProvider, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"Semantic provider is not available: {selection.SelectedProvider}");

        var observedUuAap = SemanticProviderSupport.RequireExactSourceFrontier(packet);
        var inputDigest = SemanticProviderSupport.ComputeInputDigest(packet);
        var request = new SemanticHostRequest(
            "matawaka.semantic-host-request/v0.4",
            selection.SelectedProvider,
            inputDigest,
            packet);

        var requestJson = JsonSerializer.Serialize(request, JsonOptions);
        if (Encoding.UTF8.GetByteCount(requestJson) > MaxInputBytes)
            throw new InvalidDataException($"Semantic IPC input exceeds {MaxInputBytes} bytes.");

        var hostPath = Path.Combine(
            AppContext.BaseDirectory,
            "semantic-host",
            "Matawaka.Workbench.SemanticHost.exe");

        if (!File.Exists(hostPath))
            throw new FileNotFoundException("Fixed semantic host executable is missing.", hostPath);

        var runtimeRoot = Path.Combine(
            Path.GetTempPath(),
            "Matawaka.Workbench",
            "semantic",
            SanitizePathSegment(packet.CommandId) + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runtimeRoot);

        progress?.Report(new WorkbenchProgress(
            packet.CommandId,
            "semantic.process.started",
            94,
            $"{selection.SelectedProvider} -> fixed semantic host process; stdin packet {inputDigest[..12]}…",
            DateTimeOffset.Now,
            "SEMANTIC_ANALYSIS",
            "PROCESS_BOUNDARY_ENTERED",
            "SEMANTIC_HOST",
            "SEMANTIC_HOST_RECEIPT",
            $"semantic-process:{packet.CommandId}:{selection.SelectedProvider}"));

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = hostPath,
                WorkingDirectory = runtimeRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("--stdio-v0.4");
            ApplyEnvironmentAllowlist(startInfo, runtimeRoot);

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
                throw new InvalidOperationException("Semantic host process did not start.");

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            await process.StandardInput.WriteAsync(requestJson);
            await process.StandardInput.FlushAsync();
            process.StandardInput.Close();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ProviderTimeoutMilliseconds);

            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                throw new TimeoutException(
                    $"Semantic host exceeded {ProviderTimeoutMilliseconds} ms timeout.");
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                throw;
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (Encoding.UTF8.GetByteCount(stdout) > MaxOutputBytes)
                throw new InvalidDataException($"Semantic IPC output exceeds {MaxOutputBytes} bytes.");

            if (string.IsNullOrWhiteSpace(stdout))
                throw new InvalidDataException(
                    $"Semantic host returned no receipt. Exit={process.ExitCode}; stderr={CompactError(stderr)}");

            SemanticHostResponse response;
            try
            {
                response = JsonSerializer.Deserialize<SemanticHostResponse>(stdout, JsonOptions)
                    ?? throw new InvalidDataException("Semantic host response was empty JSON.");
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException(
                    $"Semantic host returned invalid JSON. Exit={process.ExitCode}; stderr={CompactError(stderr)}",
                    ex);
            }

            if (process.ExitCode != 0 || !response.Success)
                throw new InvalidDataException(
                    $"Semantic host denied/failed provider execution: {response.Error ?? CompactError(stderr)}");

            if (!string.Equals(response.Schema, "matawaka.semantic-host-response/v0.4", StringComparison.Ordinal) ||
                !string.Equals(response.Provider, selection.SelectedProvider, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(response.InputDigest, inputDigest, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Semantic host response identity/input digest mismatch.");

            if (response.Proposal is null || response.Signals is null || string.IsNullOrWhiteSpace(response.OutputDigest))
                throw new InvalidDataException("Semantic host response is missing proposal/signals/output digest.");

            var verifiedOutputDigest = SemanticProviderSupport.ComputeOutputDigest(
                response.Proposal,
                response.Signals);
            if (!string.Equals(verifiedOutputDigest, response.OutputDigest, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Semantic host output digest failed parent-process verification.");

            var analysis = new SemanticAnalysisReceipt(
                "matawaka.semantic-analysis-receipt/v0.4",
                selection.SelectedProvider,
                inputDigest,
                verifiedOutputDigest,
                packet.Evidence.Count,
                packet.Repositories.Count,
                response.Signals,
                SemanticProviderSupport.CommonInvariants,
                SemanticProviderSupport.ProviderNonEffects);

            var boundary = SemanticProviderSupport.BuildBoundary(
                selection.SelectedProvider,
                packet,
                inputDigest,
                verifiedOutputDigest,
                observedUuAap);

            progress?.Report(new WorkbenchProgress(
                packet.CommandId,
                "semantic.process.completed",
                98,
                $"{selection.SelectedProvider}: child receipt verified; output {verifiedOutputDigest[..12]}…",
                DateTimeOffset.Now,
                "SEMANTIC_ANALYSIS",
                "PROCESS_RECEIPT_VERIFIED",
                "NONE",
                "AGENT_CHECKPOINT_READY",
                $"semantic-process:{packet.CommandId}:{selection.SelectedProvider}"));

            return new SemanticProviderResult(
                response.Proposal,
                boundary,
                analysis,
                response.Note ?? "Fixed semantic host process returned a verified offline receipt.");
        }
        finally
        {
            TryDeleteDirectory(runtimeRoot);
        }
    }

    private static void ApplyEnvironmentAllowlist(ProcessStartInfo info, string runtimeRoot)
    {
        var inherited = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["SystemRoot"] = Environment.GetEnvironmentVariable("SystemRoot"),
            ["WINDIR"] = Environment.GetEnvironmentVariable("WINDIR"),
            ["DOTNET_ROOT"] = Environment.GetEnvironmentVariable("DOTNET_ROOT")
        };

        info.Environment.Clear();
        foreach (var pair in inherited)
        {
            if (!string.IsNullOrWhiteSpace(pair.Value))
                info.Environment[pair.Key] = pair.Value!;
        }

        info.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";
        info.Environment["TEMP"] = runtimeRoot;
        info.Environment["TMP"] = runtimeRoot;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Cancellation/timeout cleanup must not mint a different authority path.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Runtime temp cleanup failure is non-authoritative and does not touch repositories.
        }
    }

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "run" : sanitized[..Math.Min(sanitized.Length, 80)];
    }

    private static string CompactError(string value)
    {
        var normalized = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (normalized.Length == 0) return "<empty>";
        return normalized.Length <= 300 ? normalized : normalized[..300] + "…";
    }
}

public static class SemanticProviderSupport
{
    public static readonly string[] ProviderNonEffects =
    [
        "no repository mutation",
        "no repository root included in semantic IPC packet",
        "no file handle included in semantic IPC packet",
        "no network endpoint/client/credential supplied to semantic provider",
        "built-in semantic providers perform no network model call",
        "no arbitrary executable/path accepted from command JSON",
        "fixed semantic host process only",
        "child environment reduced to an explicit allowlist",
        "no materialization authority created",
        "no execution authority created",
        "no ActionPermit created",
        "no provider self-selection after authority decision",
        "no dynamic provider assembly/path loading from JSON",
        "no Stable Core or interface-registry promotion",
        "no canonical UU-AAP conformance claim from this adapter",
        "no hidden reasoning disclosure",
        "separate process boundary is not represented as an OS sandbox"
    ];

    public static string ComputeInputDigest(SemanticEvidencePacket packet)
    {
        var projection = new
        {
            target = packet.Target,
            repositories = packet.Repositories.Select(item => new
            {
                item.Name,
                item.Branch,
                item.Head,
                item.SelectedEvidenceItems,
                item.TopTerms
            }),
            coverage = packet.Coverage,
            evidence = packet.Evidence,
            authority = new
            {
                packet.AuthorityReceipt.Request.Capability,
                packet.AuthorityReceipt.Request.Operation,
                packet.AuthorityReceipt.Request.Target,
                packet.AuthorityReceipt.Decision.Decision,
                packet.AuthorityReceipt.Decision.AuthorityGranted,
                packet.AuthorityReceipt.Decision.MutationBudgetGranted,
                packet.AuthorityReceipt.Decision.NetworkAccessGranted,
                packet.AuthorityReceipt.Decision.ArbitraryProcessExecutionGranted
            }
        };

        return Digest(projection);
    }

    public static string ComputeOutputDigest(AgentProposal proposal, IReadOnlyList<SemanticSignal> signals)
        => Digest(new { proposal, signals });

    public static IReadOnlyList<SemanticSignal> BuildSignals(SemanticEvidencePacket packet)
    {
        var definitions = new[]
        {
            new { Id = "AUTHORITY_BOUNDARY", Terms = new[] { "authority", "capability" } },
            new { Id = "EVIDENCE_PROVENANCE", Terms = new[] { "evidence", "receipt" } },
            new { Id = "POSSIBILITY_INTENT", Terms = new[] { "intent", "availability", "possibility" } },
            new { Id = "NON_BINDING_ATTENTION", Terms = new[] { "companion", "solver", "hint", "attention" } },
            new { Id = "REVERSIBILITY", Terms = new[] { "reversible" } }
        };

        var signals = new List<SemanticSignal>();
        foreach (var definition in definitions)
        {
            var matched = packet.Evidence
                .Where(item => item.Terms.Any(term =>
                    definition.Terms.Contains(term, StringComparer.OrdinalIgnoreCase)))
                .ToArray();

            if (matched.Length == 0) continue;

            signals.Add(new SemanticSignal(
                definition.Id,
                matched.Length,
                matched.Select(item => item.Repository)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                matched.SelectMany(item => item.Terms)
                    .Where(term => definition.Terms.Contains(term, StringComparer.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                    .ToArray()));
        }

        return signals
            .OrderByDescending(item => item.EvidenceItems)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
    }

    public static string RequireExactSourceFrontier(SemanticEvidencePacket packet)
    {
        var observed = packet.Repositories
            .FirstOrDefault(item => string.Equals(item.Name, "uu-aap", StringComparison.OrdinalIgnoreCase))
            ?.Head;

        if (!string.Equals(observed, PclCompatibleProgress.UuAapFrontier, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Semantic source frontier mismatch. Expected uu-aap {PclCompatibleProgress.UuAapFrontier}, observed {observed ?? "<missing>"}.");

        return observed!;
    }

    public static SemanticProviderBoundaryReceipt BuildBoundary(
        string providerId,
        SemanticEvidencePacket packet,
        string inputDigest,
        string outputDigest,
        string observedUuAap)
    {
        var bindings = new[]
        {
            PclCompatibleProgress.ProgressSource,
            PclCompatibleProgress.HumanViewSource,
            PclCompatibleProgress.ScopedAuthoritySource,
            PclCompatibleProgress.MaterializationAuthoritySource,
            PclCompatibleProgress.ReusableAdmissionAuditSource
        };

        var processBoundary = new SemanticProcessBoundaryReceipt(
            "matawaka.semantic-process-boundary-receipt/v0.4",
            "Matawaka.Workbench.SemanticHost.exe",
            true,
            true,
            true,
            true,
            new[] { "SystemRoot", "WINDIR", "DOTNET_ROOT", "DOTNET_MULTILEVEL_LOOKUP", "TEMP", "TMP" },
            false,
            false,
            false,
            false,
            false,
            false,
            true,
            ProcessSemanticProviderClient.ProviderTimeoutMilliseconds,
            ProcessSemanticProviderClient.MaxInputBytes,
            ProcessSemanticProviderClient.MaxOutputBytes);

        return new SemanticProviderBoundaryReceipt(
            "matawaka.semantic-provider-boundary-receipt/v0.4",
            providerId,
            SemanticProviderCatalog.RegistryVersion,
            packet.Schema,
            inputDigest,
            outputDigest,
            PclCompatibleProgress.UuAapFrontier,
            observedUuAap,
            true,
            bindings,
            true,
            false,
            "separate fixed semantic host process; same-user security context; not an OS sandbox",
            false,
            false,
            false,
            false,
            false,
            processBoundary,
            ProviderNonEffects);
    }

    public static IReadOnlyList<string> CommonInvariants =>
    [
        "Evidence != Authority",
        "Provider Selection != Authority Grant",
        "Proposal != Materialization",
        "Scoped Authority Evidence != Materialization Authority",
        "Materialization Authority != Execution Authority",
        "Supported Evidence != ActionPermit",
        "Semantic Similarity != Stable Core Admission",
        "Visible Progress != Hidden Reasoning",
        "Process Isolation != OS Sandbox",
        "Fixed Process Invocation != Arbitrary Process Authority"
    ];

    private static string Digest(object value)
    {
        var json = JsonSerializer.Serialize(value);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }
}
