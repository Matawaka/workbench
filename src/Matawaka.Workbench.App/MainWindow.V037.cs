using System.IO;
using System.Text;
using System.Windows;
using Matawaka.Workbench.Protocol;
using Matawaka.Workbench.Runtime;
using Microsoft.Win32;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalApplicationPackageBuilderService _localApplicationPackageBuilderService = new();
    private readonly LocalCheckpointV037Service _checkpointV037Service = new();
    private readonly FixedGitHubPublicationV037Service _fixedGitHubPublicationV037Service = new();

    private async void SelfTestV037Button_Click(object sender, RoutedEventArgs e)
    {
        var id = $"self-test-v0.37-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            SaveSettings();
            SetV035PrimaryControlsEnabled(false);
            BeginRun(id);
            StatusText.Text = "RUNNING: v0.37 acceptance + local-app package-builder offline checks";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  acceptance.started           v0.37; builderEffect=false; updateEffect=false");

            var context = new RuntimeContext(CatalogRootBox.Text, true, false);
            var harness = new WorkbenchV037AcceptanceHarness(_acceptanceHarness);
            var receipt = await harness.RunAsync(context, _cts!.Token);

            var artifactDir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
            Directory.CreateDirectory(artifactDir);
            var artifactPath = Path.Combine(artifactDir, $"v0.37-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            await File.WriteAllTextAsync(artifactPath, CommandCodec.Serialize(receipt), new UTF8Encoding(false), _cts.Token);

            _lastAcceptanceReceipt = receipt;
            _lastAcceptanceArtifactPath = artifactPath;
            _lastAcceptanceConsumed = false;
            AcceptCheckpointButton.IsEnabled = receipt.Passed;
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Receipt = receipt,
                ArtifactPath = artifactPath,
                PackageBuilderEffectPerformed = false,
                LocalAppUpdatePerformed = false,
                LocalCheckpointAvailable = receipt.Passed
            });
            OutputTabs.SelectedItem = AcceptanceTab;
            ProgressBar.Value = 100;
            _currentTerminalState = receipt.Passed ? CommandTerminalState.Completed : CommandTerminalState.Failed;
            StatusText.Text = receipt.Passed ? $"COMPLETED: v0.37 Self-test PASSED; {artifactPath}" : $"FAILED: v0.37 acceptance matrix has failing checks; {artifactPath}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  acceptance.{(receipt.Passed ? "completed" : "failed"),-18} passed={receipt.Passed}; builderEffect=false");
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally
        {
            EndRun();
            SetV035PrimaryControlsEnabled(true);
            AcceptCheckpointButton.IsEnabled = _lastAcceptanceReceipt?.Passed == true && !_lastAcceptanceConsumed &&
                                               string.Equals(_lastAcceptanceReceipt.Version, LocalCheckpointV037Service.Version, StringComparison.Ordinal);
        }
    }

    private async void AcceptCheckpointV037Button_Click(object sender, RoutedEventArgs e)
    {
        var id = $"accept-v0.37-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            if (_lastAcceptanceReceipt is null || !_lastAcceptanceReceipt.Passed ||
                !string.Equals(_lastAcceptanceReceipt.Version, LocalCheckpointV037Service.Version, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(_lastAcceptanceArtifactPath))
                throw new InvalidDataException("Run a passing v0.37 Self-test in this Workbench process before accepting the checkpoint.");
            if (_lastAcceptanceConsumed) throw new InvalidDataException("The latest v0.37 Self-test receipt has already been consumed.");

            SaveSettings();
            var candidate = await _checkpointV037Service.PreviewAsync(
                WorkspaceRootBox.Text, _lastAcceptanceArtifactPath, _lastAcceptanceReceipt, CancellationToken.None);

            var preview = new StringBuilder();
            preview.AppendLine("Создать локальный accepted checkpoint Workbench v0.37?");
            preview.AppendLine();
            preview.AppendLine($"Predecessor: {candidate.PreviousHead} / {candidate.ExpectedPredecessorTag}");
            preview.AppendLine($"Target tag: {candidate.TargetTag}");
            preview.AppendLine($"Acceptance SHA-256: {candidate.AcceptanceArtifactSha256}");
            preview.AppendLine($"Build-source manifest SHA-256: {candidate.BuildSourceManifestSha256}");
            preview.AppendLine();
            preview.AppendLine("Package Builder, Local app Update, Publish accepted и Lifecycle receipt остаются отдельными действиями.");
            if (MessageBox.Show(this, preview.ToString(), "Принять Workbench v0.37", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            SetV035PrimaryControlsEnabled(false);
            BeginRun(id);
            StatusText.Text = "RUNNING: explicit local Workbench v0.37 checkpoint";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  checkpoint.requested        tag={candidate.TargetTag}; publication=false; localApp=false");
            var receipt = await _checkpointV037Service.AcceptAsync(candidate, _cts!.Token);
            var receiptPath = await LocalCheckpointV037Service.WriteReceiptAsync(WorkspaceRootBox.Text, receipt, _cts.Token);
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

    private async void PublishAcceptedV037Button_Click(object sender, RoutedEventArgs e)
    {
        var id = $"publish-accepted-v0.37-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            SaveSettings();
            var candidate = await _fixedGitHubPublicationV037Service.PreviewAsync(WorkspaceRootBox.Text, CancellationToken.None);
            var preview = new StringBuilder();
            preview.AppendLine("Опубликовать принятый Workbench v0.37?");
            preview.AppendLine();
            preview.AppendLine($"Remote: {candidate.RemoteName}");
            preview.AppendLine($"URL: {candidate.RemoteUrl}");
            preview.AppendLine($"Accepted HEAD: {candidate.Head}");
            preview.AppendLine($"Parent: {candidate.Parent}");
            preview.AppendLine($"Tag: {candidate.AcceptedTag}");
            preview.AppendLine();
            preview.AppendLine("Разрешён только exact fast-forward/tag. Local-app package-build/update authority этим действием не создаётся.");
            if (MessageBox.Show(this, preview.ToString(), "Publish accepted v0.37", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            SetV035PrimaryControlsEnabled(false);
            BeginRun(id);
            StatusText.Text = "RUNNING: fixed v0.37 accepted-source GitHub publication";
            var receipt = await _fixedGitHubPublicationV037Service.PublishAsync(candidate, _cts!.Token);
            var receiptPath = await FixedGitHubPublicationV037Service.WriteReceiptAsync(WorkspaceRootBox.Text, receipt, _cts.Token);
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Publication = receipt,
                PublicationReceiptPath = receiptPath,
                LifecycleReceiptAutomatic = false,
                PackageBuilderAuthorityCreated = false,
                LocalAppUpdateAuthorityCreated = false,
                NextExplicitAction = "Lifecycle receipt"
            });
            OutputTabs.SelectedItem = AcceptanceTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: remote main/tag == {receipt.LocalHead}; run Lifecycle receipt separately";
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

    private async void LocalAppsV037Button_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings();
        var workspace = Path.GetFullPath(WorkspaceRootBox.Text.Trim());
        var appsRoot = Path.Combine(workspace, LocalApplicationRegistrationService.AppsDirectoryName);
        if (!Directory.Exists(appsRoot))
        {
            MessageBox.Show(this, $"Managed Apps root отсутствует:\n{appsRoot}", "Local apps", MessageBoxButton.OK, MessageBoxImage.Information);
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

        var choice = MessageBox.Show(
            this,
            "Приложение зарегистрировано.\n\nYES — Update from package\nNO — Build update package from Workspace\\AppCandidates\\<ApplicationId>\nCANCEL — ничего не делать\n\nBuild package не обновляет и не запускает приложение.",
            "Local apps",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
        if (choice == MessageBoxResult.Yes)
        {
            await UpdateSelectedLocalAppAsync(selectedRoot);
            return;
        }
        if (choice == MessageBoxResult.No)
            await BuildLocalAppPackageAsync(selectedRoot);
    }

    private async Task BuildLocalAppPackageAsync(string selectedRoot)
    {
        var id = $"local-app-package-build-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            var plan = await _localApplicationPackageBuilderService.PreviewAsync(selectedRoot, WorkspaceRootBox.Text, CancellationToken.None);
            var effectChanges = plan.Changes.Where(change => change.Action != "NoOp").ToArray();
            var preview = new StringBuilder();
            preview.AppendLine("Собрать локальный update package для зарегистрированного приложения?");
            preview.AppendLine();
            preview.AppendLine($"Application: {plan.ApplicationId}");
            preview.AppendLine($"Current root: {plan.ApplicationRoot}");
            preview.AppendLine($"Candidate root: {plan.CandidateRoot}");
            preview.AppendLine($"Version: {plan.CurrentVersion} -> {plan.TargetVersion}");
            preview.AppendLine($"Current identity SHA-256: {plan.CurrentIdentitySha256}");
            preview.AppendLine($"Generated manifest SHA-256: {plan.GeneratedManifestSha256}");
            preview.AppendLine($"Changes: {effectChanges.Length}; NoOp={plan.Changes.Count(x => x.Action == "NoOp")}");
            foreach (var change in plan.Changes.Take(24))
                preview.AppendLine($"  {change.Action,-7} {change.Path}");
            if (plan.Changes.Count > 24) preview.AppendLine($"  ... +{plan.Changes.Count - 24} files");
            preview.AppendLine();
            preview.AppendLine("Будет записан только ZIP в Workbench/artifacts/local-app-packages. Apps и AppCandidates не изменяются. После записи ZIP обязан пройти существующий updater Preview; Update authority не создаётся.");
            if (MessageBox.Show(this, preview.ToString(), "Build local app update package", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            SetV035PrimaryControlsEnabled(false);
            BeginRun(id);
            StatusText.Text = $"RUNNING: build local-app update package {plan.ApplicationId} {plan.CurrentVersion} -> {plan.TargetVersion}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  local-app.package.requested app={plan.ApplicationId}; target={plan.TargetVersion}; update=false; launch=false");
            var result = await _localApplicationPackageBuilderService.BuildAsync(plan, WorkspaceRootBox.Text, _cts!.Token);
            LocalAppsTextBox.Text = CommandCodec.Serialize(new
            {
                BuilderPreview = plan,
                PackageBuild = result.Receipt,
                ExistingUpdaterPreview = result.UpdaterPreview,
                UpdateAuthorityCreated = false,
                ApplicationMutationPerformed = false,
                ApplicationLaunchPerformed = false,
                NextAction = "Use Local apps -> Update from package and select the generated ZIP"
            });
            OutputTabs.SelectedItem = LocalAppsTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: package built and updater Preview READY; {result.Receipt.PackagePath}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  local-app.package.completed app={plan.ApplicationId}; package={result.Receipt.PackageSha256}; update=false; launch=false");
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
