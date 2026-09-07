using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

internal sealed class WorkbenchV0601AcceptanceHarness
{
    private readonly WorkbenchV0542AcceptanceHarness _predecessor;
    private readonly MainWindow _window;

    internal WorkbenchV0601AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness, MainWindow window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _predecessor = new WorkbenchV0542AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)), _window);
    }

    internal async Task<WorkbenchAcceptanceReceipt> RunAsync(
        RuntimeContext context,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        // Deliberately stop the inherited harness chain at v0.54.2. The v0.55/v0.55.2
        // harnesses contain version-specific routing/title observations and must not be
        // reinterpreted for this successor. Provider-neutral v0.55 contracts are run
        // separately below.
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);

        var v055InvocationChecks = BoundedLocalModelInvocationV055Service.RunOfflineContractChecks()
            .Select(x => new WorkbenchAcceptanceCheck("v0601-v055-invocation-" + x.Id, x.Passed, x.Observed, x.Expected));
        var v055ParserChecks = LocalModelInvocationRequestV055Parser.RunOfflineContractChecks()
            .Select(x => new WorkbenchAcceptanceCheck("v0601-v055-parser-" + x.Id, x.Passed, x.Observed, x.Expected));
        var reviewedSourceChecks = V0601QualifiedSourceBindings.Run(workspaceRoot);
        var checkpointChecks = LocalCheckpointV0601Service.RunOfflineContractChecks()
            .Select(x => new WorkbenchAcceptanceCheck("v0601-" + x.Id, x.Passed, x.Observed, x.Expected));
        var routingChecks = _window.ObserveV0601RoutingContract()
            .Select(x => new WorkbenchAcceptanceCheck("v0601-routing-" + x.Id, x.Passed, x.Observed, x.Expected));

        var evidenceChecks = new List<WorkbenchAcceptanceCheck>();
        try
        {
            var imported = PublicMainImportReceiptVerifierV0601.FindExact(workspaceRoot);
            evidenceChecks.Add(new WorkbenchAcceptanceCheck(
                "v0601-public-main-import-receipt",
                imported.ImportedCommit == PublicMainImportReceiptVerifierV0601.PublicMainCommit,
                $"{imported.ImportedCommit}/{imported.ReceiptSha256}",
                $"{PublicMainImportReceiptVerifierV0601.PublicMainCommit}/exact receipt SHA"));
            evidenceChecks.Add(new WorkbenchAcceptanceCheck(
                "v0601-public-main-import-refs",
                string.Equals(imported.RefsDigestBefore, imported.RefsDigestAfter, StringComparison.OrdinalIgnoreCase),
                $"{imported.RefsDigestBefore}/{imported.RefsDigestAfter}",
                "refs digest unchanged"));
        }
        catch (Exception ex)
        {
            evidenceChecks.Add(new WorkbenchAcceptanceCheck(
                "v0601-public-main-import", false, ex.Message, "exact fixed no-ref public-main import receipt"));
        }

        var successorChecks = v055InvocationChecks
            .Concat(v055ParserChecks)
            .Concat(reviewedSourceChecks)
            .Concat(evidenceChecks)
            .Concat(checkpointChecks)
            .Concat(routingChecks)
            .ToArray();
        var checks = predecessor.Checks.Concat(successorChecks).ToArray();
        var passed = predecessor.Passed && successorChecks.All(x => x.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.60.1 is an acceptance/update successor and does not relabel reviewed v0.60 bytes as an accepted v0.60 release",
            "v0.56-v0.60 provenance, capability-evidence and live Authority/Evidence sources are byte-bound and not reinterpreted",
            "human-reviewed neutral branding is byte-bound; historical v0.55.2 -> v0.60 artwork remains historical provenance evidence only",
            "v0.55 model invocation contracts are rechecked offline without performing a model invocation",
            "acceptance performs no artifact acquisition, runtime materialization, runtime execution, model invocation, benchmark, game or display action",
            "public-main import receipt is read-only evidence; no network operation is performed by acceptance",
            "local checkpoint is the only post-PASS Git authority and is fixed to two exact parents plus workbench-v0.60.1-accepted",
            "remote publication remains a separate future explicit authority boundary",
            "no ResponseAuthority, DisplayPermit, Agent Execute, ActionPermit or SuccessorPermit is created",
            "No Workbench Network Transport != OS-Level Process Network Isolation",
            "automatic retry is not authorized; failed transition bootstrap remains FAILED_NO_RETRY"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            LocalCheckpointV0601Service.AcceptanceSchema,
            LocalCheckpointV0601Service.Version,
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
            "Workbench v0.60.1 adds only a bounded operator-host first-boot/local-checkpoint closure above the already reviewed v0.60 implementation. It requires exact predecessor, no-ref public-main evidence, reviewed source bindings and a fresh transition-bootstrap lease. Publication remains separate.");
    }
}
