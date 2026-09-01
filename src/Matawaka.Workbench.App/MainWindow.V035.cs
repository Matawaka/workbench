using System.IO;
using System.Text;
using System.Windows;
using Matawaka.Workbench.Protocol;
using Matawaka.Workbench.Runtime;
using Microsoft.Win32;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalCheckpointV035Service _checkpointV035Service = new();
    private readonly FixedGitHubPublicationV035Service _fixedGitHubPublicationV035Service = new();
    private readonly LocalApplicationMaintenanceService _localApplicationMaintenanceService = new();

    private async void SelfTestV035Button_Click(object sender, RoutedEventArgs e)
    {
        var id = $"self-test-v0.35-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            // In v0.35 the explicit Self-test click is itself the human test authority.
            // The removed persistent Agent-enabled checkbox no longer acts as a second UI gate.
            // This only enables the existing bounded read-only acceptance matrix, never Agent Execute.
            SaveSettings();
            SetV035PrimaryControlsEnabled(false);
            BeginRun(id);
            StatusText.Text = "RUNNING: v0.35 acceptance + local-app maintenance offline checks";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  acceptance.started           v0.35 matrix; localAppEffect=false; agentExecute=false");

            var context = new RuntimeContext(CatalogRootBox.Text, true, false);
            var harness = new WorkbenchV035AcceptanceHarness(_acceptanceHarness);
            var receipt = await harness.RunAsync(context, _cts!.Token);

            var artifactDir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
            Directory.CreateDirectory(artifactDir);
            var artifactPath = Path.Combine(artifactDir, $"v0.35-{DateTime.Now:yyyyMMdd-HHmmss}.json");
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
                LocalApplicationUpdatePerformed = false,
                PersistentAgentCheckboxRequired = false,
                LocalCheckpointAvailable = receipt.Passed
            });
            OutputTabs.SelectedItem = AcceptanceTab;
            ProgressBar.Value = 100;
            _currentTerminalState = receipt.Passed ? CommandTerminalState.Completed : CommandTerminalState.Failed;
            StatusText.Text = receipt.Passed
                ? $"COMPLETED: v0.35 Self-test PASSED; {artifactPath}"
                : $"FAILED: v0.35 acceptance matrix has failing checks; {artifactPath}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  acceptance.{(receipt.Passed ? "completed" : "failed"),-18} passed={receipt.Passed}; localAppEffect=false");
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
                                               string.Equals(_lastAcceptanceReceipt.Version, LocalCheckpointV035Service.Version, StringComparison.Ordinal);
        }
    }

    private async void AcceptCheckpointV035Button_Click(object sender, RoutedEventArgs e)
    {
        var id = $"accept-v0.35-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            if (_lastAcceptanceReceipt is null || !_lastAcceptanceReceipt.Passed ||
                !string.Equals(_lastAcceptanceReceipt.Version, LocalCheckpointV035Service.Version, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(_lastAcceptanceArtifactPath))
                throw new InvalidDataException("Run a passing v0.35 Self-test in this Workbench process before accepting the checkpoint.");
            if (_lastAcceptanceConsumed)
                throw new InvalidDataException("The latest v0.35 Self-test receipt has already been consumed by local checkpoint acceptance.");

            SaveSettings();
            var candidate = await _checkpointV035Service.PreviewAsync(
                WorkspaceRootBox.Text,
                _lastAcceptanceArtifactPath,
                _lastAcceptanceReceipt,
                CancellationToken.None);

            var preview = new StringBuilder();
            preview.AppendLine("Создать локальный accepted checkpoint Workbench v0.35?");
            preview.AppendLine();
            preview.AppendLine($"Predecessor: {candidate.PreviousHead} / {candidate.ExpectedPredecessorTag}");
            preview.AppendLine($"Target tag: {candidate.TargetTag}");
            preview.AppendLine($"Acceptance SHA-256: {candidate.AcceptanceArtifactSha256}");
            preview.AppendLine($"Build-source manifest SHA-256: {candidate.BuildSourceManifestSha256}");
            preview.AppendLine();
            preview.AppendLine("Это только локальный Workbench commit/tag. Publish accepted, Lifecycle receipt и Update local app остаются отдельными authority decisions.");

            if (MessageBox.Show(this, preview.ToString(), "Принять Workbench v0.35", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            SetV035PrimaryControlsEnabled(false);
            BeginRun(id);
            StatusText.Text = "RUNNING: explicit local Workbench v0.35 checkpoint";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  checkpoint.requested        tag={candidate.TargetTag}; publication=false; localApp=false");

            var receipt = await _checkpointV035Service.AcceptAsync(candidate, _cts!.Token);
            var receiptPath = await LocalCheckpointV035Service.WriteReceiptAsync(
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
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  checkpoint.completed        {receipt.Tag} -> {receipt.NewHead}; remotePush=false; localApp=false");
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

    private async void PublishAcceptedV035Button_Click(object sender, RoutedEventArgs e)
    {
        var id = $"publish-accepted-v0.35-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            SaveSettings();
            var candidate = await _fixedGitHubPublicationV035Service.PreviewAsync(
                WorkspaceRootBox.Text, CancellationToken.None);

            var preview = new StringBuilder();
            preview.AppendLine("Опубликовать принятый Workbench v0.35?");
            preview.AppendLine();
            preview.AppendLine($"Remote: {candidate.RemoteName}");
            preview.AppendLine($"URL: {candidate.RemoteUrl}");
            preview.AppendLine($"Accepted HEAD: {candidate.Head}");
            preview.AppendLine($"Parent: {candidate.Parent}");
            preview.AppendLine($"Tag: {candidate.AcceptedTag}");
            preview.AppendLine();
            preview.AppendLine("Разрешён только exact fast-forward/tag к фиксированному Workbench repository. Local-app update authority и Lifecycle authority этим действием не создаются.");

            if (MessageBox.Show(this, preview.ToString(), "Publish accepted v0.35", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            SetV035PrimaryControlsEnabled(false);
            BeginRun(id);
            StatusText.Text = "RUNNING: fixed v0.35 accepted-source GitHub publication";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  publication.requested       head={candidate.Head}; force=false; localApp=false");

            var receipt = await _fixedGitHubPublicationV035Service.PublishAsync(candidate, _cts!.Token);
            var receiptPath = await FixedGitHubPublicationV035Service.WriteReceiptAsync(
                WorkspaceRootBox.Text, receipt, _cts.Token);

            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Publication = receipt,
                PublicationReceiptPath = receiptPath,
                LifecycleReceiptAutomatic = false,
                LocalApplicationUpdateAuthorityCreated = false,
                NextExplicitAction = "Lifecycle receipt"
            });
            OutputTabs.SelectedItem = AcceptanceTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: remote main/tag == {receipt.LocalHead}; run Lifecycle receipt separately";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  publication.completed       main={receipt.RemoteMainAfter}; tag={receipt.RemoteTagAfter}; localApp=false");
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

    private async void UpdateLocalAppButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Matawaka local app update (*.zip)|*.zip|Все файлы (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
            Title = "Выберите локальный update package приложения"
        };
        if (dialog.ShowDialog(this) != true) return;

        var id = $"local-app-update-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            SaveSettings();
            var plan = await _localApplicationMaintenanceService.PreviewAsync(
                dialog.FileName,
                WorkspaceRootBox.Text,
                CancellationToken.None);

            var preview = new StringBuilder();
            preview.AppendLine("Обновить зарегистрированное локальное приложение?");
            preview.AppendLine();
            preview.AppendLine($"Application: {plan.ApplicationId}");
            preview.AppendLine($"Root: {plan.ApplicationRoot}");
            preview.AppendLine($"Version: {plan.CurrentVersion} -> {plan.TargetVersion}");
            preview.AppendLine($"Package SHA-256: {plan.PackageSha256}");
            preview.AppendLine($"Manifest SHA-256: {plan.ManifestSha256}");
            preview.AppendLine($"Files: {plan.Changes.Count} (Add={plan.Changes.Count(x => x.Action == "Add")}, Replace={plan.Changes.Count(x => x.Action == "Replace")})");
            foreach (var change in plan.Changes.Take(20))
                preview.AppendLine($"  {change.Action,-7} {change.Path}");
            if (plan.Changes.Count > 20) preview.AppendLine($"  ... +{plan.Changes.Count - 20} files");
            preview.AppendLine();
            preview.AppendLine("Разрешается только exact Add/Replace внутри <WorkspaceRoot>\\Apps\\<ApplicationId>. Delete, сеть, git, installer/script, registry/service, Agent Execute и автоматический запуск приложения запрещены. При ошибке выполняется rollback predecessor bytes.");

            if (MessageBox.Show(this, preview.ToString(), "Update local app", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            SetV035PrimaryControlsEnabled(false);
            BeginRun(id);
            StatusText.Text = $"RUNNING: bounded local app update {plan.ApplicationId} {plan.CurrentVersion} -> {plan.TargetVersion}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  local-app.update.requested app={plan.ApplicationId}; target={plan.TargetVersion}; network=false; launch=false");

            var result = await _localApplicationMaintenanceService.ApplyAsync(
                plan,
                WorkspaceRootBox.Text,
                _cts!.Token);

            LocalAppsTextBox.Text = CommandCodec.Serialize(new
            {
                Preview = plan,
                Update = result.Receipt,
                UpdateReceiptPath = result.ArtifactPath,
                ApplicationLaunchPerformed = false,
                NextAction = "Launch application manually if desired"
            });
            OutputTabs.SelectedItem = LocalAppsTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: {result.Receipt.ApplicationId} -> {result.Receipt.TargetVersion}; launch=false";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  local-app.update.completed app={result.Receipt.ApplicationId}; version={result.Receipt.TargetVersion}; launch=false; {result.ArtifactPath}");
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

    private void SetV035PrimaryControlsEnabled(bool enabled)
    {
        UpdateCandidateButton.IsEnabled = enabled;
        UpdateLocalAppButton.IsEnabled = enabled;
        SelfTestButton.IsEnabled = enabled;
        PublishAcceptedButton.IsEnabled = enabled;
        LifecycleReceiptButton.IsEnabled = enabled;
        if (!enabled)
            AcceptCheckpointButton.IsEnabled = false;
    }
}
