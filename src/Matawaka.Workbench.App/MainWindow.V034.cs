using System.IO;
using System.Text;
using System.Windows;
using Matawaka.Workbench.Protocol;
using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalCheckpointV034Service _checkpointV034Service = new();
    private readonly FixedGitHubPublicationV034Service _fixedGitHubPublicationV034Service = new();
    private readonly MaintenanceLifecycleReceiptService _maintenanceLifecycleReceiptService = new();

    private async void SelfTestV034Button_Click(object sender, RoutedEventArgs e)
    {
        var id = $"self-test-v0.34-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            if (AgentEnabledBox.IsChecked != true)
                throw new InvalidDataException("Self-test requires 'Агент включен' to be explicitly enabled.");

            SaveSettings();
            BeginRun(id);
            StatusText.Text = "RUNNING: v0.34 acceptance + offline lifecycle contract checks";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  acceptance.started           v0.34 matrix; lifecycleEffect=false");

            var context = new RuntimeContext(CatalogRootBox.Text, true, false);
            var harness = new WorkbenchV034AcceptanceHarness(_acceptanceHarness);
            var receipt = await harness.RunAsync(context, _cts!.Token);

            var artifactDir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
            Directory.CreateDirectory(artifactDir);
            var artifactPath = Path.Combine(artifactDir, $"v0.34-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            await File.WriteAllTextAsync(artifactPath, CommandCodec.Serialize(receipt), new UTF8Encoding(false), _cts.Token);

            _lastAcceptanceReceipt = receipt;
            _lastAcceptanceArtifactPath = artifactPath;
            _lastAcceptanceConsumed = false;
            AcceptCheckpointButton.IsEnabled = receipt.Passed;

            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Receipt = receipt,
                ArtifactPath = artifactPath,
                LifecycleAssessmentPerformed = false,
                LifecycleReceiptWritten = false,
                LocalCheckpointAvailable = receipt.Passed
            });
            OutputTabs.SelectedItem = AcceptanceTab;
            ProgressBar.Value = 100;
            _currentTerminalState = receipt.Passed ? CommandTerminalState.Completed : CommandTerminalState.Failed;
            StatusText.Text = receipt.Passed
                ? $"COMPLETED: v0.34 acceptance PASSED; lifecycleEffect=false; {artifactPath}"
                : $"FAILED: v0.34 acceptance matrix has failing checks; {artifactPath}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  acceptance.{(receipt.Passed ? "completed" : "failed"),-18} passed={receipt.Passed}; lifecycleEffect=false; {artifactPath}");
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally { EndRun(); }
    }

    private async void AcceptCheckpointV034Button_Click(object sender, RoutedEventArgs e)
    {
        var id = $"accept-v0.34-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            if (_lastAcceptanceReceipt is null || !_lastAcceptanceReceipt.Passed ||
                !string.Equals(_lastAcceptanceReceipt.Version, LocalCheckpointV034Service.Version, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(_lastAcceptanceArtifactPath))
                throw new InvalidDataException("Run a passing v0.34 Self-test in this Workbench process before accepting the local checkpoint.");
            if (_lastAcceptanceConsumed)
                throw new InvalidDataException("The latest v0.34 Self-test receipt has already been consumed by local checkpoint acceptance.");

            SaveSettings();
            var candidate = await _checkpointV034Service.PreviewAsync(
                WorkspaceRootBox.Text, _lastAcceptanceArtifactPath, _lastAcceptanceReceipt, CancellationToken.None);

            var preview = new StringBuilder();
            preview.AppendLine("Создать локальный accepted checkpoint Workbench v0.34?");
            preview.AppendLine();
            preview.AppendLine($"Predecessor: {candidate.PreviousHead} / {candidate.ExpectedPredecessorTag}");
            preview.AppendLine($"Target tag: {candidate.TargetTag}");
            preview.AppendLine($"Acceptance SHA-256: {candidate.AcceptanceArtifactSha256}");
            preview.AppendLine($"Build-source manifest SHA-256: {candidate.BuildSourceManifestSha256}");
            preview.AppendLine();
            preview.AppendLine("Это только локальный commit/tag. Publish accepted и Lifecycle receipt остаются двумя отдельными последующими действиями. Сеть, remote mutation, Agent Execute и lifecycle authority здесь не разрешаются.");

            if (MessageBox.Show(this, preview.ToString(), "Принять Workbench v0.34", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            BeginRun(id);
            StatusText.Text = "RUNNING: explicit local Workbench v0.34 checkpoint";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  checkpoint.requested        tag={candidate.TargetTag}; remotePublication=false; lifecycle=false");

            var receipt = await _checkpointV034Service.AcceptAsync(candidate, _cts!.Token);
            var receiptPath = await LocalCheckpointV034Service.WriteReceiptAsync(WorkspaceRootBox.Text, receipt, _cts.Token);
            _lastAcceptanceConsumed = true;

            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Acceptance = _lastAcceptanceReceipt,
                AcceptanceArtifactPath = _lastAcceptanceArtifactPath,
                Checkpoint = receipt,
                CheckpointReceiptPath = receiptPath,
                NextExplicitActions = new[] { "Publish accepted", "Lifecycle receipt only after publication" }
            });
            OutputTabs.SelectedItem = AcceptanceTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: {receipt.Tag} -> {receipt.NewHead}; publication/lifecycle still separate";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  checkpoint.completed        {receipt.Tag} -> {receipt.NewHead}; remotePush=false; lifecycle=false");
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally { EndRun(); }
    }

    private async void PublishAcceptedV034Button_Click(object sender, RoutedEventArgs e)
    {
        var id = $"publish-accepted-v0.34-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            SaveSettings();
            var candidate = await _fixedGitHubPublicationV034Service.PreviewAsync(WorkspaceRootBox.Text, CancellationToken.None);

            var preview = new StringBuilder();
            preview.AppendLine("Опубликовать уже принятый Workbench v0.34 в фиксированный GitHub repository?");
            preview.AppendLine();
            preview.AppendLine($"Remote: {candidate.RemoteName}");
            preview.AppendLine($"URL: {candidate.RemoteUrl}");
            preview.AppendLine($"Accepted HEAD: {candidate.Head}");
            preview.AppendLine($"Parent: {candidate.Parent}");
            preview.AppendLine($"Tag: {candidate.AcceptedTag}");
            preview.AppendLine();
            preview.AppendLine("Разрешён только exact fast-forward/tag к фиксированному repository. Lifecycle receipt НЕ создаётся автоматически и не получает authority из publication success.");

            if (MessageBox.Show(this, preview.ToString(), "Publish accepted v0.34", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            BeginRun(id);
            StatusText.Text = "RUNNING: fixed v0.34 accepted-source GitHub publication";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  publication.requested       head={candidate.Head}; force=false; lifecycle=false");

            var receipt = await _fixedGitHubPublicationV034Service.PublishAsync(candidate, _cts!.Token);
            var receiptPath = await FixedGitHubPublicationV034Service.WriteReceiptAsync(WorkspaceRootBox.Text, receipt, _cts.Token);

            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Publication = receipt,
                PublicationReceiptPath = receiptPath,
                LifecycleReceiptAutomatic = false,
                NextExplicitAction = "Lifecycle receipt"
            });
            OutputTabs.SelectedItem = AcceptanceTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: remote main/tag == {receipt.LocalHead}; Lifecycle receipt available separately";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  publication.completed       main={receipt.RemoteMainAfter}; tag={receipt.RemoteTagAfter}; lifecycle=false");
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally { EndRun(); }
    }

    private async void LifecycleReceiptButton_Click(object sender, RoutedEventArgs e)
    {
        var id = $"lifecycle-v0.34-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            SaveSettings();
            // Assessment is read-only except for fixed local Git observations.
            // It will fail closed unless exact completed update/accept/checkpoint/publication
            // artifacts can be uniquely bound by digests and accepted commit identity.
            var assessment = await _maintenanceLifecycleReceiptService.AssessAsync(
                WorkspaceRootBox.Text, CancellationToken.None);

            var preview = new StringBuilder();
            preview.AppendLine("Записать Maintenance Lifecycle Receipt v0.34?");
            preview.AppendLine();
            preview.AppendLine($"Predecessor: {assessment.PredecessorCommit}");
            preview.AppendLine($"Accepted commit: {assessment.AcceptedCommit}");
            preview.AppendLine($"Candidate executable SHA-256: {assessment.CandidateExecutableSha256}");
            preview.AppendLine($"Orchestrator: {assessment.Orchestrator.Sha256}");
            preview.AppendLine($"Acceptance: {assessment.Acceptance.Sha256}");
            preview.AppendLine($"Checkpoint: {assessment.Checkpoint.Sha256}");
            preview.AppendLine($"Publication: {assessment.Publication.Sha256}");
            preview.AppendLine($"Checks: {assessment.Checks.Count}; all pass={assessment.Complete}");
            preview.AppendLine();
            preview.AppendLine("Это только локальная запись связанного evidence. Никакие update/build/launch/Self-test/checkpoint/publish/retry/rollback действия не выполняются и не авторизуются.");

            if (MessageBox.Show(this, preview.ToString(), "Lifecycle receipt v0.34", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes)
                return;

            BeginRun(id);
            StatusText.Text = "RUNNING: local lifecycle evidence write only";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  lifecycle.write.requested   complete={assessment.Complete}; actionAuthority=false");

            var receiptPath = await MaintenanceLifecycleReceiptService.WriteReceiptAsync(
                WorkspaceRootBox.Text, assessment, _cts!.Token);
            LifecycleTextBox.Text = CommandCodec.Serialize(new
            {
                Assessment = assessment,
                LifecycleReceiptPath = receiptPath,
                AuthorityCreated = false,
                ActionPerformed = false
            });
            OutputTabs.SelectedItem = LifecycleTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: lifecycle evidence bound; {receiptPath}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  lifecycle.write.completed   complete=true; authority=false; {receiptPath}");
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally { EndRun(); }
    }
}
