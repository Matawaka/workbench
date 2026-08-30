using Matawaka.Workbench.AgentHost;
using Matawaka.Workbench.Catalog;
using Matawaka.Workbench.Engine;
using Matawaka.Workbench.Protocol;

namespace Matawaka.Workbench.Runtime;

public sealed record RuntimeContext(
    string CatalogRoot,
    bool AgentEnabled,
    bool AllowGitFetch);

public sealed record CommandResult(
    string Kind,
    string Summary,
    object? Data = null,
    object? Evidence = null,
    object? Agent = null);

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
        progress?.Report(new WorkbenchProgress(command.Id, "command.accepted", 0, command.Kind, DateTimeOffset.Now));

        CommandResult result;
        switch (command.Kind.ToLowerInvariant())
        {
            case "analysis.run":
                var decision = await _engine.EvaluateAsync(command.Payload, cancellationToken);
                result = new CommandResult(command.Kind, $"Ranked {decision.Ranked.Count} options.", decision);
                break;

            case "catalog.inspect":
                var repos = await _catalog.InspectAsync(context.CatalogRoot, progress, command.Id, cancellationToken);
                result = new CommandResult(command.Kind, $"Found {repos.Count} repositories.", repos);
                break;

            case "catalog.fetch":
                await _catalog.FetchAsync(context.CatalogRoot, context.AllowGitFetch, progress, command.Id, cancellationToken);
                result = new CommandResult(command.Kind, "Catalog refs fetched.");
                break;

            case "agent.run":
                var snapshot = await _catalog.InspectAsync(context.CatalogRoot, progress, command.Id, cancellationToken);
                var receipt = await _agent.RunAsync(command, snapshot, context.AgentEnabled, progress, cancellationToken);

                if (string.Equals(receipt.Status, "denied", StringComparison.OrdinalIgnoreCase))
                {
                    result = new CommandResult(
                        command.Kind,
                        $"Agent {receipt.Mode} denied by typed capability policy; mutations=0.",
                        receipt.CapabilityDecision,
                        null,
                        receipt);
                }
                else
                {
                    object agentData = receipt.Proposal is not null ? receipt.Proposal : receipt.Findings;
                    result = new CommandResult(
                        command.Kind,
                        $"Agent {receipt.Mode} checkpoint completed with {receipt.Evidence.Count} balanced evidence items from {receipt.Coverage.RepositoriesRepresented} repositories and {receipt.Mutations.Count} mutations.",
                        agentData,
                        new
                        {
                            receipt.Coverage,
                            Items = receipt.Evidence
                        },
                        receipt);
                }
                break;

            default:
                throw new InvalidDataException($"Unsupported command kind: {command.Kind}");
        }

        progress?.Report(new WorkbenchProgress(command.Id, "command.completed", 100, result.Summary, DateTimeOffset.Now));
        return result;
    }
}
