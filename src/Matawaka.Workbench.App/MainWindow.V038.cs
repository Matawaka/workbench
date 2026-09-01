using System.IO;
using System.Text;
using System.Windows;
using Matawaka.Workbench.Protocol;
using Matawaka.Workbench.Runtime;
using Microsoft.Win32;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalApplicationPackageBuildReceiptStoreV038Service _packageBuildReceiptStoreV038Service = new();
    private readonly LocalCheckpointV038Service _checkpointV038Service = new();
    private readonly FixedGitHubPublicationV038Service _fixedGitHubPublicationV038Service = new();

    private async void SelfTestV038Button_Click(object sender, RoutedEventArgs e)
    {
        var id = $"self-test-v0.38-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            SaveSettings();
            SetV035PrimaryControlsEnabled(false);
            BeginRun(id);
            StatusText.Text = "RUNNING: v0.38 acceptance + Local Apps chooser/receipt persistence checks";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  acceptance.started           v0.38; chooserEffect=false; packageEffect=false");
            var context = new RuntimeContext(CatalogRootBox.Text, true, false);
            var receipt = await new WorkbenchV038AcceptanceHarness(_acceptanceHarness).RunAsync(context, _cts!.Token);
            var dir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"v0.38-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            await File.WriteAllTextAsync(path, CommandCodec.Serialize(receipt), new UTF8Encoding(false), _cts.Token);
            _lastAcceptanceReceipt = receipt;
            _lastAcceptanceArtifactPath = path;
            _lastAcceptanceConsumed = false;
            AcceptCheckpointButton.IsEnabled = receipt.Passed;
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Receipt = receipt,
                ArtifactPath = path,
                LocalAppsChooserEffectPerformed = false,
                PackageBuildEffectPerformed = false,
                LocalAppUpdatePerformed = false,
                LocalCheckpointAvailable = receipt.Passed
            });
            OutputTabs.SelectedItem = AcceptanceTab;
            ProgressBar.Value = 100;
            _currentTerminalState = receipt.Passed ? CommandTerminalState.Completed : CommandTerminalState.Failed;
            StatusText.Text = receipt.Passed ? $"COMPLETED: v0.38 Self-test PASSED; {path}" : "FAILED: v0.38 acceptance matrix has failing checks";
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally
        {
            EndRun();
            SetV035PrimaryControlsEnabled(true);
            AcceptCheckpointButton.IsEnabled = _lastAcceptanceReceipt?.Passed == true && !_lastAcceptanceConsumed &&
                                               _lastAcceptanceReceipt.Version == LocalCheckpointV038Service.Version;
        }
    }

    private async void AcceptCheckpointV038Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_lastAcceptanceReceipt is null || !_lastAcceptanceReceipt.Passed ||
                _lastAcceptanceReceipt.Version != LocalCheckpointV038Service.Version || string.IsNullOrWhiteSpace(_lastAcceptanceArtifactPath))
                throw new InvalidDataException("Run a passing v0.38 Self-test before accepting the checkpoint.");
            if (_lastAcceptanceConsumed) throw new InvalidDataException("The latest v0.38 Self-test receipt has already been consumed.");
            SaveSettings();
            var candidate = await _checkpointV038Service.PreviewAsync(
                WorkspaceRootBox.Text, _lastAcceptanceArtifactPath, _lastAcceptanceReceipt, CancellationToken.None);
            var preview = $"Создать локальный accepted checkpoint Workbench v0.38?\n\nPredecessor: {candidate.PreviousHead} / {candidate.ExpectedPredecessorTag}\nTarget tag: {candidate.TargetTag}\nAcceptance SHA-256: {candidate.AcceptanceArtifactSha256}\n\nЭто только local commit/tag. Local Apps, package build/update, Publish и Lifecycle остаются отдельными решениями.";
            if (MessageBox.Show(this, preview, "Принять Workbench v0.38", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            SetV035PrimaryControlsEnabled(false);
            BeginRun($"accept-v0.38-{DateTime.Now:yyyyMMddHHmmss}");
            var receipt = await _checkpointV038Service.AcceptAsync(candidate, _cts!.Token);
            var path = await LocalCheckpointV038Service.WriteReceiptAsync(WorkspaceRootBox.Text, receipt, _cts.Token);
            _lastAcceptanceConsumed = true;
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Acceptance = _lastAcceptanceReceipt,
                AcceptanceArtifactPath = _lastAcceptanceArtifactPath,
                Checkpoint = receipt,
                CheckpointReceiptPath = path,
                NextExplicitActions = new[] { "Publish accepted", "Lifecycle receipt" }
            });
            OutputTabs.SelectedItem = AcceptanceTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: {receipt.Tag} -> {receipt.NewHead}";
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally { EndRun(); SetV035PrimaryControlsEnabled(true); AcceptCheckpointButton.IsEnabled = false; }
    }

    private async void PublishAcceptedV038Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveSettings();
            var candidate = await _fixedGitHubPublicationV038Service.PreviewAsync(WorkspaceRootBox.Text, CancellationToken.None);
            var preview = $"Опубликовать принятый Workbench v0.38?\n\nRemote: {candidate.RemoteName}\nURL: {candidate.RemoteUrl}\nAccepted HEAD: {candidate.Head}\nParent: {candidate.Parent}\nTag: {candidate.AcceptedTag}\n\nТолько exact fast-forward/tag; Local Apps/package-build/update authority не создаётся.";
            if (MessageBox.Show(this, preview, "Publish accepted v0.38", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            SetV035PrimaryControlsEnabled(false);
            BeginRun($"publish-v0.38-{DateTime.Now:yyyyMMddHHmmss}");
            var receipt = await _fixedGitHubPublicationV038Service.PublishAsync(candidate, _cts!.Token);
            var path = await FixedGitHubPublicationV038Service.WriteReceiptAsync(WorkspaceRootBox.Text, receipt, _cts.Token);
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Publication = receipt,
                PublicationReceiptPath = path,
                LocalAppsAuthorityCreated = false,
                PackageBuilderAuthorityCreated = false,
                LocalAppUpdateAuthorityCreated = false,
                NextExplicitAction = "Lifecycle receipt"
            });
            OutputTabs.SelectedItem = AcceptanceTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: remote main/tag == {receipt.LocalHead}";
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally { EndRun(); SetV035PrimaryControlsEnabled(true); }
    }

    private async void LocalAppsV038Button_Click(object sender, RoutedEventArgs e)
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
            try
            {
                await _localApplicationManagedRoleGuardV0371Service.EnsureRegistrationRoleAllowedAsync(
                    selectedRoot, WorkspaceRootBox.Text, CancellationToken.None);
            }
            catch (InvalidDataException ex)
            {
                ShowInvalid(ex);
                return;
            }
            await RegisterSelectedLocalAppAsync(selectedRoot);
            return;
        }

        var appId = Path.GetFileName(selectedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var choice = LocalAppsActionDialogV038.ShowChoice(this, appId);
        switch (choice)
        {
            case LocalAppsActionChoiceV038.UpdateFromPackage:
                await UpdateSelectedLocalAppAsync(selectedRoot);
                break;
            case LocalAppsActionChoiceV038.BuildUpdatePackage:
                await BuildLocalAppPackageV038Async(selectedRoot);
                break;
            case LocalAppsActionChoiceV038.Cancel:
            default:
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  local-app.choice.cancelled app={appId}; effect=false");
                break;
        }
    }

    private async Task BuildLocalAppPackageV038Async(string selectedRoot)
    {
        var id = $"local-app-package-build-v038-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            var plan = await _localApplicationPackageBuilderService.PreviewAsync(
                selectedRoot, WorkspaceRootBox.Text, CancellationToken.None);
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
            foreach (var change in plan.Changes.Take(24)) preview.AppendLine($"  {change.Action,-7} {change.Path}");
            if (plan.Changes.Count > 24) preview.AppendLine($"  ... +{plan.Changes.Count - 24} files");
            preview.AppendLine();
            preview.AppendLine("Будут записаны ZIP и отдельный builder receipt JSON только в Workbench/artifacts/local-app-packages. Apps и AppCandidates не изменяются. Update authority не создаётся.");
            if (MessageBox.Show(this, preview.ToString(), "Build local app update package", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            SetV035PrimaryControlsEnabled(false);
            BeginRun(id);
            StatusText.Text = $"RUNNING: build local-app update package {plan.ApplicationId} {plan.CurrentVersion} -> {plan.TargetVersion}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  local-app.package.requested app={plan.ApplicationId}; target={plan.TargetVersion}; update=false; launch=false");
            var result = await _localApplicationPackageBuilderService.BuildAsync(plan, WorkspaceRootBox.Text, _cts!.Token);
            var receiptPath = await _packageBuildReceiptStoreV038Service.WriteAsync(
                WorkspaceRootBox.Text, result.Receipt, _cts.Token);

            LocalAppsTextBox.Text = CommandCodec.Serialize(new
            {
                BuilderPreview = plan,
                PackageBuild = result.Receipt,
                PackageBuildReceiptPath = receiptPath,
                ExistingUpdaterPreview = result.UpdaterPreview,
                UpdateAuthorityCreated = false,
                ApplicationMutationPerformed = false,
                ApplicationLaunchPerformed = false,
                NextAction = "Use Local apps -> Update from package and select the generated ZIP"
            });
            OutputTabs.SelectedItem = LocalAppsTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: package + receipt persisted; updater Preview READY; {result.Receipt.PackagePath}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  local-app.package.completed app={plan.ApplicationId}; package={result.Receipt.PackageSha256}; receipt={receiptPath}; update=false; launch=false");
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally { EndRun(); SetV035PrimaryControlsEnabled(true); }
    }
}
