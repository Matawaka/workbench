using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public sealed class WorkbenchV0491AcceptanceHarness
{
    private readonly WorkbenchV049AcceptanceHarness _predecessor;
    private readonly MainWindow _window;

    public WorkbenchV0491AcceptanceHarness(WorkbenchAcceptanceHarness acceptedV031Harness, MainWindow window)
    {
        _predecessor = new WorkbenchV049AcceptanceHarness(
            acceptedV031Harness ?? throw new ArgumentNullException(nameof(acceptedV031Harness)),
            window ?? throw new ArgumentNullException(nameof(window)));
        _window = window;
    }

    public async Task<WorkbenchAcceptanceReceipt> RunAsync(RuntimeContext context, CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(_window);
        var predecessor = await _predecessor.RunAsync(context, cancellationToken);
        var successorChecks = new List<WorkbenchAcceptanceCheck>();
        Add(successorChecks, LocalCheckpointV0491Service.RunOfflineContractChecks());
        Add(successorChecks, FixedGitHubPublicationV0491Service.RunOfflineContractChecks());
        successorChecks.Add(new WorkbenchAcceptanceCheck(
            "v0491-runtime-base-dotnet",
            LocalAppMcpReadAdapterV049Service.RuntimeDependencyProfile == "base-dotnet-tcp-listener-no-aspnet",
            LocalAppMcpReadAdapterV049Service.RuntimeDependencyProfile,
            "base-dotnet-tcp-listener-no-aspnet"));
        successorChecks.Add(new WorkbenchAcceptanceCheck(
            "v0491-runtime-chunked-bounded",
            LocalAppMcpReadAdapterV049Service.BoundedChunkedRequestsSupported,
            LocalAppMcpReadAdapterV049Service.BoundedChunkedRequestsSupported.ToString(),
            "True"));
        successorChecks.AddRange(OperatorSurfaceV045Contract.Observe(_window).Select(x => new WorkbenchAcceptanceCheck(
            "v0491-preserve-" + x.Id, x.Passed, x.Observed, x.Expected)));

        var checks = predecessor.Checks.Concat(successorChecks).ToArray();
        var passed = predecessor.Passed && successorChecks.All(x => x.Passed);
        var nonEffects = predecessor.NonEffects.Concat(new[]
        {
            "v0.49.1 is a stabilization of the local failed real-host v0.49 checkpoint and does not reclassify v0.49 as a published accepted frontier",
            "Microsoft.AspNetCore/Kestrel runtime dependency is removed; no package/framework installation authority is created",
            "bounded chunked HTTP admission exists only to interoperate with the official MCP client and remains capped by the 64 KiB protocol body ceiling",
            "MCP caller still cannot select ApplicationId, LeaseId, bearer or filesystem root",
            "every content read still delegates to the accepted v0.48 lease gate",
            "failed workbench-v0.49-accepted tag remains local-only and must remain absent remotely",
            "no Secure MCP Tunnel, public endpoint, account login, application/source mutation, Agent Execute or ActionPermit authority"
        }).Distinct(StringComparer.Ordinal).ToArray();

        return new WorkbenchAcceptanceReceipt(
            "matawaka.workbench-acceptance-receipt/v0.49.1",
            "0.49.1",
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
            "Workbench v0.49.1 closes the v0.49 real-host Microsoft.AspNetCore runtime dependency gap by using bounded base-.NET loopback HTTP while preserving the v0.48 lease authority boundary and official ModelContextProtocol 2.2.0 interoperability qualification.");
    }

    private static void Add(List<WorkbenchAcceptanceCheck> destination, IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> checks)
        => destination.AddRange(checks.Select(x => new WorkbenchAcceptanceCheck(x.Id, x.Passed, x.Observed, x.Expected)));
}
