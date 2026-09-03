using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed class WorkbenchV0502AcceptanceHarness
{
    private readonly WorkbenchV0501AcceptanceHarness _predecessor;
    private readonly MainWindow _window;

    public WorkbenchV0502AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness, MainWindow window)
    {
        _predecessor = new WorkbenchV0501AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)),
            window ?? throw new ArgumentNullException(nameof(window)));
        _window = window;
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(RuntimeContext context, CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(_window);
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var successorChecks = new List<WorkbenchAcceptanceCheck>();
        Add(successorChecks, PlainMcpOAuthDiscoveryCompatV0502Service.RunOfflineContractChecks());
        Add(successorChecks, LocalCheckpointV0502Service.RunOfflineContractChecks());
        Add(successorChecks, FixedGitHubPublicationV0502Service.RunOfflineContractChecks());
        successorChecks.AddRange(OperatorSurfaceV045Contract.Observe(_window).Select(x => new WorkbenchAcceptanceCheck(
            "v0502-preserve-" + x.Id, x.Passed, x.Observed, x.Expected)));

        var checks = predecessor.Checks.Concat(successorChecks).ToArray();
        var passed = predecessor.Passed && successorChecks.All(x => x.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.50.2 is a stabilization of the local failed real-host v0.50.1 checkpoint and does not reclassify v0.50.1 as a published accepted frontier",
            "plain-MCP OAuth discovery compatibility is a loopback-only transport facade and does not modify the accepted v0.49.1 lease-gated MCP implementation",
            "OAuth Protected Resource Metadata candidates return deterministic 404 and no OAuth authorization server/DCR metadata is advertised",
            "Authorization and Cookie headers are not forwarded from the tunnel-facing compatibility facade to the lease-gated local MCP endpoint",
            "the compatibility facade has no filesystem read/write authority and every content read remains enforced by the accepted v0.48 lease gate",
            "v0.50.1 bounded/redacted healthz/readyz diagnostics remain the tunnel readiness gate",
            "failed workbench-v0.50-accepted and workbench-v0.50.1-accepted tags remain local-only and must remain absent remotely",
            "no OAuth authority, tunnel CRUD/admin authority, automatic ChatGPT configuration, public listener, application/source mutation, Agent Execute or ActionPermit authority"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.50.2",
            "0.50.2",
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
            "Workbench v0.50.2 closes the plain no-auth MCP OAuth-discovery compatibility gap by presenting only deterministic 404 PRMD responses in a loopback facade while keeping all file-content authority in the existing v0.48 lease gate.");
    }

    private static void Add(List<WorkbenchAcceptanceCheck> destination, IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> checks)
        => destination.AddRange(checks.Select(x => new WorkbenchAcceptanceCheck(x.Id, x.Passed, x.Observed, x.Expected)));
}
