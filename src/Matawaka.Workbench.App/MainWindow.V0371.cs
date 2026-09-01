using System.IO;
using System.Text;
using System.Windows;
using Matawaka.Workbench.Protocol;
using Matawaka.Workbench.Runtime;
using Microsoft.Win32;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalApplicationManagedRoleGuardV0371Service _localApplicationManagedRoleGuardV0371Service = new();
    private readonly LocalCheckpointV0371Service _checkpointV0371Service = new();
    private readonly FixedGitHubPublicationV0371Service _fixedGitHubPublicationV0371Service = new();

    private async void SelfTestV0371Button_Click(object sender, RoutedEventArgs e)
    {
        var id = $"self-test-v0.37.1-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            SaveSettings(); SetV035PrimaryControlsEnabled(false); BeginRun(id);
            StatusText.Text = "RUNNING: v0.37.1 acceptance + candidate/managed role-separation checks";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  acceptance.started           v0.37.1; roleGuardEffect=false");
            var context = new RuntimeContext(CatalogRootBox.Text, true, false);
            var receipt = await new WorkbenchV0371AcceptanceHarness(_acceptanceHarness).RunAsync(context, _cts!.Token);
            var dir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance"); Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"v0.37.1-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            await File.WriteAllTextAsync(path, CommandCodec.Serialize(receipt), new UTF8Encoding(false), _cts.Token);
            _lastAcceptanceReceipt = receipt; _lastAcceptanceArtifactPath = path; _lastAcceptanceConsumed = false; AcceptCheckpointButton.IsEnabled = receipt.Passed;
            AcceptanceTextBox.Text = CommandCodec.Serialize(new { Receipt = receipt, ArtifactPath = path, CandidateImportPerformed = false, LocalAppEffectPerformed = false, LocalCheckpointAvailable = receipt.Passed });
            OutputTabs.SelectedItem = AcceptanceTab; ProgressBar.Value = 100;
            _currentTerminalState = receipt.Passed ? CommandTerminalState.Completed : CommandTerminalState.Failed;
            StatusText.Text = receipt.Passed ? $"COMPLETED: v0.37.1 Self-test PASSED; {path}" : "FAILED: v0.37.1 acceptance matrix has failing checks";
        }
        catch (OperationCanceledException) { ShowCancelled(); } catch (InvalidDataException ex) { ShowInvalid(ex); } catch (Exception ex) { ShowFailure(ex); }
        finally { EndRun(); SetV035PrimaryControlsEnabled(true); AcceptCheckpointButton.IsEnabled = _lastAcceptanceReceipt?.Passed == true && !_lastAcceptanceConsumed && _lastAcceptanceReceipt.Version == LocalCheckpointV0371Service.Version; }
    }

    private async void AcceptCheckpointV0371Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_lastAcceptanceReceipt is null || !_lastAcceptanceReceipt.Passed || _lastAcceptanceReceipt.Version != LocalCheckpointV0371Service.Version || string.IsNullOrWhiteSpace(_lastAcceptanceArtifactPath))
                throw new InvalidDataException("Run a passing v0.37.1 Self-test before accepting the checkpoint.");
            if (_lastAcceptanceConsumed) throw new InvalidDataException("The latest v0.37.1 Self-test receipt has already been consumed.");
            SaveSettings();
            var candidate = await _checkpointV0371Service.PreviewAsync(WorkspaceRootBox.Text, _lastAcceptanceArtifactPath, _lastAcceptanceReceipt, CancellationToken.None);
            var preview = $"Создать локальный accepted checkpoint Workbench v0.37.1?\n\nPredecessor: {candidate.PreviousHead} / {candidate.ExpectedPredecessorTag}\nTarget tag: {candidate.TargetTag}\nAcceptance SHA-256: {candidate.AcceptanceArtifactSha256}\n\nЭто только local commit/tag. Candidate import, Local Apps, Publish и Lifecycle остаются отдельными решениями.";
            if (MessageBox.Show(this, preview, "Принять Workbench v0.37.1", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            SetV035PrimaryControlsEnabled(false); BeginRun($"accept-v0.37.1-{DateTime.Now:yyyyMMddHHmmss}");
            var receipt = await _checkpointV0371Service.AcceptAsync(candidate, _cts!.Token);
            var path = await LocalCheckpointV0371Service.WriteReceiptAsync(WorkspaceRootBox.Text, receipt, _cts.Token); _lastAcceptanceConsumed = true;
            AcceptanceTextBox.Text = CommandCodec.Serialize(new { Acceptance = _lastAcceptanceReceipt, AcceptanceArtifactPath = _lastAcceptanceArtifactPath, Checkpoint = receipt, CheckpointReceiptPath = path, NextExplicitActions = new[] { "Publish accepted", "Lifecycle receipt" } });
            OutputTabs.SelectedItem = AcceptanceTab; ProgressBar.Value = 100; _currentTerminalState = CommandTerminalState.Completed; StatusText.Text = $"COMPLETED: {receipt.Tag} -> {receipt.NewHead}";
        }
        catch (OperationCanceledException) { ShowCancelled(); } catch (InvalidDataException ex) { ShowInvalid(ex); } catch (Exception ex) { ShowFailure(ex); }
        finally { EndRun(); SetV035PrimaryControlsEnabled(true); AcceptCheckpointButton.IsEnabled = false; }
    }

    private async void PublishAcceptedV0371Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveSettings(); var candidate = await _fixedGitHubPublicationV0371Service.PreviewAsync(WorkspaceRootBox.Text, CancellationToken.None);
            var preview = $"Опубликовать принятый Workbench v0.37.1?\n\nRemote: {candidate.RemoteName}\nURL: {candidate.RemoteUrl}\nAccepted HEAD: {candidate.Head}\nParent: {candidate.Parent}\nTag: {candidate.AcceptedTag}\n\nТолько exact fast-forward/tag; Candidate import/Local Apps authority не создаётся.";
            if (MessageBox.Show(this, preview, "Publish accepted v0.37.1", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            SetV035PrimaryControlsEnabled(false); BeginRun($"publish-v0.37.1-{DateTime.Now:yyyyMMddHHmmss}");
            var receipt = await _fixedGitHubPublicationV0371Service.PublishAsync(candidate, _cts!.Token);
            var path = await FixedGitHubPublicationV0371Service.WriteReceiptAsync(WorkspaceRootBox.Text, receipt, _cts.Token);
            AcceptanceTextBox.Text = CommandCodec.Serialize(new { Publication = receipt, PublicationReceiptPath = path, CandidateImportAuthorityCreated = false, LocalAppAuthorityCreated = false, NextExplicitAction = "Lifecycle receipt" });
            OutputTabs.SelectedItem = AcceptanceTab; ProgressBar.Value = 100; _currentTerminalState = CommandTerminalState.Completed; StatusText.Text = $"COMPLETED: remote main/tag == {receipt.LocalHead}";
        }
        catch (OperationCanceledException) { ShowCancelled(); } catch (InvalidDataException ex) { ShowInvalid(ex); } catch (Exception ex) { ShowFailure(ex); }
        finally { EndRun(); SetV035PrimaryControlsEnabled(true); }
    }

    private async void LocalAppsV0371Button_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings();
        var workspace = Path.GetFullPath(WorkspaceRootBox.Text.Trim());
        var appsRoot = Path.Combine(workspace, LocalApplicationRegistrationService.AppsDirectoryName);
        if (!Directory.Exists(appsRoot)) { MessageBox.Show(this, $"Managed Apps root отсутствует:\n{appsRoot}", "Local apps", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var folderDialog = new OpenFolderDialog { Title = "Выберите приложение внутри Workspace\\Apps", InitialDirectory = appsRoot, Multiselect = false };
        if (folderDialog.ShowDialog(this) != true) return;
        var selectedRoot = Path.GetFullPath(folderDialog.FolderName);
        var identityPath = Path.Combine(selectedRoot, LocalApplicationRegistrationService.IdentityFileName);
        if (!File.Exists(identityPath))
        {
            try
            {
                await _localApplicationManagedRoleGuardV0371Service.EnsureRegistrationRoleAllowedAsync(selectedRoot, WorkspaceRootBox.Text, CancellationToken.None);
            }
            catch (InvalidDataException ex)
            {
                ShowInvalid(ex);
                return;
            }
            await RegisterSelectedLocalAppAsync(selectedRoot);
            return;
        }

        var choice = MessageBox.Show(this,
            "Приложение зарегистрировано.\n\nYES — Update from package\nNO — Build update package from Workspace\\AppCandidates\\<ApplicationId>\nCANCEL — ничего не делать\n\nBuild package не обновляет и не запускает приложение.",
            "Local apps", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (choice == MessageBoxResult.Yes) { await UpdateSelectedLocalAppAsync(selectedRoot); return; }
        if (choice == MessageBoxResult.No) await BuildLocalAppPackageAsync(selectedRoot);
    }
}
