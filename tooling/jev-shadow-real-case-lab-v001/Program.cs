using System.Text.Json;
using Matawaka.Workbench.App;
using Matawaka.Workbench.Protocol;
using Matawaka.Workbench.Runtime;

static string FindRepoRoot(string start)
{
    var dir = new DirectoryInfo(Path.GetFullPath(start));
    while (dir is not null)
    {
        if (Directory.Exists(Path.Combine(dir.FullName, ".git")) || File.Exists(Path.Combine(dir.FullName, ".git")))
            return dir.FullName;
        dir = dir.Parent;
    }
    throw new DirectoryNotFoundException("Unable to locate repository root.");
}

static bool IsInside(string parent, string child)
{
    var relative = Path.GetRelativePath(Path.GetFullPath(parent), Path.GetFullPath(child));
    return relative == "." || (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative));
}

if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: dotnet run --project tooling/jev-shadow-real-case-lab-v001/Capture.csproj -- <command.json> <catalog-root> <private-shadow-root>");
    return 2;
}

var commandPath = Path.GetFullPath(args[0]);
var catalogRoot = Path.GetFullPath(args[1]);
var shadowRoot = Path.GetFullPath(args[2]);
var repoRoot = FindRepoRoot(AppContext.BaseDirectory);

if (IsInside(repoRoot, shadowRoot))
    throw new InvalidOperationException("Real shadow capture must be written outside the public repository root.");

var command = CommandCodec.Parse(await File.ReadAllTextAsync(commandPath));
if (!string.Equals(command.Kind, "catalog.inspect", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Shadow Lab v0.1 permits only catalog.inspect. Network, agent, mutation, and fetch commands are out of scope.");

var context = new RuntimeContext(
    CatalogRoot: catalogRoot,
    AgentEnabled: false,
    AllowGitFetch: false);

var progressEvents = new List<WorkbenchProgress>();
var progress = new Progress<WorkbenchProgress>(progressEvents.Add);
var router = new CommandRouter();

var result = await router.RunAsync(command, context, progress, CancellationToken.None);
if (result.TerminalState is not (CommandTerminalState.Completed or CommandTerminalState.Denied))
    throw new InvalidOperationException($"Unexpected terminal state for capture: {result.TerminalState}");

var exporter = new JevShadowObservationExportV001Service();
var export = await exporter.ExportAsync(shadowRoot, command, result);

Directory.CreateDirectory(shadowRoot);
var summaryPath = Path.Combine(shadowRoot, "shadow-lab-run-summary.json");
var summary = new
{
    schema = "matawaka.jev-shadow-real-case-lab/v0.1",
    sourceKind = "REAL_WORKBENCH_COMMANDROUTER_RUN",
    commandKind = command.Kind,
    commandId = command.Id,
    commandTarget = command.Target,
    catalogRootDigestOnly = true,
    catalogRootSha256 = "sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(catalogRoot))).ToLowerInvariant(),
    terminalState = result.TerminalState.ToString(),
    summary = result.Summary,
    allowGitFetch = false,
    agentEnabled = false,
    networkAuthorityCreated = false,
    mutationAuthorityCreated = false,
    shadowArtifact = export.ArtifactPath,
    shadowNormativeEffect = export.Envelope?.NormativeEffect,
    shadowAuthorityIssuance = export.Envelope?.AuthorityIssuance,
    shadowProviderInvocationAuthorized = export.Envelope?.ProviderInvocationAuthorized,
    shadowDecisionReadbackSupported = export.Envelope?.DecisionReadbackSupported,
    progressEvents = progressEvents.Select(e => new { e.Event, e.Percent, e.Phase, e.ProgressKind }).ToArray()
};

await File.WriteAllTextAsync(
    summaryPath,
    JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));

Console.WriteLine(JsonSerializer.Serialize(new
{
    status = "REAL_WORKBENCH_SHADOW_CAPTURED",
    terminalState = result.TerminalState.ToString(),
    shadowArtifact = export.ArtifactPath,
    summaryArtifact = summaryPath,
    providerInvoked = false,
    networkAuthorityCreated = false,
    mutationAuthorityCreated = false
}, new JsonSerializerOptions { WriteIndented = true }));

return 0;
