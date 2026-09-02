using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed class WorkbenchV049AcceptanceHarness
{
    private readonly WorkbenchV048AcceptanceHarness _predecessor;
    private readonly MainWindow _window;

    public WorkbenchV049AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness, MainWindow window)
    {
        _predecessor = new WorkbenchV048AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)),
            window ?? throw new ArgumentNullException(nameof(window)));
        _window = window;
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(RuntimeContext context, CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(_window);
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var successorChecks = new List<WorkbenchAcceptanceCheck>();
        Add(successorChecks, LocalAppsActionDialogV049.RunOfflineContractChecks());
        Add(successorChecks, LocalAppMcpAdapterGrantDialogV049.RunOfflineContractChecks());
        Add(successorChecks, LocalAppMcpReadAdapterV049Service.RunOfflineContractChecks());
        Add(successorChecks, LocalCheckpointV049Service.RunOfflineContractChecks());
        Add(successorChecks, FixedGitHubPublicationV049Service.RunOfflineContractChecks());

        successorChecks.AddRange(OperatorSurfaceV045Contract.Observe(_window).Select(x => new WorkbenchAcceptanceCheck(
            "v049-preserve-" + x.Id, x.Passed, x.Observed, x.Expected)));

        var checks = predecessor.Checks.Concat(successorChecks).ToArray();
        var passed = predecessor.Passed && successorChecks.All(x => x.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.49 adapter startup is not lease creation; exact active v0.48 lease grant remains required",
            "MCP caller cannot supply ApplicationId, LeaseId or bearer; those remain fixed in the adapter runtime session",
            "every MCP content read delegates to LocalAppReadLeaseV048Service and remains bounded by scope, TTL, bytes, calls, revocation and expected SHA",
            "adapter listener is IPv4 loopback only with a random in-memory endpoint path token",
            "endpoint token hash only is persisted in adapter receipt; plaintext lease bearer is not persisted by adapter receipt",
            "Workbench runtime adds no MCP NuGet dependency; interoperability with pinned official ModelContextProtocol 2.2.0 client is external qualification evidence",
            "v0.49 starts no Secure MCP Tunnel and performs no automatic account login/linking or public endpoint publication",
            "v0.47 manual Chat read relay and v0.48 lease create/revoke remain available",
            "no application/source mutation, arbitrary filesystem authority, app process execution, catalog mutation, Agent Execute, ActionPermit or Stable Core promotion"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.49",
            "0.49.0",
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
            "Workbench v0.49 adds an offline-update-compatible, lease-gated read-only MCP Streamable HTTP subset on IPv4 loopback. Official C# ModelContextProtocol 2.2.0 client interoperability is required qualification evidence; it is not a runtime package or transport authority. Secure tunnel/public activation remains separate.");
    }

    private static void Add(List<WorkbenchAcceptanceCheck> destination, IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> checks)
        => destination.AddRange(checks.Select(x => new WorkbenchAcceptanceCheck(x.Id, x.Passed, x.Observed, x.Expected)));
}
