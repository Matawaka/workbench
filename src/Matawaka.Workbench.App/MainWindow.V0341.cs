using System.IO;
using System.Text;
using System.Windows;
using Matawaka.Workbench.Protocol;
using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalCheckpointV0341Service _checkpointV0341Service = new();
    private readonly FixedGitHubPublicationV0341Service _fixedGitHubPublicationV0341Service = new();

    private async void SelfTestV0341Button_Click(object sender, RoutedEventArgs e)
    {
        var id = $"self-test-v0.34.1-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            if (AgentEnabledBox.IsChecked != true)
                throw new InvalidDataException("Self-test requires 'Агент включен' to be explicitly enabled.");

            SaveSettings();
            BeginRun(id);
            StatusText.Text = "RUNNING: v0.34.1 qualification/stabilization acceptance";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  acceptance.started           v0.34.1 matrix; lifecycleEffect=false");

            var context = new RuntimeContext(CatalogRootBox.Text, true, false);
            var harness = new WorkbenchV0341AcceptanceHarness(_acceptanceHarness);
            var receipt = await harness.RunAsync(context, _cts!.Token);

            var artifactDir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
            Directory.CreateDirectory(artifactDir);
            var artifactPath = Path.Combine(artifactDir, $"v0.34.1-{DateTime.Now:yyyyMMdd-HHmmss}.json");
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
                QualificationPatch = true,
                GenericLifecycleAssessmentPerformed = false,
                LocalCheckpointAvailable = receipt.Passed
            });
            OutputTabs.SelectedItem = AcceptanceTab;
            ProgressBar.Value = 100;
            _currentTerminalState = receipt.Passed ? CommandTerminalState.Completed : CommandTerminalState.Failed;
            StatusText.Text = receipt.Passed
                ? $"COMPLETED: v0.34.1 Self-test PASSED; {artifactPath}"
                : $"FAILED: v0.34.1 acceptance matrix has failing checks; {artifactPath}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  acceptance.{(receipt.Passed ? "completed" : "failed"),-18} passed={receipt.Passed}; genericLifecycleEffect=false");
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally { EndRun(); }
    }

    private async void AcceptCheckpointV0341Button_Click(object sender, RoutedEventArgs e)
    {
        var id = $"accept-v0.34.1-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            if (_lastAcceptanceReceipt is null || !_lastAcceptanceReceipt.Passed ||
                !string.Equals(_lastAcceptanceReceipt.Version, LocalCheckpointV0341Service.Version, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(_lastAcceptanceArtifactPath))
                throw new InvalidDataException("Run a passing v0.34.1 Self-test in this Workbench process before accepting the stabilization checkpoint.");
            if (_lastAcceptanceConsumed)
                throw new InvalidDataException("The latest v0.34.1 Self-test receipt has already been consumed by local checkpoint acceptance.");

            SaveSettings();
            var candidate = await _checkpointV0341Service.PreviewAsync(
                WorkspaceRootBox.Text,
                _lastAcceptanceArtifactPath,
                _lastAcceptanceReceipt,
                CancellationToken.None);

            var preview = new StringBuilder();
            preview.AppendLine("Создать локальный accepted checkpoint Workbench v0.34.1 qualification/stabilization?");
            preview.AppendLine();
            preview.AppendLine($"Predecessor: {candidate.PreviousHead} / {candidate.ExpectedPredecessorTag}");
            preview.AppendLine($"Target tag: {candidate.TargetTag}");
            preview.AppendLine($"Acceptance SHA-256: {candidate.AcceptanceArtifactSha256}");
            preview.AppendLine($"Build-source manifest SHA-256: {candidate.BuildSourceManifestSha256}");
            preview.AppendLine();
            preview.AppendLine("Это только локальный patch checkpoint. Publish accepted и generic Lifecycle receipt остаются отдельными последующими решениями. Новая feature authority не создаётся.");

            if (MessageBox.Show(this, preview.ToString(), "Принять Workbench v0.34.1", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            BeginRun(id);
            StatusText.Text = "RUNNING: explicit local v0.34.1 stabilization checkpoint";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  checkpoint.requested        tag={candidate.TargetTag}; publication=false; lifecycle=false");

            var receipt = await _checkpointV0341Service.AcceptAsync(candidate, _cts!.Token);
            var receiptPath = await LocalCheckpointV0341Service.WriteReceiptAsync(
                WorkspaceRootBox.Text, receipt, _cts.Token);
            _lastAcceptanceConsumed = true;

            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Acceptance = _lastAcceptanceReceipt,
                AcceptanceArtifactPath = _lastAcceptanceArtifactPath,
                Checkpoint = receipt,
                CheckpointReceiptPath = receiptPath,
                NextExplicitActions = new[] { "Publish accepted", "Lifecycle receipt after publication" }
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
        finally { EndRun(); }
    }

    private async void PublishAcceptedV0341Button_Click(object sender, RoutedEventArgs e)
    {
        var id = $"publish-accepted-v0.34.1-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            SaveSettings();
            var candidate = await _fixedGitHubPublicationV0341Service.PreviewAsync(
                WorkspaceRootBox.Text, CancellationToken.None);

            var preview = new StringBuilder();
            preview.AppendLine("Опубликовать принятый Workbench v0.34.1 qualification/stabilization patch?");
            preview.AppendLine();
            preview.AppendLine($"Remote: {candidate.RemoteName}");
            preview.AppendLine($"URL: {candidate.RemoteUrl}");
            preview.AppendLine($"Accepted HEAD: {candidate.Head}");
            preview.AppendLine($"Parent: {candidate.Parent}");
            preview.AppendLine($"Tag: {candidate.AcceptedTag}");
            preview.AppendLine();
            preview.AppendLine("Разрешён только exact fast-forward/tag к фиксированному repository. Generic Lifecycle receipt запускается отдельно после публикации и не получает authority из publication success.");

            if (MessageBox.Show(this, preview.ToString(), "Publish accepted v0.34.1", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            BeginRun(id);
            StatusText.Text = "RUNNING: fixed v0.34.1 stabilization publication";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  publication.requested       head={candidate.Head}; force=false; lifecycle=false");

            var receipt = await _fixedGitHubPublicationV0341Service.PublishAsync(candidate, _cts!.Token);
            var receiptPath = await FixedGitHubPublicationV0341Service.WriteReceiptAsync(
                WorkspaceRootBox.Text, receipt, _cts.Token);

            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Publication = receipt,
                PublicationReceiptPath = receiptPath,
                QualificationPatch = true,
                LifecycleReceiptAutomatic = false,
                NextExplicitAction = "Lifecycle receipt"
            });
            OutputTabs.SelectedItem = AcceptanceTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: remote main/tag == {receipt.LocalHead}; run generic Lifecycle receipt separately";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  publication.completed       main={receipt.RemoteMainAfter}; tag={receipt.RemoteTagAfter}; lifecycle=false");
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally { EndRun(); }
    }

    private async void LifecycleReceiptGenericButton_Click(object sender, RoutedEventArgs e)
    {
        var id = $"lifecycle-generic-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            SaveSettings();
            var assessment = await _maintenanceLifecycleReceiptService.AssessAsync(
                WorkspaceRootBox.Text, CancellationToken.None);

            var preview = new StringBuilder();
            preview.AppendLine($"Записать Maintenance Lifecycle Receipt для Workbench {assessment.TargetVersion}?");
            preview.AppendLine();
            preview.AppendLine($"Predecessor: {assessment.PredecessorCommit} / {assessment.PredecessorTag}");
            preview.AppendLine($"Accepted: {assessment.AcceptedCommit} / {assessment.TargetTag}");
            preview.AppendLine($"Candidate executable SHA-256: {assessment.CandidateExecutableSha256}");
            preview.AppendLine($"Orchestrator: {assessment.Orchestrator.Sha256}");
            preview.AppendLine($"Acceptance: {assessment.Acceptance.Sha256}");
            preview.AppendLine($"Checkpoint: {assessment.Checkpoint.Sha256}");
            preview.AppendLine($"Publication: {assessment.Publication.Sha256}");
            preview.AppendLine($"Checks: {assessment.Checks.Count}; Complete={assessment.Complete}");
            preview.AppendLine();
            preview.AppendLine("Версия и predecessor получены из exact accepted tag/checkpoint evidence, а не release constants. Это evidence routing, не trust/authority discovery. Запись receipt не повторяет и не авторизует lifecycle actions.");

            if (MessageBox.Show(this, preview.ToString(), "Generic Lifecycle receipt", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes)
                return;

            BeginRun(id);
            StatusText.Text = $"RUNNING: local generic lifecycle evidence write for {assessment.TargetVersion}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  lifecycle.write.requested   target={assessment.TargetVersion}; complete={assessment.Complete}; authority=false");

            var receiptPath = await MaintenanceLifecycleReceiptService.WriteReceiptAsync(
                WorkspaceRootBox.Text, assessment, _cts!.Token);
            LifecycleTextBox.Text = CommandCodec.Serialize(new
            {
                Assessment = assessment,
                LifecycleReceiptPath = receiptPath,
                QualificationOutcomeCandidate = assessment.TargetVersion == "0.34.1" ? "LIFECYCLE_REUSABLE_IF_INDEPENDENT_REMOTE_VERIFICATION_MATCHES" : "GENERIC_LIFECYCLE_COMPLETE",
                AuthorityCreated = false,
                ActionPerformed = false
            });
            OutputTabs.SelectedItem = LifecycleTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: generic lifecycle evidence bound for {assessment.TargetVersion}; {receiptPath}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  lifecycle.write.completed   target={assessment.TargetVersion}; complete=true; authority=false");
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally { EndRun(); }
    }
}
