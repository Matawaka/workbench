using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Matawaka.Workbench.Protocol;
using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly CandidateLaunchHandoffV039Service _candidateLaunchHandoffV039Service = new();
    private readonly LocalCheckpointV039Service _checkpointV039Service = new();
    private readonly FixedGitHubPublicationV039Service _fixedGitHubPublicationV039Service = new();

    private async void LaunchCandidateV039Button_Click(object sender, RoutedEventArgs e)
    {
        var id = $"update-launch-handoff-v039-{DateTime.Now:yyyyMMddHHmmss}";
        var closePredecessor = false;
        try
        {
            if (_lastApplyBuildReceipt is null || string.IsNullOrWhiteSpace(_lastApplyBuildArtifactPath))
                throw new InvalidDataException("Build a byte-bound candidate before launch.");

            var receipt = _lastApplyBuildReceipt;
            var preview = new StringBuilder();
            preview.AppendLine("Запустить точный собранный Workbench candidate и передать ему окно?");
            preview.AppendLine();
            preview.AppendLine($"Target: {receipt.TargetVersion} / {receipt.TargetTag}");
            preview.AppendLine($"Executable: {receipt.CandidateExecutablePath}");
            preview.AppendLine($"SHA-256: {receipt.CandidateExecutableSha256}");
            preview.AppendLine();
            preview.AppendLine("После успешного receipt-bound запуска Workbench подождёт короткий bounded интервал, повторно свяжет сохранённый launch receipt с exact PID/process image и только затем автоматически закроет ТЕКУЩЕЕ старое окно.");
            preview.AppendLine("Если candidate не стартует, успеет завершиться или PID/image не совпадут — старый Workbench останется открыт.");
            preview.AppendLine("Candidate launch/handoff НЕ означает acceptance: новый Workbench должен отдельно пройти Self-test и Accept. Внешний process kill, Git/network/catalog/Agent Execute authority не создаются.");
            if (MessageBox.Show(this, preview.ToString(), "Запустить candidate + безопасный handoff", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            SaveSettings();
            SetV035PrimaryControlsEnabled(false);
            BeginRun(id);
            StatusText.Text = "RUNNING: exact candidate launch + verified handoff";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  update.candidate.handoff.requested target={receipt.TargetVersion}; accepted=false; selfClose=false");

            var launched = await _applyBuildService.LaunchCandidateAsync(
                receipt, WorkspaceRootBox.Text, _cts!.Token);
            var handoff = await _candidateLaunchHandoffV039Service.ObserveAndPersistAsync(
                launched.Receipt, launched.ArtifactPath, WorkspaceRootBox.Text, _cts!.Token);

            if (!handoff.Receipt.PredecessorSelfCloseEligible ||
                !handoff.Receipt.CandidateObservedAlive ||
                !handoff.Receipt.ProcessImageMatchedCandidate ||
                handoff.Receipt.CandidateAcceptanceCreated ||
                handoff.Receipt.ExternalProcessTerminationAuthorityCreated)
                throw new InvalidDataException("v0.39 handoff receipt did not authorize bounded predecessor self-close.");

            UpdatePlanTextBox.Text = CommandCodec.Serialize(new
            {
                ApplyBuild = receipt,
                ApplyBuildReceiptPath = _lastApplyBuildArtifactPath,
                Launch = launched.Receipt,
                LaunchReceiptPath = launched.ArtifactPath,
                Handoff = handoff.Receipt,
                HandoffReceiptPath = handoff.ArtifactPath,
                PredecessorSelfCloseScheduled = true,
                CandidateAccepted = false,
                ExternalProcessTerminationAuthorityCreated = false
            });
            OutputTabs.SelectedItem = UpdatePlanTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: {handoff.Receipt.Status}; pid={launched.Receipt.ProcessId}; closing predecessor";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  update.candidate.handoff.ready pid={launched.Receipt.ProcessId}; imageMatch=true; accepted=false; selfClose=true");
            closePredecessor = true;
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally
        {
            EndRun();
            SetV035PrimaryControlsEnabled(true);
            if (closePredecessor)
                _ = Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(Close));
        }
    }

    private async void SelfTestV039Button_Click(object sender, RoutedEventArgs e)
    {
        var id = $"self-test-v0.39-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            SaveSettings();
            SetV035PrimaryControlsEnabled(false);
            BeginRun(id);
            StatusText.Text = "RUNNING: v0.39 acceptance + candidate launch handoff checks";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  acceptance.started           v0.39; launchEffect=false; selfCloseEffect=false");
            var context = new RuntimeContext(CatalogRootBox.Text, true, false);
            var receipt = await new WorkbenchV039AcceptanceHarness(_acceptanceHarness).RunAsync(context, _cts!.Token);
            var dir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"v0.39-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            await File.WriteAllTextAsync(path, CommandCodec.Serialize(receipt), new UTF8Encoding(false), _cts.Token);
            _lastAcceptanceReceipt = receipt;
            _lastAcceptanceArtifactPath = path;
            _lastAcceptanceConsumed = false;
            AcceptCheckpointButton.IsEnabled = receipt.Passed;
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Receipt = receipt,
                ArtifactPath = path,
                CandidateLaunchPerformed = false,
                HandoffPerformed = false,
                PredecessorClosed = false,
                LocalCheckpointAvailable = receipt.Passed
            });
            OutputTabs.SelectedItem = AcceptanceTab;
            ProgressBar.Value = 100;
            _currentTerminalState = receipt.Passed ? CommandTerminalState.Completed : CommandTerminalState.Failed;
            StatusText.Text = receipt.Passed ? $"COMPLETED: v0.39 Self-test PASSED; {path}" : "FAILED: v0.39 acceptance matrix has failing checks";
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally
        {
            EndRun();
            SetV035PrimaryControlsEnabled(true);
            AcceptCheckpointButton.IsEnabled = _lastAcceptanceReceipt?.Passed == true && !_lastAcceptanceConsumed &&
                                               _lastAcceptanceReceipt.Version == LocalCheckpointV039Service.Version;
        }
    }

    private async void AcceptCheckpointV039Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_lastAcceptanceReceipt is null || !_lastAcceptanceReceipt.Passed ||
                _lastAcceptanceReceipt.Version != LocalCheckpointV039Service.Version || string.IsNullOrWhiteSpace(_lastAcceptanceArtifactPath))
                throw new InvalidDataException("Run a passing v0.39 Self-test before accepting the checkpoint.");
            if (_lastAcceptanceConsumed) throw new InvalidDataException("The latest v0.39 Self-test receipt has already been consumed.");
            SaveSettings();
            var candidate = await _checkpointV039Service.PreviewAsync(
                WorkspaceRootBox.Text, _lastAcceptanceArtifactPath, _lastAcceptanceReceipt, CancellationToken.None);
            var preview = $"Создать локальный accepted checkpoint Workbench v0.39?\n\nPredecessor: {candidate.PreviousHead} / {candidate.ExpectedPredecessorTag}\nTarget tag: {candidate.TargetTag}\nAcceptance SHA-256: {candidate.AcceptanceArtifactSha256}\n\nЭто только local commit/tag. Candidate launch/handoff, Publish и Lifecycle остаются отдельными решениями.";
            if (MessageBox.Show(this, preview, "Принять Workbench v0.39", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            SetV035PrimaryControlsEnabled(false);
            BeginRun($"accept-v0.39-{DateTime.Now:yyyyMMddHHmmss}");
            var receipt = await _checkpointV039Service.AcceptAsync(candidate, _cts!.Token);
            var path = await LocalCheckpointV039Service.WriteReceiptAsync(WorkspaceRootBox.Text, receipt, _cts.Token);
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

    private async void PublishAcceptedV039Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveSettings();
            var candidate = await _fixedGitHubPublicationV039Service.PreviewAsync(WorkspaceRootBox.Text, CancellationToken.None);
            var preview = $"Опубликовать принятый Workbench v0.39?\n\nRemote: {candidate.RemoteName}\nURL: {candidate.RemoteUrl}\nAccepted HEAD: {candidate.Head}\nParent: {candidate.Parent}\nTag: {candidate.AcceptedTag}\n\nТолько exact fast-forward/tag; launch/handoff/self-close/candidate-acceptance authority не создаётся.";
            if (MessageBox.Show(this, preview, "Publish accepted v0.39", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            SetV035PrimaryControlsEnabled(false);
            BeginRun($"publish-v0.39-{DateTime.Now:yyyyMMddHHmmss}");
            var receipt = await _fixedGitHubPublicationV039Service.PublishAsync(candidate, _cts!.Token);
            var path = await FixedGitHubPublicationV039Service.WriteReceiptAsync(WorkspaceRootBox.Text, receipt, _cts.Token);
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Publication = receipt,
                PublicationReceiptPath = path,
                CandidateLaunchAuthorityCreated = false,
                HandoffAuthorityCreated = false,
                PredecessorCloseAuthorityCreated = false,
                CandidateAcceptanceCreated = false,
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
}
