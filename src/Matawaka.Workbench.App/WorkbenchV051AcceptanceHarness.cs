using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed class WorkbenchV051AcceptanceHarness
{
    private readonly WorkbenchV0502AcceptanceHarness _predecessor;
    private readonly MainWindow _window;

    public WorkbenchV051AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness, MainWindow window)
    {
        _predecessor = new WorkbenchV0502AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)),
            window ?? throw new ArgumentNullException(nameof(window)));
        _window = window;
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(RuntimeContext context, CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(_window);
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var successorChecks = new List<WorkbenchAcceptanceCheck>();
        Add(successorChecks, LocalAppReadLeaseV048Service.RunV051BrowseContractChecks());
        Add(successorChecks, LocalAppMcpReadAdapterV049Service.RunV051BrowseContractChecks());
        Add(successorChecks, LocalCheckpointV051Service.RunOfflineContractChecks());
        Add(successorChecks, FixedGitHubPublicationV051Service.RunOfflineContractChecks());
        successorChecks.AddRange(OperatorSurfaceV045Contract.Observe(_window).Select(x => new WorkbenchAcceptanceCheck(
            "v051-preserve-" + x.Id, x.Passed, x.Observed, x.Expected)));

        var checks = predecessor.Checks.Concat(successorChecks).ToArray();
        var passed = predecessor.Passed && successorChecks.All(x => x.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.51 adds directory metadata discovery only inside an already-created v0.48 directory-prefix lease scope",
            "exact-file lease scope does not authorize parent or sibling enumeration",
            "application-root browse wildcard, recursive walk and glob/search authority remain absent",
            "list_local_app_entries discloses only immediate-child relative path, file/directory kind and file size where applicable; no contents, hashes, timestamps or ACLs",
            "each browse call atomically consumes one call and bounded serialized metadata bytes from the same v0.48 lease state used by reads",
            "read_local_app_chunk remains unchanged and both MCP tools keep ApplicationId, LeaseId, bearer and filesystem root outside caller arguments",
            "v0.50.2 no-auth OAuth discovery compatibility facade and v0.50.1 tunnel readiness diagnostics remain unchanged",
            "no application/source mutation, arbitrary filesystem root, process execution, tunnel CRUD/admin, OAuth, Agent Execute, ActionPermit or Stable Core authority"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.51",
            "0.51.0",
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
            "Workbench v0.51 extends the proven lease-gated ChatGPT read bridge with bounded non-recursive live app browsing. Directory visibility remains strictly subordinate to explicit v0.48 directory-prefix scopes and consumes the same call/byte budget.");
    }

    private static void Add(List<WorkbenchAcceptanceCheck> destination, IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> checks)
        => destination.AddRange(checks.Select(x => new WorkbenchAcceptanceCheck(x.Id, x.Passed, x.Observed, x.Expected)));
}
