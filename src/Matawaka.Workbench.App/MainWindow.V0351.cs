using System.IO;
using System.Text;
using System.Windows;
using Matawaka.Workbench.Protocol;
using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalCheckpointV0351Service _checkpointV0351Service = new();
    private readonly FixedGitHubPublicationV0351Service _fixedGitHubPublicationV0351Service = new();
    private readonly MaintenanceLifecycleReceiptV2Service _maintenanceLifecycleReceiptV2Service = new();

    private async void SelfTestV0351Button_Click(object sender, RoutedEventArgs e)
    {
        var id = $"self-test-v0.35.1-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            SaveSettings();
            SetV035PrimaryControlsEnabled(false);
            BeginRun(id);
            StatusText.Text = "RUNNING: v0.35.1 lifecycle version-key stabilization acceptance";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  acceptance.started           v0.35.1; lifecycleEffect=false; localAppEffect=false");

            var context = new RuntimeContext(CatalogRootBox.Text, true, false);
            var harness = new WorkbenchV0351AcceptanceHarness(_acceptanceHarness);
            var receipt = await harness.RunAsync(context, _cts!.Token);

            var artifactDir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
            Directory.CreateDirectory(artifactDir);
            var artifactPath = Path.Combine(artifactDir, $"v0.35.1-{DateTime.Now:yyyyMMdd-HHmmss}.json");
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
                LifecycleAdapter = "v2",
                Regression = "semantic 0.35.0 -> accepted tag/schema token 0.35",
                LifecycleEffectPerformed = false,
                LocalCheckpointAvailable = receipt.Passed
            });
            OutputTabs.SelectedItem = AcceptanceTab;
            ProgressBar.Value = 100;
            _currentTerminalState = receipt.Passed ? CommandTerminalState.Completed : CommandTerminalState.Failed;
            StatusText.Text = receipt.Passed
                ? $"COMPLETED: v0.35.1 Self-test PASSED; {artifactPath}"
                : $"FAILED: v0.35.1 acceptance matrix has failing checks; {artifactPath}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  acceptance.{(receipt.Passed ? "completed" : "failed"),-18} passed={receipt.Passed}; lifecycleEffect=false");
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally
        {
            EndRun();
            SetV035PrimaryControlsEnabled(true);
            AcceptCheckpointButton.IsEnabled = _lastAcceptanceReceipt?.Passed == true &&
                                               !_lastAcceptanceConsumed &&
                                               string.Equals(_lastAcceptanceReceipt.Version, LocalCheckpointV0351Service.Version, StringComparison.Ordinal);
        }
    }

    private async void AcceptCheckpointV0351Button_Click(object sender, RoutedEventArgs e)
    {
        var id = $"accept-v0.35.1-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            if (_lastAcceptanceReceipt is null || !_lastAcceptanceReceipt.Passed ||
                !string.Equals(_lastAcceptanceReceipt.Version, LocalCheckpointV0351Service.Version, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(_lastAcceptanceArtifactPath))
                throw new InvalidDataException("Run a passing v0.35.1 Self-test in this Workbench process before accepting the stabilization checkpoint.");
            if (_lastAcceptanceConsumed)
                throw new InvalidDataException("The latest v0.35.1 Self-test receipt has already been consumed by local checkpoint acceptance.");

            SaveSettings();
            var candidate = await _checkpointV0351Service.PreviewAsync(
                WorkspaceRootBox.Text,
                _lastAcceptanceArtifactPath,
                _lastAcceptanceReceipt,
                CancellationToken.None);

            var preview = new StringBuilder();
            preview.AppendLine("Создать локальный accepted checkpoint Workbench v0.35.1 stabilization?");
            preview.AppendLine();
            preview.AppendLine($"Predecessor: {candidate.PreviousHead} / {candidate.ExpectedPredecessorTag}");
            preview.AppendLine($"Target tag: {candidate.TargetTag}");
            preview.AppendLine($"Acceptance SHA-256: {candidate.AcceptanceArtifactSha256}");
            preview.AppendLine($"Build-source manifest SHA-256: {candidate.BuildSourceManifestSha256}");
            preview.AppendLine();
            preview.AppendLine("Это только локальный stabilization commit/tag. Publish accepted и Lifecycle receipt v2 остаются отдельными решениями.");

            if (MessageBox.Show(this, preview.ToString(), "Принять Workbench v0.35.1", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            SetV035PrimaryControlsEnabled(false);
            BeginRun(id);
            StatusText.Text = "RUNNING: explicit local Workbench v0.35.1 stabilization checkpoint";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  checkpoint.requested        tag={candidate.TargetTag}; publication=false; lifecycle=false");

            var receipt = await _checkpointV0351Service.AcceptAsync(candidate, _cts!.Token);
            var receiptPath = await LocalCheckpointV0351Service.WriteReceiptAsync(
                WorkspaceRootBox.Text, receipt, _cts.Token);
            _lastAcceptanceConsumed = true;

            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Acceptance = _lastAcceptanceReceipt,
                AcceptanceArtifactPath = _lastAcceptanceArtifactPath,
                Checkpoint = receipt,
                CheckpointReceiptPath = receiptPath,
                NextExplicitActions = new[] { "Publish accepted", "Lifecycle receipt" }
            });
            OutputTabs.SelectedItem = AcceptanceTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: {receipt.Tag} -> {receipt.NewHead}; publication/lifecycle remain separate";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  checkpoint.completed        {receipt.Tag} -> {receipt.NewHead}; remotePush=false; lifecycle=false");
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally
        {
            EndRun();
            SetV035PrimaryControlsEnabled(true);
            AcceptCheckpointButton.IsEnabled = false;
        }
    }

    private async void PublishAcceptedV0351Button_Click(object sender, RoutedEventArgs e)
    {
        var id = $"publish-accepted-v0.35.1-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            SaveSettings();
            var candidate = await _fixedGitHubPublicationV0351Service.PreviewAsync(
                WorkspaceRootBox.Text, CancellationToken.None);

            var preview = new StringBuilder();
            preview.AppendLine("Опубликовать принятый Workbench v0.35.1 stabilization?");
            preview.AppendLine();
            preview.AppendLine($"Remote: {candidate.RemoteName}");
            preview.AppendLine($"URL: {candidate.RemoteUrl}");
            preview.AppendLine($"Accepted HEAD: {candidate.Head}");
            preview.AppendLine($"Parent: {candidate.Parent}");
            preview.AppendLine($"Tag: {candidate.AcceptedTag}");
            preview.AppendLine();
            preview.AppendLine("Разрешён только exact fast-forward/tag к фиксированному Workbench repository. Lifecycle v2 запускается отдельно после publication.");

            if (MessageBox.Show(this, preview.ToString(), "Publish accepted v0.35.1", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            SetV035PrimaryControlsEnabled(false);
            BeginRun(id);
            StatusText.Text = "RUNNING: fixed v0.35.1 accepted-source GitHub publication";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  publication.requested       head={candidate.Head}; force=false; lifecycle=false");

            var receipt = await _fixedGitHubPublicationV0351Service.PublishAsync(candidate, _cts!.Token);
            var receiptPath = await FixedGitHubPublicationV0351Service.WriteReceiptAsync(
                WorkspaceRootBox.Text, receipt, _cts.Token);

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
            StatusText.Text = $"COMPLETED: remote main/tag == {receipt.LocalHead}; run Lifecycle receipt separately";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  publication.completed       main={receipt.RemoteMainAfter}; tag={receipt.RemoteTagAfter}; lifecycle=false");
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally
        {
            EndRun();
            SetV035PrimaryControlsEnabled(true);
        }
    }

    private async void LifecycleReceiptV2Button_Click(object sender, RoutedEventArgs e)
    {
        var id = $"lifecycle-v2-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            SaveSettings();
            var assessment = await _maintenanceLifecycleReceiptV2Service.AssessAsync(
                WorkspaceRootBox.Text, CancellationToken.None);

            var token = MaintenanceLifecycleReceiptV2Service.NormalizeSemanticVersionForAcceptedToken(assessment.TargetVersion);
            var preview = new StringBuilder();
            preview.AppendLine($"Записать Maintenance Lifecycle Receipt v2 для Workbench {assessment.TargetVersion}?");
            preview.AppendLine();
            preview.AppendLine($"Accepted tag/schema token: {token}");
            preview.AppendLine($"Predecessor: {assessment.PredecessorCommit} / {assessment.PredecessorTag}");
            preview.AppendLine($"Accepted: {assessment.AcceptedCommit} / {assessment.TargetTag}");
            preview.AppendLine($"Candidate executable SHA-256: {assessment.CandidateExecutableSha256}");
            preview.AppendLine($"Checks: {assessment.Checks.Count}; Complete={assessment.Complete}");
            preview.AppendLine();
            preview.AppendLine("Tag/schema token и semantic Version проверены отдельно. Запись receipt не повторяет и не авторизует lifecycle actions.");

            if (MessageBox.Show(this, preview.ToString(), "Lifecycle receipt v2", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes)
                return;

            SetV035PrimaryControlsEnabled(false);
            BeginRun(id);
            StatusText.Text = $"RUNNING: local lifecycle v2 evidence write for {assessment.TargetVersion}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  lifecycle-v2.write.requested target={assessment.TargetVersion}; token={token}; complete={assessment.Complete}; authority=false");

            var receiptPath = await MaintenanceLifecycleReceiptV2Service.WriteReceiptAsync(
                WorkspaceRootBox.Text, assessment, _cts!.Token);
            LifecycleTextBox.Text = CommandCodec.Serialize(new
            {
                Assessment = assessment,
                LifecycleReceiptPath = receiptPath,
                Adapter = "v2",
                AcceptedTagSchemaToken = token,
                SemanticVersion = assessment.TargetVersion,
                AuthorityCreated = false,
                ActionPerformed = false
            });
            OutputTabs.SelectedItem = LifecycleTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: lifecycle v2 evidence bound for {assessment.TargetVersion}; {receiptPath}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  lifecycle-v2.write.completed target={assessment.TargetVersion}; token={token}; complete=true; authority=false");
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally
        {
            EndRun();
            SetV035PrimaryControlsEnabled(true);
        }
    }
}
