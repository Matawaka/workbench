using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed class WorkbenchV0511AcceptanceHarness
{
    private readonly WorkbenchV051AcceptanceHarness _predecessor;
    private readonly MainWindow _window;

    public WorkbenchV0511AcceptanceHarness(
        WorkbenchAcceptanceHarness acceptedV031Harness,
        MainWindow window)
    {
        _predecessor = new WorkbenchV051AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)),
            window ?? throw new ArgumentNullException(nameof(window)));
        _window = window;
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(
        RuntimeContext context,
        CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(_window);
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);

        var successorChecks = new List<WorkbenchAcceptanceCheck>();
        Add(successorChecks, _window.ObserveV0511AutoMcpContract());
        Add(successorChecks, LocalCheckpointV0511Service.RunOfflineContractChecks());

        successorChecks.AddRange(
            OperatorSurfaceV045Contract.Observe(_window)
                .Select(x => new WorkbenchAcceptanceCheck(
                    "v0511-preserve-" + x.Id,
                    x.Passed,
                    x.Observed,
                    x.Expected)));

        var checks = predecessor.Checks.Concat(successorChecks).ToArray();
        var passed = predecessor.Passed && successorChecks.All(x => x.Passed);

        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.51.1 changes local operator orchestration only; the accepted v0.51 read/browse lease and MCP authorization semantics remain unchanged",
            "explicit Read session lease confirmation authorizes the combined local sequence lease-create -> exact clipboard grant handoff -> local MCP adapter start",
            "automatic MCP startup consumes the exact just-created ApplicationId/LeaseId/bearer grant and does not create, renew or widen read authority",
            "clipboard is used only as the operator-visible grant handoff: automatic startup requires an immediate exact string round-trip before the grant is accepted",
            "if lease creation succeeds but MCP startup fails, the lease remains explicit local authority, automatic retry is absent, and the operator must manually start MCP or revoke the lease",
            "local MCP remains IPv4 loopback-only and exposes the existing v0.51 two-tool surface",
            "no Secure MCP Tunnel, outbound HTTPS, ChatGPT/plugin mutation or OpenAI bridge operation is automatically started",
            "manual Start/Stop local MCP and Revoke read leases remain available",
            "no catalog mutation, application/source mutation, Agent Execute, ActionPermit, arbitrary process execution or Stable Core authority"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.51.1",
            "0.51.1",
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
            "Workbench v0.51.1 simplifies the local read workflow: one explicit Read session lease confirmation creates the bounded lease, copies its exact grant JSON to clipboard, verifies the clipboard round-trip, and starts the existing lease-gated local MCP adapter. The workflow adds no tunnel or filesystem authority.");
    }

    private static void Add(
        List<WorkbenchAcceptanceCheck> destination,
        IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> checks)
        => destination.AddRange(
            checks.Select(x => new WorkbenchAcceptanceCheck(
                x.Id, x.Passed, x.Observed, x.Expected)));
}
