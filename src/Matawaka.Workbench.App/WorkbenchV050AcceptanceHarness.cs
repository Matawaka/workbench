using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed class WorkbenchV050AcceptanceHarness
{
    private readonly WorkbenchV0491AcceptanceHarness _predecessor;
    private readonly MainWindow _window;

    public WorkbenchV050AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness, MainWindow window)
    {
        _predecessor = new WorkbenchV0491AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)),
            window ?? throw new ArgumentNullException(nameof(window)));
        _window = window;
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(RuntimeContext context, CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(_window);
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var successorChecks = new List<WorkbenchAcceptanceCheck>();
        Add(successorChecks, LocalAppsActionDialogV050.RunOfflineContractChecks());
        Add(successorChecks, OpenAiSecureMcpTunnelDialogV050.RunOfflineContractChecks());
        Add(successorChecks, OpenAiSecureMcpTunnelV050Service.RunOfflineContractChecks());
        Add(successorChecks, LocalCheckpointV050Service.RunOfflineContractChecks());
        Add(successorChecks, FixedGitHubPublicationV050Service.RunOfflineContractChecks());
        successorChecks.AddRange(OperatorSurfaceV045Contract.Observe(_window).Select(x => new WorkbenchAcceptanceCheck(
            "v050-preserve-" + x.Id, x.Passed, x.Observed, x.Expected)));

        var checks = predecessor.Checks.Concat(successorChecks).ToArray();
        var passed = predecessor.Passed && successorChecks.All(x => x.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.50 adds only an explicit Secure MCP Tunnel handoff above the accepted v0.49.1 loopback MCP adapter",
            "Secure MCP Tunnel startup requires an already-active lease-gated local MCP adapter and cannot create/renew/widen a read lease",
            "the external OpenAI tunnel-client runtime is fixed-path, version-observed and SHA-256-observed but remains outside Workbench Git source",
            "v0.50 does not download or install tunnel-client automatically",
            "v0.50 creates/deletes no OpenAI tunnels and accepts no Admin key authority",
            "runtime API key and secret local MCP endpoint are child-environment-only and are not persisted in Workbench receipts",
            "tunnel readiness is required; process launch alone is not success",
            "tunnel child lifetime is bounded by the read lease expiry and can be explicitly stopped earlier",
            "ChatGPT connector/developer-mode configuration remains a separate human product action",
            "no application/source mutation, arbitrary process execution, public inbound listener, Agent Execute, ActionPermit or Stable Core promotion"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.50",
            "0.50.0",
            predecessor.RunId,
            DateTimeOffset.Now,
            passed,
            predecessor.AppExecutableSha256,
            predecessor.ProviderA,
            predecessor.ProviderB,
            predecessor.ExecuteTerminalState,
            predecessor.ExecuteProgressEvents,
            checks,
            nonEffects,
            "Workbench v0.50 adds an explicit, session-only OpenAI Secure MCP Tunnel process handoff over the accepted lease-gated local MCP adapter. The tunnel transport creates no filesystem authority and ChatGPT-side connection remains separate.");
    }

    private static void Add(List<WorkbenchAcceptanceCheck> destination, IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> checks)
        => destination.AddRange(checks.Select(x => new WorkbenchAcceptanceCheck(x.Id, x.Passed, x.Observed, x.Expected)));
}
