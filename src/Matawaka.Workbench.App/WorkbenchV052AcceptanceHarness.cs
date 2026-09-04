using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed class WorkbenchV052AcceptanceHarness
{
    private readonly WorkbenchV05113AcceptanceHarness _predecessor;
    private readonly MainWindow _window;

    public WorkbenchV052AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness, MainWindow window)
    {
        _predecessor = new WorkbenchV05113AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)),
            window ?? throw new ArgumentNullException(nameof(window)));
        _window = window;
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(RuntimeContext context, CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(_window);
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var successorChecks = new List<WorkbenchAcceptanceCheck>();

        successorChecks.AddRange(BoundedArtifactAcquisitionV052Service.RunOfflineContractChecks()
            .Select(x => new WorkbenchAcceptanceCheck("v052-acquisition-" + x.Id, x.Passed, x.Observed, x.Expected)));
        successorChecks.AddRange(LocalCheckpointV052Service.RunOfflineContractChecks()
            .Select(x => new WorkbenchAcceptanceCheck("v052-checkpoint-" + x.Id, x.Passed, x.Observed, x.Expected)));
        successorChecks.AddRange(LocalAppsActionDialogV0515.RunOfflineContractChecks()
            .Where(x => x.Id.StartsWith("chooser-v052-", StringComparison.Ordinal))
            .Select(x => new WorkbenchAcceptanceCheck("v052-ui-" + x.Id, x.Passed, x.Observed, x.Expected)));
        successorChecks.AddRange(_window.ObserveV052ArtifactAcquisitionContract()
            .Select(x => new WorkbenchAcceptanceCheck("v052-route-" + x.Id, x.Passed, x.Observed, x.Expected)));
        successorChecks.AddRange(OperatorSurfaceV045Contract.Observe(_window)
            .Select(x => new WorkbenchAcceptanceCheck("v052-preserve-" + x.Id, x.Passed, x.Observed, x.Expected)));

        var checks = predecessor.Checks.Concat(successorChecks).ToArray();
        var passed = predecessor.Passed && successorChecks.All(x => x.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.52 introduces a generic artifact-acquisition authority separate from local-app read leases and MCP runtime authority",
            "artifact selection, JSON schema validity and Preview are not acquisition authority",
            "Grant materializes one exact one-shot acquisition authority but performs no network or artifact-byte write",
            "Acquire consumes its one call before network and never automatically retries or resumes after failure/crash",
            "initial and redirected requests remain credential-free HTTPS under exact reviewed host/path-prefix rules",
            "destination root is fixed outside the Workbench Git tree; reparse/junction/symlink boundaries are fail-closed",
            "download bytes are staged as partial evidence and promoted only after exact size and SHA-256 verification",
            "pre-existing different final files are never overwritten; exact verified files may be reused without network",
            "verified artifact does not imply extraction, installation, process execution, runtime start, benchmark, model request or game access",
            "KONTUR is only one future caller of the provider-neutral contract; no KONTUR-specific runtime authority is embedded",
            "grant bearer plaintext is not persisted and is omitted from operator JSON output",
            "v0.51.13 shutdown transaction and complete MCP lifecycle remain preserved",
            "no automatic Secure MCP Tunnel, general browser/network authority, Git/catalog mutation, Agent Execute or ActionPermit",
            "top-level four-button Workbench operator surface remains unchanged"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.52",
            "0.52",
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
            "Workbench v0.52 adds a reusable bounded artifact-acquisition primitive: exact selected identity and declarative handoff remain non-authoritative until one explicit one-shot acquisition grant; downloaded bytes become usable evidence only after exact local size/SHA-256 verification, with no extraction or execution authority.");
    }
}
