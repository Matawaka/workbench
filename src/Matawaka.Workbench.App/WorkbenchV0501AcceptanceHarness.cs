using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed class WorkbenchV0501AcceptanceHarness
{
    private readonly WorkbenchV050AcceptanceHarness _predecessor;
    private readonly MainWindow _window;

    public WorkbenchV0501AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness, MainWindow window)
    {
        _predecessor = new WorkbenchV050AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)),
            window ?? throw new ArgumentNullException(nameof(window)));
        _window = window;
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(RuntimeContext context, CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(_window);
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var successorChecks = new List<WorkbenchAcceptanceCheck>();
        Add(successorChecks, OpenAiSecureMcpTunnelV0501Service.RunOfflineContractChecks());
        Add(successorChecks, LocalCheckpointV0501Service.RunOfflineContractChecks());
        Add(successorChecks, FixedGitHubPublicationV0501Service.RunOfflineContractChecks());
        successorChecks.AddRange(OperatorSurfaceV045Contract.Observe(_window).Select(x => new WorkbenchAcceptanceCheck(
            "v0501-preserve-" + x.Id, x.Passed, x.Observed, x.Expected)));

        var checks = predecessor.Checks.Concat(successorChecks).ToArray();
        var passed = predecessor.Passed && successorChecks.All(x => x.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.50.1 is a stabilization of the local failed real-host v0.50 checkpoint and does not reclassify v0.50 as a published accepted frontier",
            "readiness observation is bounded by min(90 seconds, current read-lease expiry)",
            "healthz liveness is observed separately from readyz readiness and does not imply tunnel success",
            "non-success readiness bodies are bounded and redacted before local evidence persistence",
            "failed readiness stops the exact Workbench-started tunnel-client child before refusal",
            "runtime credential, lease bearer and secret local MCP endpoint are not persisted in readiness evidence",
            "MCP caller still cannot select ApplicationId, LeaseId, bearer or filesystem root",
            "every content read still delegates to the accepted v0.48 lease gate through the v0.49.1 adapter",
            "failed workbench-v0.50-accepted tag remains local-only and must remain absent remotely",
            "no tunnel CRUD/admin authority, automatic ChatGPT configuration, public listener, application/source mutation, Agent Execute or ActionPermit authority"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.50.1",
            "0.50.1",
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
            "Workbench v0.50.1 closes the v0.50 real-host readiness-observability gap by retaining bounded redacted health/readiness diagnostics while preserving fail-closed /readyz admission and the accepted lease authority boundary.");
    }

    private static void Add(List<WorkbenchAcceptanceCheck> destination, IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> checks)
        => destination.AddRange(checks.Select(x => new WorkbenchAcceptanceCheck(x.Id, x.Passed, x.Observed, x.Expected)));
}
