using System.Diagnostics;
using Matawaka.Workbench.Protocol;

namespace Matawaka.Workbench.Catalog;

public sealed record CatalogRepository(
    string Name,
    string Root,
    string Branch,
    string Head);

public sealed class CatalogService
{
    public async Task<IReadOnlyList<CatalogRepository>> InspectAsync(
        string catalogRoot,
        IProgress<WorkbenchProgress>? progress,
        string commandId,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(catalogRoot))
            throw new DirectoryNotFoundException($"Catalog root not found: {catalogRoot}");

        var roots = Directory.EnumerateDirectories(catalogRoot)
            .Where(path => Directory.Exists(Path.Combine(path, ".git")) || File.Exists(Path.Combine(path, ".git")))
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var repos = new List<CatalogRepository>();
        for (var i = 0; i < roots.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = roots[i];
            var branch = await ReadGitAsync(root, ["rev-parse", "--abbrev-ref", "HEAD"], cancellationToken);
            var head = await ReadGitAsync(root, ["rev-parse", "HEAD"], cancellationToken);

            repos.Add(new CatalogRepository(
                Path.GetFileName(root),
                root,
                string.IsNullOrWhiteSpace(branch) ? "unknown" : branch,
                string.IsNullOrWhiteSpace(head) ? "unknown" : head));

            progress?.Report(new WorkbenchProgress(
                commandId,
                "catalog.repository",
                Percent(i + 1, roots.Length),
                $"{Path.GetFileName(root)} @ {ShortHead(head)}",
                DateTimeOffset.Now));
        }

        return repos;
    }

    public async Task FetchAsync(
        string catalogRoot,
        bool explicitlyAllowed,
        IProgress<WorkbenchProgress>? progress,
        string commandId,
        CancellationToken cancellationToken)
    {
        if (!explicitlyAllowed)
            throw new UnauthorizedAccessException("FreeShield boundary: git fetch is default-deny. Enable it explicitly in the UI.");

        var repos = await InspectAsync(catalogRoot, null, commandId, cancellationToken);
        for (var i = 0; i < repos.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var repo = repos[i];
            progress?.Report(new WorkbenchProgress(commandId, "catalog.fetch.started", Percent(i, repos.Count), repo.Name, DateTimeOffset.Now));

            var result = await RunGitAsync(repo.Root, ["fetch", "--all", "--prune"], cancellationToken);
            if (result.ExitCode != 0)
                throw new InvalidOperationException($"git fetch failed for {repo.Name}: {result.Error.Trim()}");

            progress?.Report(new WorkbenchProgress(commandId, "catalog.fetch.completed", Percent(i + 1, repos.Count), repo.Name, DateTimeOffset.Now));
        }
    }

    private static async Task<string> ReadGitAsync(
        string root,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await RunGitAsync(root, arguments, cancellationToken);
            return result.ExitCode == 0 ? result.Output.Trim() : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static async Task<GitResult> RunGitAsync(
        string root,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo("git")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("-C");
        psi.ArgumentList.Add(root);
        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Unable to start git.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new GitResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static string ShortHead(string head)
        => string.IsNullOrWhiteSpace(head) || head.Length <= 8 ? head : head[..8];

    private static int Percent(int value, int total)
        => total == 0 ? 100 : (int)Math.Round(value * 100d / total);

    private sealed record GitResult(int ExitCode, string Output, string Error);
}
