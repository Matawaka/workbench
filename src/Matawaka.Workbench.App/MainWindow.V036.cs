using System.IO;
using System.Text;
using System.Windows;
using Matawaka.Workbench.Protocol;
using Matawaka.Workbench.Runtime;
using Microsoft.Win32;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalApplicationRegistrationService _localApplicationRegistrationService = new();
    private readonly LocalCheckpointV036Service _checkpointV036Service = new();
    private readonly FixedGitHubPublicationV036Service _fixedGitHubPublicationV036Service = new();

    private async void SelfTestV036Button_Click(object sender, RoutedEventArgs e)
    {
        var id = $"self-test-v0.36-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            SaveSettings();
            SetV035PrimaryControlsEnabled(false);
            BeginRun(id);
            StatusText.Text = "RUNNING: v0.36 acceptance + local-app registration offline checks";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  acceptance.started           v0.36; registrationEffect=false; updateEffect=false");

            var context = new RuntimeContext(CatalogRootBox.Text, true, false);
            var harness = new WorkbenchV036AcceptanceHarness(_acceptanceHarness);
            var receipt = await harness.RunAsync(context, _cts!.Token);

            var artifactDir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
            Directory.CreateDirectory(artifactDir);
            var artifactPath = Path.Combine(artifactDir, $"v0.36-{DateTime.Now:yyyyMMdd-HHmmss}.json");
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
                LocalAppRegistrationPerformed = false,
                LocalAppUpdatePerformed = false,
                LocalCheckpointAvailable = receipt.Passed
            });
            OutputTabs.SelectedItem = AcceptanceTab;
            ProgressBar.Value = 100;
            _currentTerminalState = receipt.Passed ? CommandTerminalState.Completed : CommandTerminalState.Failed;
            StatusText.Text = receipt.Passed
                ? $"COMPLETED: v0.36 Self-test PASSED; {artifactPath}"
                : $"FAILED: v0.36 acceptance matrix has failing checks; {artifactPath}";
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
                                               string.Equals(_lastAcceptanceReceipt.Version, LocalCheckpointV036Service.Version, StringComparison.Ordinal);
        }
    }

    private async void AcceptCheckpointV036Button_Click(object sender, RoutedEventArgs e)
    {
        var id = $"accept-v0.36-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            if (_lastAcceptanceReceipt is null || !_lastAcceptanceReceipt.Passed ||
                !string.Equals(_lastAcceptanceReceipt.Version, LocalCheckpointV036Service.Version, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(_lastAcceptanceArtifactPath))
                throw new InvalidDataException("Run a passing v0.36 Self-test in this Workbench process before accepting the checkpoint.");
            if (_lastAcceptanceConsumed)
                throw new InvalidDataException("The latest v0.36 Self-test receipt has already been consumed by local checkpoint acceptance.");

            SaveSettings();
            var candidate = await _checkpointV036Service.PreviewAsync(
                WorkspaceRootBox.Text,
                _lastAcceptanceArtifactPath,
                _lastAcceptanceReceipt,
                CancellationToken.None);

            var preview = new StringBuilder();
            preview.AppendLine("Создать локальный accepted checkpoint Workbench v0.36?");
            preview.AppendLine();
            preview.AppendLine($"Predecessor: {candidate.PreviousHead} / {candidate.ExpectedPredecessorTag}");
            preview.AppendLine($"Target tag: {candidate.TargetTag}");
            preview.AppendLine($"Acceptance SHA-256: {candidate.AcceptanceArtifactSha256}");
            preview.AppendLine($"Build-source manifest SHA-256: {candidate.BuildSourceManifestSha256}");
            preview.AppendLine();
            preview.AppendLine("Это только локальный Workbench commit/tag. Local app registration/update, Publish accepted и Lifecycle receipt остаются отдельными решениями.");

            if (MessageBox.Show(this, preview.ToString(), "Принять Workbench v0.36", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            SetV035PrimaryControlsEnabled(false);
            BeginRun(id);
            StatusText.Text = "RUNNING: explicit local Workbench v0.36 checkpoint";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  checkpoint.requested        tag={candidate.TargetTag}; publication=false; localApp=false");

            var receipt = await _checkpointV036Service.AcceptAsync(candidate, _cts!.Token);
            var receiptPath = await LocalCheckpointV036Service.WriteReceiptAsync(WorkspaceRootBox.Text, receipt, _cts.Token);
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

    private async void PublishAcceptedV036Button_Click(object sender, RoutedEventArgs e)
    {
        var id = $"publish-accepted-v0.36-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            SaveSettings();
            var candidate = await _fixedGitHubPublicationV036Service.PreviewAsync(WorkspaceRootBox.Text, CancellationToken.None);
            var preview = new StringBuilder();
            preview.AppendLine("Опубликовать принятый Workbench v0.36?");
            preview.AppendLine();
            preview.AppendLine($"Remote: {candidate.RemoteName}");
            preview.AppendLine($"URL: {candidate.RemoteUrl}");
            preview.AppendLine($"Accepted HEAD: {candidate.Head}");
            preview.AppendLine($"Parent: {candidate.Parent}");
            preview.AppendLine($"Tag: {candidate.AcceptedTag}");
            preview.AppendLine();
            preview.AppendLine("Разрешён только exact fast-forward/tag к фиксированному Workbench repository. Local Apps и Lifecycle authority этим действием не создаются.");

            if (MessageBox.Show(this, preview.ToString(), "Publish accepted v0.36", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            SetV035PrimaryControlsEnabled(false);
            BeginRun(id);
            StatusText.Text = "RUNNING: fixed v0.36 accepted-source GitHub publication";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  publication.requested       head={candidate.Head}; force=false; localApp=false");

            var receipt = await _fixedGitHubPublicationV036Service.PublishAsync(candidate, _cts!.Token);
            var receiptPath = await FixedGitHubPublicationV036Service.WriteReceiptAsync(WorkspaceRootBox.Text, receipt, _cts.Token);

            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Publication = receipt,
                PublicationReceiptPath = receiptPath,
                LifecycleReceiptAutomatic = false,
                LocalAppAuthorityCreated = false,
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

    private async void LocalAppsButton_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings();
        var workspace = Path.GetFullPath(WorkspaceRootBox.Text.Trim());
        var appsRoot = Path.Combine(workspace, LocalApplicationRegistrationService.AppsDirectoryName);
        if (!Directory.Exists(appsRoot))
        {
            MessageBox.Show(
                this,
                $"Managed Apps root пока отсутствует:\n{appsRoot}\n\nСоздайте папку Apps и поместите внутрь отдельную папку приложения. Workbench не импортирует и не копирует приложение сам.",
                "Local apps",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var folderDialog = new OpenFolderDialog
        {
            Title = "Выберите приложение внутри Workspace\\Apps",
            InitialDirectory = appsRoot,
            Multiselect = false
        };
        if (folderDialog.ShowDialog(this) != true) return;
        var selectedRoot = Path.GetFullPath(folderDialog.FolderName);
        var identityPath = Path.Combine(selectedRoot, LocalApplicationRegistrationService.IdentityFileName);

        if (!File.Exists(identityPath))
        {
            await RegisterSelectedLocalAppAsync(selectedRoot);
            return;
        }

        await UpdateSelectedLocalAppAsync(selectedRoot);
    }

    private async Task RegisterSelectedLocalAppAsync(string selectedRoot)
    {
        var id = $"local-app-register-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            var plan = await _localApplicationRegistrationService.PreviewAsync(selectedRoot, WorkspaceRootBox.Text, CancellationToken.None);
            var preview = new StringBuilder();
            preview.AppendLine("Зарегистрировать существующее локальное приложение под управлением Workbench?");
            preview.AppendLine();
            preview.AppendLine($"ApplicationId: {plan.ApplicationId}");
            preview.AppendLine($"Root: {plan.ApplicationRoot}");
            preview.AppendLine($"Files: {plan.FileCount}; bytes: {plan.TotalBytes}");
            preview.AppendLine($"Tree SHA-256: {plan.TreeSha256}");
            preview.AppendLine($"Baseline: {plan.ProposedIdentity.Version}");
            preview.AppendLine($"Identity SHA-256: {plan.ProposedIdentitySha256}");
            preview.AppendLine();
            preview.AppendLine("Будет создан только .matawaka-app.json. Остальные файлы приложения не копируются, не перемещаются, не изменяются и приложение не запускается. baseline-* — отпечаток наблюдаемых байтов, а не версия производителя.");

            if (MessageBox.Show(this, preview.ToString(), "Register local app", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            SetV035PrimaryControlsEnabled(false);
            BeginRun(id);
            StatusText.Text = $"RUNNING: register local app {plan.ApplicationId}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  local-app.register.requested app={plan.ApplicationId}; files={plan.FileCount}; copy=false; launch=false");

            var result = await _localApplicationRegistrationService.RegisterAsync(plan, WorkspaceRootBox.Text, _cts!.Token);
            LocalAppsTextBox.Text = CommandCodec.Serialize(new
            {
                Preview = plan,
                Registration = result.Receipt,
                RegistrationReceiptPath = result.ArtifactPath,
                UpdateAuthorityCreated = false,
                ApplicationLaunchPerformed = false,
                NextAction = "Use Local apps again with a matching local update ZIP"
            });
            OutputTabs.SelectedItem = LocalAppsTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: {result.Receipt.ApplicationId} registered as {result.Receipt.Identity.Version}; update/launch remain separate";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  local-app.register.completed app={result.Receipt.ApplicationId}; identity={result.Receipt.Identity.Version}; otherFilesChanged=false");
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

    private async Task UpdateSelectedLocalAppAsync(string selectedRoot)
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
            var plan = await _localApplicationMaintenanceService.PreviewAsync(dialog.FileName, WorkspaceRootBox.Text, CancellationToken.None);
            if (!string.Equals(Path.GetFullPath(plan.ApplicationRoot), Path.GetFullPath(selectedRoot), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Update package targets {plan.ApplicationRoot}, but selected managed app is {selectedRoot}.");

            var preview = new StringBuilder();
            preview.AppendLine("Обновить выбранное зарегистрированное локальное приложение?");
            preview.AppendLine();
            preview.AppendLine($"Application: {plan.ApplicationId}");
            preview.AppendLine($"Root: {plan.ApplicationRoot}");
            preview.AppendLine($"Version: {plan.CurrentVersion} -> {plan.TargetVersion}");
            preview.AppendLine($"Package SHA-256: {plan.PackageSha256}");
            preview.AppendLine($"Files: {plan.Changes.Count} (Add={plan.Changes.Count(x => x.Action == "Add")}, Replace={plan.Changes.Count(x => x.Action == "Replace")})");
            preview.AppendLine();
            preview.AppendLine("Разрешается только exact Add/Replace внутри выбранного managed app. Delete, сеть, Git, installer/script, registry/service, Agent Execute и auto-launch запрещены; failure вызывает rollback.");

            if (MessageBox.Show(this, preview.ToString(), "Update local app", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            SetV035PrimaryControlsEnabled(false);
            BeginRun(id);
            StatusText.Text = $"RUNNING: bounded local app update {plan.ApplicationId} {plan.CurrentVersion} -> {plan.TargetVersion}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  local-app.update.requested app={plan.ApplicationId}; target={plan.TargetVersion}; network=false; launch=false");

            var result = await _localApplicationMaintenanceService.ApplyAsync(plan, WorkspaceRootBox.Text, _cts!.Token);
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
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  local-app.update.completed app={result.Receipt.ApplicationId}; version={result.Receipt.TargetVersion}; launch=false");
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
