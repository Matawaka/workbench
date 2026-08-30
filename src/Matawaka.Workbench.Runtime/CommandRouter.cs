using Matawaka.Workbench.AgentHost;
using Matawaka.Workbench.Catalog;
using Matawaka.Workbench.Engine;
using Matawaka.Workbench.Protocol;

namespace Matawaka.Workbench.Runtime;

public sealed record RuntimeContext(
    string CatalogRoot,
    bool AgentEnabled,
    bool AllowGitFetch);

public sealed record EvidenceReceipt(
    string Schema,
    string CommandId,
    IReadOnlyList<CatalogRepository> CatalogSnapshot,
    AgentEvidenceCoverage Coverage,
    IReadOnlyList<AgentEvidence> Items,
    IReadOnlyList<string> NonEffects);

public sealed record CommandResult(
    string Kind,
    CommandTerminalState TerminalState,
    string Summary,
    object? Data = null,
    object? Evidence = null,
    object? Authority = null,
    object? Agent = null,
    object? Semantic = null);

public interface ICommandRunner
{
    Task<CommandResult> RunAsync(
        CommandEnvelope command,
        RuntimeContext context,
        IProgress<WorkbenchProgress>? progress,
        CancellationToken cancellationToken);
}

public sealed class CommandRouter : ICommandRunner
{
    private readonly IAnalyticFutureAdapter _engine;
    private readonly CatalogService _catalog;
    private readonly DevelopmentAgentHost _agent;

    public CommandRouter(
        IAnalyticFutureAdapter? engine = null,
        CatalogService? catalog = null,
        DevelopmentAgentHost? agent = null)
    {
        _engine = engine ?? new WeightedAnalyticFutureAdapter();
        _catalog = catalog ?? new CatalogService();
        _agent = agent ?? new DevelopmentAgentHost();
    }

    public async Task<CommandResult> RunAsync(
        CommandEnvelope command,
        RuntimeContext context,
        IProgress<WorkbenchProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new WorkbenchProgress(
            command.Id, "command.accepted", 0, command.Kind, DateTimeOffset.Now,
            "ROUTING", "COMMAND_ACCEPTED", "NONE",
            command.Kind.Equals("agent.run", StringComparison.OrdinalIgnoreCase) ? "AUTHORITY_DECISION" : "COMMAND_HANDLER",
            $"command:{command.Id}"));

        CommandResult result;
        switch (command.Kind.ToLowerInvariant())
        {
            case "analysis.run":
                var decision = await _engine.EvaluateAsync(command.Payload, cancellationToken);
                result = new CommandResult(
                    command.Kind,
                    CommandTerminalState.Completed,
                    $"Ranked {decision.Ranked.Count} options.",
                    decision);
                break;

            case "catalog.inspect":
                var repos = await _catalog.InspectAsync(context.CatalogRoot, progress, command.Id, cancellationToken);
                result = new CommandResult(
                    command.Kind,
                    CommandTerminalState.Completed,
                    $"Found {repos.Count} repositories.",
                    repos);
                break;

            case "catalog.fetch":
                await _catalog.FetchAsync(context.CatalogRoot, context.AllowGitFetch, progress, command.Id, cancellationToken);
                result = new CommandResult(
                    command.Kind,
                    CommandTerminalState.Completed,
                    "Catalog refs fetched.");
                break;

            case "agent.run":
                var snapshot = await _catalog.InspectAsync(context.CatalogRoot, progress, command.Id, cancellationToken);
                var receipt = await _agent.RunAsync(command, snapshot, context.AgentEnabled, progress, cancellationToken);
                var authorityReceipt = new CapabilityReceipt(
                    "matawaka.capability-receipt/v1",
                    receipt.CapabilityRequest,
                    receipt.CapabilityDecision);

                if (string.Equals(receipt.Status, "denied", StringComparison.OrdinalIgnoreCase))
                {
                    result = new CommandResult(
                        command.Kind,
                        CommandTerminalState.Denied,
                        $"Agent {receipt.Mode} denied by typed capability policy; mutations=0.",
                        receipt.CapabilityDecision,
                        null,
                        authorityReceipt,
                        receipt);
                }
                else
                {
                    object agentData = receipt.Proposal is not null ? receipt.Proposal : receipt.Findings;
                    var evidenceReceipt = new EvidenceReceipt(
                        "matawaka.evidence-receipt/v1",
                        command.Id,
                        receipt.CatalogSnapshot,
                        receipt.Coverage,
                        receipt.Evidence,
                        receipt.CapabilityDecision.NonEffects);

                    var semanticReceipt = receipt.SemanticProviderSelection is null
                        ? null
                        : new
                        {
                            Selection = receipt.SemanticProviderSelection,
                            Analysis = receipt.SemanticAnalysis,
                            Boundary = receipt.SemanticProviderBoundary
                        };

                    result = new CommandResult(
                        command.Kind,
                        CommandTerminalState.Completed,
                        $"Agent {receipt.Mode} checkpoint completed with {receipt.Evidence.Count} balanced evidence items from {receipt.Coverage.RepositoriesRepresented} repositories and {receipt.Mutations.Count} mutations.",
                        agentData,
                        evidenceReceipt,
                        authorityReceipt,
                        receipt,
                        semanticReceipt);
                }
                break;

            default:
                throw new InvalidDataException($"Unsupported command kind: {command.Kind}");
        }

        var terminalEvent = result.TerminalState == CommandTerminalState.Denied
            ? "command.denied"
            : "command.completed";

        progress?.Report(new WorkbenchProgress(
            command.Id,
            terminalEvent,
            100,
            result.Summary,
            DateTimeOffset.Now,
            "TERMINAL",
            result.TerminalState.ToString().ToUpperInvariant(),
            "NONE",
            "NONE",
            $"command:{command.Id}"));

        return result;
    }
}
