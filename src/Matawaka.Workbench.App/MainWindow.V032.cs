using System.IO;
using System.Text;
using System.Windows;
using Matawaka.Workbench.Protocol;
using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalCheckpointV032Service _checkpointV032Service = new();
    private readonly FixedGitHubPublicationService _fixedGitHubPublicationService = new();

    private async void SelfTestV032Button_Click(object sender, RoutedEventArgs e)
    {
        var id = $"self-test-v0.32-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            if (AgentEnabledBox.IsChecked != true)
                throw new InvalidDataException("Self-test requires 'Агент включен' to be explicitly enabled.");

            SaveSettings();
            BeginRun(id);
            StatusText.Text = "RUNNING: v0.32 acceptance + offline publisher contract checks";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  acceptance.started           v0.32 matrix; publicationEffect=false");

            var context = new RuntimeContext(
                CatalogRootBox.Text,
                AgentEnabledBox.IsChecked == true,
                false);
            var harness = new WorkbenchV032AcceptanceHarness(_acceptanceHarness);
            var receipt = await harness.RunAsync(context, _cts!.Token);

            var artifactDir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
            Directory.CreateDirectory(artifactDir);
            var artifactPath = Path.Combine(artifactDir, $"v0.32-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            await File.WriteAllTextAsync(
                artifactPath,
                CommandCodec.Serialize(receipt),
                new UTF8Encoding(false),
                _cts.Token);

            _lastAcceptanceReceipt = receipt;
            _lastAcceptanceArtifactPath = artifactPath;
            _lastAcceptanceConsumed = false;
            AcceptCheckpointButton.IsEnabled = receipt.Passed;

            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Receipt = receipt,
                ArtifactPath = artifactPath,
                FixedPublicationEffectExercised = false,
                LocalCheckpointAvailable = receipt.Passed
            });
            OutputTabs.SelectedItem = AcceptanceTab;
            ProgressBar.Value = 100;
            _currentTerminalState = receipt.Passed ? CommandTerminalState.Completed : CommandTerminalState.Failed;
            StatusText.Text = receipt.Passed
                ? $"COMPLETED: v0.32 acceptance PASSED; {artifactPath}"
                : $"FAILED: v0.32 acceptance matrix has failing checks; {artifactPath}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  acceptance.{(receipt.Passed ? "completed" : "failed"),-18} passed={receipt.Passed}; publicationEffect=false; {artifactPath}");
        }
        catch (OperationCanceledException)
        {
            ShowCancelled();
        }
        catch (InvalidDataException ex)
        {
            ShowInvalid(ex);
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
        finally
        {
            EndRun();
        }
    }

    private async void AcceptCheckpointV032Button_Click(object sender, RoutedEventArgs e)
    {
        var id = $"accept-v0.32-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            if (_lastAcceptanceReceipt is null ||
                !_lastAcceptanceReceipt.Passed ||
                !string.Equals(_lastAcceptanceReceipt.Version, "0.32.0", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(_lastAcceptanceArtifactPath))
                throw new InvalidDataException("Run a passing v0.32 Self-test in this Workbench process before accepting the local checkpoint.");
            if (_lastAcceptanceConsumed)
                throw new InvalidDataException("The latest v0.32 Self-test receipt has already been consumed by a local checkpoint acceptance.");

            SaveSettings();
            var candidate = await _checkpointV032Service.PreviewAsync(
                WorkspaceRootBox.Text,
                _lastAcceptanceArtifactPath,
                _lastAcceptanceReceipt,
                CancellationToken.None);

            var preview = new StringBuilder();
            preview.AppendLine("Создать локальный accepted checkpoint Workbench v0.32?");
            preview.AppendLine();
            preview.AppendLine($"Predecessor: {candidate.PreviousHead} / {candidate.ExpectedPredecessorTag}");
            preview.AppendLine($"Target tag: {candidate.TargetTag}");
            preview.AppendLine($"Acceptance SHA-256: {candidate.AcceptanceArtifactSha256}");
            preview.AppendLine();
            preview.AppendLine("Изменения Workbench, которые войдут в commit:");
            foreach (var file in candidate.ChangedFiles.Take(40)) preview.AppendLine($"  {file}");
            if (candidate.ChangedFiles.Count > 40) preview.AppendLine($"  ... +{candidate.ChangedFiles.Count - 40} files");
            preview.AppendLine();
            preview.AppendLine("Это только локальный commit/tag. Сеть, git push, remote publication, каталог Matawaka и Agent Execute НЕ разрешаются. Publish accepted остаётся отдельным следующим решением.");

            if (MessageBox.Show(this, preview.ToString(), "Принять Workbench v0.32", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            BeginRun(id);
            StatusText.Text = "RUNNING: explicit local Workbench v0.32 checkpoint";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  checkpoint.requested        tag={candidate.TargetTag}; files={candidate.ChangedFiles.Count}; remotePublication=false");

            var receipt = await _checkpointV032Service.AcceptAsync(candidate, _cts!.Token);
            var receiptPath = await LocalCheckpointV032Service.WriteReceiptAsync(WorkspaceRootBox.Text, receipt, _cts.Token);
            _lastAcceptanceConsumed = true;

            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Acceptance = _lastAcceptanceReceipt,
                AcceptanceArtifactPath = _lastAcceptanceArtifactPath,
                Checkpoint = receipt,
                CheckpointReceiptPath = receiptPath,
                NextExplicitAction = "Publish accepted"
            });
            OutputTabs.SelectedItem = AcceptanceTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: {receipt.Tag} -> {receipt.NewHead}; publication still separate";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  checkpoint.completed        {receipt.Tag} -> {receipt.NewHead}; remotePush=false; publicationSeparate=true");
        }
        catch (OperationCanceledException)
        {
            ShowCancelled();
        }
        catch (InvalidDataException ex)
        {
            ShowInvalid(ex);
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
        finally
        {
            EndRun();
        }
    }

    private async void PublishAcceptedButton_Click(object sender, RoutedEventArgs e)
    {
        var id = $"publish-accepted-v0.32-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            SaveSettings();
            // Preview is deliberately local-only. No network effect occurs before
            // the operator sees the exact accepted HEAD/parent/tag and fixed remote.
            var candidate = await _fixedGitHubPublicationService.PreviewAsync(
                WorkspaceRootBox.Text,
                CancellationToken.None);

            var preview = new StringBuilder();
            preview.AppendLine("Опубликовать уже принятый Workbench v0.32 в фиксированный GitHub repository?");
            preview.AppendLine();
            preview.AppendLine($"Remote: {candidate.RemoteName}");
            preview.AppendLine($"URL: {candidate.RemoteUrl}");
            preview.AppendLine($"Accepted HEAD: {candidate.Head}");
            preview.AppendLine($"Parent: {candidate.Parent}");
            preview.AppendLine($"Tag: {candidate.AcceptedTag}");
            preview.AppendLine($"Remote config add required: {candidate.RemoteConfigurationRequired}");
            preview.AppendLine();
            preview.AppendLine("После подтверждения разрешается только сетевой Git-коридор к этому фиксированному repository: remote main должен быть exact parent или уже exact HEAD; push только fast-forward exact HEAD; accepted tag только отсутствующий или уже exact HEAD.");
            preview.AppendLine("Force-push, движение конфликтующего tag, другой remote/URL, каталог Matawaka, Agent Execute, ActionPermit и общий сетевой доступ запрещены.");

            if (MessageBox.Show(this, preview.ToString(), "Publish accepted v0.32", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            BeginRun(id);
            StatusText.Text = "RUNNING: fixed accepted-source GitHub publication";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  publication.requested       remote={candidate.RemoteName}; head={candidate.Head}; force=false");

            var receipt = await _fixedGitHubPublicationService.PublishAsync(candidate, _cts!.Token);
            var receiptPath = await FixedGitHubPublicationService.WriteReceiptAsync(WorkspaceRootBox.Text, receipt, _cts.Token);

            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Publication = receipt,
                PublicationReceiptPath = receiptPath
            });
            OutputTabs.SelectedItem = AcceptanceTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: remote main/tag == {receipt.LocalHead}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  publication.completed       main={receipt.RemoteMainAfter}; tag={receipt.RemoteTagAfter}; localUnchanged={receipt.LocalHeadUnchanged && receipt.WorkingTreeUnchanged}");
        }
        catch (OperationCanceledException)
        {
            ShowCancelled();
        }
        catch (InvalidDataException ex)
        {
            ShowInvalid(ex);
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
        finally
        {
            EndRun();
        }
    }
}
