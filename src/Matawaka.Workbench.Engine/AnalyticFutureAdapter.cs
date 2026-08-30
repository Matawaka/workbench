using System.Text.Json;
using Matawaka.Workbench.Protocol;

namespace Matawaka.Workbench.Engine;

public interface IAnalyticFutureAdapter
{
    Task<DecisionEnvelope> EvaluateAsync(JsonElement payload, CancellationToken cancellationToken);
}

public sealed record EngineWeights(
    double Utility = 0.24,
    double Availability = 0.20,
    double Evidence = 0.18,
    double Reversibility = 0.14,
    double Learning = 0.10,
    double RiskPenalty = 0.20,
    double CostPenalty = 0.10);

public sealed class WeightedAnalyticFutureAdapter : IAnalyticFutureAdapter
{
    private readonly EngineWeights _weights;

    public WeightedAnalyticFutureAdapter(EngineWeights? weights = null)
    {
        _weights = weights ?? new EngineWeights();
    }

    public Task<DecisionEnvelope> EvaluateAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!payload.TryGetProperty("options", out var options) || options.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("analysis.run requires payload.options[].");

        var scores = new List<DecisionScore>();
        foreach (var option in options.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = Text(option, "id");
            var score =
                Scalar(option, "utility") * _weights.Utility +
                Scalar(option, "availability") * _weights.Availability +
                Scalar(option, "evidence") * _weights.Evidence +
                Scalar(option, "reversibility") * _weights.Reversibility +
                Scalar(option, "learning") * _weights.Learning -
                Scalar(option, "risk") * _weights.RiskPenalty -
                Scalar(option, "cost") * _weights.CostPenalty;
            scores.Add(new DecisionScore(id, Math.Round(score, 6)));
        }

        var ranked = scores.OrderByDescending(x => x.Score).ToArray();
        return Task.FromResult(new DecisionEnvelope(
            "uu-aap-bridge-v0",
            ranked,
            "Non-normative, tunable bridge profile; availability/intent/authority remain separate gates."));
    }

    private static string Text(JsonElement node, string name)
        => node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? name
            : name;

    private static double Scalar(JsonElement node, string name)
    {
        if (!node.TryGetProperty(name, out var value) || !value.TryGetDouble(out var result))
            return 0.0;
        return Math.Clamp(result, 0.0, 1.0);
    }
}
