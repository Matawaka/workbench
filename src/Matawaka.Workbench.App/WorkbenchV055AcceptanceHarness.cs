using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed class WorkbenchV055AcceptanceHarness
{
    private readonly WorkbenchV0542AcceptanceHarness _predecessor;
    private readonly MainWindow _window;

    public WorkbenchV055AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness, MainWindow window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _predecessor = new WorkbenchV0542AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)), _window);
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(RuntimeContext context, CancellationToken cancellationToken)
    {
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var invocationChecks = BoundedLocalModelInvocationV055Service.RunOfflineContractChecks()
            .Select(x => new WorkbenchAcceptanceCheck("v055-" + x.Id, x.Passed, x.Observed, x.Expected))
            .ToArray();
        var parserChecks = LocalModelInvocationRequestV055Parser.RunOfflineContractChecks()
            .Select(x => new WorkbenchAcceptanceCheck("v055-" + x.Id, x.Passed, x.Observed, x.Expected))
            .ToArray();
        var routingChecks = _window.ObserveV055RoutingContract()
            .Select(x => new WorkbenchAcceptanceCheck("v055-" + x.Id, x.Passed, x.Observed, x.Expected))
            .ToArray();
        var checkpointChecks = LocalCheckpointV055Service.RunOfflineContractChecks()
            .Select(x => new WorkbenchAcceptanceCheck("v055-" + x.Id, x.Passed, x.Observed, x.Expected))
            .ToArray();
        var successorChecks = invocationChecks.Concat(parserChecks).Concat(routingChecks).Concat(checkpointChecks).ToArray();
        var checks = predecessor.Checks.Concat(successorChecks).ToArray();
        var passed = predecessor.Passed && successorChecks.All(x => x.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.55 adds a separate bounded one-shot local-model invocation authority above exact runtime/model evidence",
            "v0.53 generic process execution is not reinterpreted as model request authority",
            "v0.55 request JSON is closed: unknown, duplicate or missing properties are refused before Preview",
            "acceptance runs contract/self-test checks only and performs no model invocation",
            "no raw request text or bearer persistence is required by canonical v0.55 lease state",
            "successful local output remains UNTRUSTED_LOCAL_MODEL_OUTPUT and creates no response/display/game authority",
            "No Workbench Network Transport != OS-Level Process Network Isolation",
            "real LM1/llama.cpp/CUDA acquisition and KONTUR inference remain separately unauthorized",
            "remote publication remains deferred until a separate real-host v0.55 fixture admission/publication successor"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.55",
            "0.55",
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
            "Workbench v0.55 introduces a separate provider-neutral one-shot model invocation lease with exact runtime/model evidence binding, closed request JSON, bounded request/stdout/stderr and untrusted output semantics. First-boot acceptance does not invoke a model or authorize publication.");
    }
}
