using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed class WorkbenchV051111AcceptanceHarness
{
    private readonly WorkbenchV05111AcceptanceHarness _predecessor;
    private readonly MainWindow _window;

    public WorkbenchV051111AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness, MainWindow window)
    {
        _predecessor = new WorkbenchV05111AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)),
            window ?? throw new ArgumentNullException(nameof(window)));
        _window = window;
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(RuntimeContext context, CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(_window);
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var successorChecks = _window.ObserveV051111ExclusiveRoutingContract()
            .Select(x => new WorkbenchAcceptanceCheck("v051111-" + x.Id, x.Passed, x.Observed, x.Expected))
            .ToArray();

        var checks = predecessor.Checks.Concat(successorChecks).ToArray();
        var passed = predecessor.Passed && successorChecks.All(x => x.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.51.11.1 changes only Local Apps event routing/admission metadata and adds no lease/read/revoke/resume authority",
            "the inherited v0.51.8 Local Apps handler is detached after the full predecessor configure chain",
            "the direct v0.51.11 handler is replaced by one hotfix wrapper that dispatches only to the v0.51.11 handler",
            "v0.51.11 owner-to-lease binding transaction, prepared LeaseId creation, canonical lease/index semantics and listener ordering are unchanged",
            "no automatic MCP/tunnel/publication/catalog mutation, Agent Execute or ActionPermit authority",
            "top-level four-button Workbench operator surface remains unchanged"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.51.11.1",
            "0.51.11.1",
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
            "Workbench v0.51.11.1 hotfixes composed Local Apps routing so one exclusive current handler reaches the v0.51.11 owner-to-lease transaction path, while preserving all v0.51.11 authority boundaries unchanged.");
    }
}
