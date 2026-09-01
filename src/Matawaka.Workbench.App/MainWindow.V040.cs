using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Matawaka.Workbench.Protocol;
using Matawaka.Workbench.Runtime;
using Microsoft.Win32;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly TransitionBootstrapV040Service _transitionBootstrapV040Service = new();
    private readonly LocalCheckpointV040Service _checkpointV040Service = new();
    private readonly FixedGitHubPublicationV040Service _fixedGitHubPublicationV040Service = new();
    private bool _v040LoadedBootstrapChecked;

    private async void UpdateCandidateV040Button_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Workbench update package (*.zip)|*.zip|Все файлы (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;

        var id = $"update-bootstrap-v0.40-{DateTime.Now:yyyyMMddHHmmss}";
        var beganRun = false;
        var closePredecessor = false;
        TransitionBootstrapV040Lease? bootstrapLease = null;
        string? bootstrapLeasePath = null;
        UpdateCandidateButton.IsEnabled = false;
        try
        {
            ResetV033UpdateState();
            SaveSettings();
            var orchestrator = CreateV033Orchestrator();
            var preview = await orchestrator.PrepareAsync(dialog.FileName, WorkspaceRootBox.Text, CancellationToken.None);

            var message = new StringBuilder();
            message.AppendLine("Обновить Workbench одним подтверждённым переходом?");
            message.AppendLine();
            message.AppendLine($"Package: {preview.PackageFileName}");
            message.AppendLine($"SHA-256: {preview.PackageSha256}");
            message.AppendLine($"Predecessor: {preview.PredecessorCommit} / {preview.PredecessorTag}");
            message.AppendLine($"Target: {preview.TargetVersion} / {preview.TargetTag}");
            message.AppendLine($"Payload: {preview.PreviewPlan.PayloadFileCount} files; {preview.PreviewPlan.PayloadBytes} bytes");
            message.AppendLine();
            message.AppendLine("После этого ОДНОГО подтверждения Workbench выполнит существующие fresh typed gates plan → materialize → staged plan → exact apply/build, затем создаст одноразовый byte-bound bootstrap lease и автоматически запустит только exact собранный candidate.");
            message.AppendLine("После persisted launch receipt + live exact-process-image handoff текущее старое окно закроется само.");
            message.AppendLine("Только первый boot exact successor PID сможет одноразово потребить lease: автоматически выполнить Self-test и, ТОЛЬКО если Passed=true, создать локальный accepted checkpoint без второго диалога.");
            message.AppendLine();
            message.AppendLine("Повторный/ручной запуск без ACTIVATED lease НЕ получает auto Self-test/Accept. Любой сбой делает lease FAILED_NO_RETRY; автоматического повтора нет.");
            message.AppendLine("Publish accepted и Lifecycle receipt НЕ автоматизируются и останутся отдельными кнопками. Force push, arbitrary process, catalog mutation, Agent Execute и general network authority не создаются.");

            if (MessageBox.Show(this, message.ToString(), "Workbench transition bootstrap v0.40", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            BeginRun(id);
            beganRun = true;
            SetV035PrimaryControlsEnabled(false);
            StatusText.Text = "RUNNING: exact update → one-shot bootstrap → automatic launch/handoff";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.requested target={preview.TargetVersion}; oneUiConfirmation=true; publish=false");

            var orchestration = await orchestrator.ExecuteConfirmedAsync(preview, WorkspaceRootBox.Text, _cts!.Token);
            var orchestrationPath = await MaintenanceUpdateOrchestratorService.WriteReceiptAsync(WorkspaceRootBox.Text, orchestration, _cts.Token);

            _lastUpdatePlanReceipt = orchestration.FreshPlan;
            _lastUpdatePlanArtifactPath = orchestration.FreshPlanArtifactPath;
            _lastUpdatePackagePath = orchestration.PackagePath;
            _lastUpdatePlanConsumed = true;
            _lastMaterializationReceipt = orchestration.Materialization;
            _lastMaterializationArtifactPath = orchestration.MaterializationArtifactPath;
            _lastStagedApplyPlanReceipt = orchestration.StagedApplyPlan;
            _lastStagedApplyPlanArtifactPath = orchestration.StagedApplyPlanArtifactPath;
            _lastApplyBuildReceipt = orchestration.ApplyBuild;
            _lastApplyBuildArtifactPath = orchestration.ApplyBuildArtifactPath;

            var authoritySource = $"explicit Update Workbench v0.40 transition confirmation; package={preview.PackageSha256}; predecessor={preview.PredecessorCommit}/{preview.PredecessorTag}; target={preview.TargetVersion}/{preview.TargetTag}";
            var prepared = await _transitionBootstrapV040Service.PrepareAsync(
                orchestration.ApplyBuild,
                orchestration.ApplyBuildArtifactPath,
                WorkspaceRootBox.Text,
                authoritySource,
                _cts.Token);
            bootstrapLease = prepared.Lease;
            bootstrapLeasePath = prepared.LeasePath;
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.prepared lease={bootstrapLease.LeaseId}; autoLaunch=true; firstBootAccept=true; retry=false");

            var launched = await _applyBuildService.LaunchCandidateAsync(orchestration.ApplyBuild, WorkspaceRootBox.Text, _cts.Token);
            var handoff = await _candidateLaunchHandoffV039Service.ObserveAndPersistAsync(
                launched.Receipt, launched.ArtifactPath, WorkspaceRootBox.Text, _cts.Token);
            var activated = await _transitionBootstrapV040Service.ActivateAsync(
                bootstrapLease, bootstrapLeasePath,
                launched.Receipt, launched.ArtifactPath,
                handoff.Receipt, handoff.ArtifactPath,
                WorkspaceRootBox.Text, _cts.Token);
            bootstrapLease = activated;

            UpdatePlanTextBox.Text = CommandCodec.Serialize(new
            {
                Orchestration = orchestration,
                OrchestrationReceiptPath = orchestrationPath,
                Bootstrap = activated,
                BootstrapLeasePath = bootstrapLeasePath,
                Launch = launched.Receipt,
                LaunchReceiptPath = launched.ArtifactPath,
                Handoff = handoff.Receipt,
                HandoffReceiptPath = handoff.ArtifactPath,
                AutomaticLaunchPerformed = true,
                FirstBootSelfTestEligible = true,
                FirstBootAcceptIfSelfTestPassesEligible = true,
                AutomaticPublish = false,
                AutomaticLifecycle = false,
                PredecessorSelfCloseScheduled = true
            });
            OutputTabs.SelectedItem = UpdatePlanTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: bootstrap ACTIVATED; pid={launched.Receipt.ProcessId}; successor will claim once; closing predecessor";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.activated lease={activated.LeaseId}; pid={launched.Receipt.ProcessId}; imageMatch=true; selfClose=true; publish=false");
            closePredecessor = true;
        }
        catch (OperationCanceledException ex)
        {
            if (bootstrapLease is not null && bootstrapLeasePath is not null)
                await TryFailBootstrapAsync(bootstrapLease, bootstrapLeasePath, ex.Message);
            ResetV033UpdateState();
            ShowCancelled();
        }
        catch (InvalidDataException ex)
        {
            if (bootstrapLease is not null && bootstrapLeasePath is not null)
                await TryFailBootstrapAsync(bootstrapLease, bootstrapLeasePath, ex.Message);
            ShowInvalid(ex);
        }
        catch (Exception ex)
        {
            if (bootstrapLease is not null && bootstrapLeasePath is not null)
                await TryFailBootstrapAsync(bootstrapLease, bootstrapLeasePath, ex.Message);
            ShowFailure(ex);
        }
        finally
        {
            if (beganRun) EndRun();
            SetV035PrimaryControlsEnabled(true);
            UpdateCandidateButton.IsEnabled = true;
            if (closePredecessor)
                _ = Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(Close));
        }
    }

    private async void Window_LoadedV040(object sender, RoutedEventArgs e)
    {
        if (_v040LoadedBootstrapChecked) return;
        _v040LoadedBootstrapChecked = true;
        TransitionBootstrapV040Claim? claim = null;
        var beganRun = false;
        try
        {
            claim = await _transitionBootstrapV040Service.TryClaimFirstBootAsync(
                WorkspaceRootBox.Text,
                LocalCheckpointV040Service.Version,
                LocalCheckpointV040Service.TargetTag,
                CancellationToken.None);
            if (claim is null)
            {
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.first-boot none; automaticSelfTest=false; automaticAccept=false");
                return;
            }

            SetV035PrimaryControlsEnabled(false);
            BeginRun($"first-boot-bootstrap-v0.40-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;
            StatusText.Text = $"RUNNING: first-boot one-shot Self-test; lease={claim.Lease.LeaseId}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.consuming lease={claim.Lease.LeaseId}; pid={Environment.ProcessId}; retry=false");

            var tested = await RunV040AcceptanceArtifactAsync(_cts!.Token);
            _lastAcceptanceReceipt = tested.Receipt;
            _lastAcceptanceArtifactPath = tested.ArtifactPath;
            _lastAcceptanceConsumed = false;

            if (!tested.Receipt.Passed)
            {
                await _transitionBootstrapV040Service.MarkFailedNoRetryAsync(
                    claim.Lease, claim.LeasePath, "first-boot Self-test returned Passed=false", CancellationToken.None);
                _currentTerminalState = CommandTerminalState.Failed;
                StatusText.Text = "FAILED: first-boot Self-test did not pass; automatic Accept refused; no retry authority";
                AcceptanceTextBox.Text = CommandCodec.Serialize(new
                {
                    Bootstrap = claim.Lease,
                    BootstrapLeasePath = claim.LeasePath,
                    Acceptance = tested.Receipt,
                    AcceptanceArtifactPath = tested.ArtifactPath,
                    AutomaticAcceptPerformed = false,
                    AutomaticRetryAuthorized = false
                });
                OutputTabs.SelectedItem = AcceptanceTab;
                return;
            }

            var checkpointCandidate = await _checkpointV040Service.PreviewAsync(
                WorkspaceRootBox.Text, tested.ArtifactPath, tested.Receipt, _cts.Token);
            var checkpoint = await _checkpointV040Service.AcceptFromBootstrapAsync(
                checkpointCandidate, claim.Lease.LeaseId, _cts.Token);
            var checkpointPath = await LocalCheckpointV040Service.WriteReceiptAsync(
                WorkspaceRootBox.Text, checkpoint, _cts.Token);
            var completed = await _transitionBootstrapV040Service.FinalizeAcceptedAsync(
                claim, tested.ArtifactPath, checkpointPath, _cts.Token);

            _lastAcceptanceConsumed = true;
            AcceptCheckpointButton.IsEnabled = false;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: first-boot Self-test PASS + local Accept; {checkpoint.Tag} -> {checkpoint.NewHead}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.completed lease={completed.LeaseId}; selfTest=true; accepted=true; publish=false; lifecycle=false");
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Bootstrap = completed,
                BootstrapLeasePath = claim.LeasePath,
                Acceptance = tested.Receipt,
                AcceptanceArtifactPath = tested.ArtifactPath,
                Checkpoint = checkpoint,
                CheckpointReceiptPath = checkpointPath,
                AutomaticSelfTestPerformed = true,
                AutomaticAcceptPerformed = true,
                AutomaticPublishPerformed = false,
                AutomaticLifecyclePerformed = false,
                NextExplicitActions = new[] { "Publish accepted", "Lifecycle receipt" }
            });
            OutputTabs.SelectedItem = AcceptanceTab;
        }
        catch (OperationCanceledException ex)
        {
            if (claim is not null) await TryFailBootstrapAsync(claim.Lease, claim.LeasePath, ex.Message);
            ShowCancelled();
        }
        catch (InvalidDataException ex)
        {
            if (claim is not null) await TryFailBootstrapAsync(claim.Lease, claim.LeasePath, ex.Message);
            ShowInvalid(ex);
        }
        catch (Exception ex)
        {
            if (claim is not null) await TryFailBootstrapAsync(claim.Lease, claim.LeasePath, ex.Message);
            ShowFailure(ex);
        }
        finally
        {
            if (beganRun) EndRun();
            SetV035PrimaryControlsEnabled(true);
            if (_lastAcceptanceConsumed) AcceptCheckpointButton.IsEnabled = false;
        }
    }

    private async Task<(WorkbenchAcceptanceReceipt Receipt, string ArtifactPath)> RunV040AcceptanceArtifactAsync(CancellationToken cancellationToken)
    {
        var context = new RuntimeContext(CatalogRootBox.Text, true, false);
        var receipt = await new WorkbenchV040AcceptanceHarness(_acceptanceHarness).RunAsync(context, cancellationToken);
        var dir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"v0.40-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, CommandCodec.Serialize(receipt), new UTF8Encoding(false), cancellationToken);
        return (receipt, path);
    }

    private async void SelfTestV040Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveSettings();
            SetV035PrimaryControlsEnabled(false);
            BeginRun($"self-test-v0.40-{DateTime.Now:yyyyMMddHHmmss}");
            StatusText.Text = "RUNNING: manual v0.40 Self-test; no bootstrap authority created";
            var tested = await RunV040AcceptanceArtifactAsync(_cts!.Token);
            _lastAcceptanceReceipt = tested.Receipt;
            _lastAcceptanceArtifactPath = tested.ArtifactPath;
            _lastAcceptanceConsumed = false;
            AcceptCheckpointButton.IsEnabled = tested.Receipt.Passed;
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Receipt = tested.Receipt,
                ArtifactPath = tested.ArtifactPath,
                ManualSelfTest = true,
                BootstrapLeaseCreated = false,
                AutomaticAcceptPerformed = false,
                LocalCheckpointAvailable = tested.Receipt.Passed
            });
            OutputTabs.SelectedItem = AcceptanceTab;
            ProgressBar.Value = 100;
            _currentTerminalState = tested.Receipt.Passed ? CommandTerminalState.Completed : CommandTerminalState.Failed;
            StatusText.Text = tested.Receipt.Passed ? $"COMPLETED: manual v0.40 Self-test PASSED; {tested.ArtifactPath}" : "FAILED: v0.40 acceptance matrix has failing checks";
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally
        {
            EndRun();
            SetV035PrimaryControlsEnabled(true);
            AcceptCheckpointButton.IsEnabled = _lastAcceptanceReceipt?.Passed == true && !_lastAcceptanceConsumed &&
                                               _lastAcceptanceReceipt.Version == LocalCheckpointV040Service.Version;
        }
    }

    private async void AcceptCheckpointV040Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_lastAcceptanceReceipt is null || !_lastAcceptanceReceipt.Passed ||
                _lastAcceptanceReceipt.Version != LocalCheckpointV040Service.Version || string.IsNullOrWhiteSpace(_lastAcceptanceArtifactPath))
                throw new InvalidDataException("Run a passing v0.40 Self-test before manual Accept.");
            if (_lastAcceptanceConsumed) throw new InvalidDataException("The latest v0.40 Self-test receipt has already been consumed.");
            SaveSettings();
            var candidate = await _checkpointV040Service.PreviewAsync(
                WorkspaceRootBox.Text, _lastAcceptanceArtifactPath, _lastAcceptanceReceipt, CancellationToken.None);
            var preview = $"Создать локальный accepted checkpoint Workbench v0.40 вручную?\n\nPredecessor: {candidate.PreviousHead} / {candidate.ExpectedPredecessorTag}\nTarget tag: {candidate.TargetTag}\nAcceptance SHA-256: {candidate.AcceptanceArtifactSha256}\n\nЭто ручной fallback. Publish и Lifecycle остаются отдельными решениями.";
            if (MessageBox.Show(this, preview, "Принять Workbench v0.40", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            SetV035PrimaryControlsEnabled(false);
            BeginRun($"accept-v0.40-{DateTime.Now:yyyyMMddHHmmss}");
            var receipt = await _checkpointV040Service.AcceptAsync(candidate, _cts!.Token);
            var path = await LocalCheckpointV040Service.WriteReceiptAsync(WorkspaceRootBox.Text, receipt, _cts.Token);
            _lastAcceptanceConsumed = true;
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Acceptance = _lastAcceptanceReceipt,
                AcceptanceArtifactPath = _lastAcceptanceArtifactPath,
                Checkpoint = receipt,
                CheckpointReceiptPath = path,
                ManualAccept = true,
                BootstrapLeaseConsumed = false,
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

    private async void PublishAcceptedV040Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveSettings();
            var candidate = await _fixedGitHubPublicationV040Service.PreviewAsync(WorkspaceRootBox.Text, CancellationToken.None);
            var preview = $"Опубликовать принятый Workbench v0.40?\n\nRemote: {candidate.RemoteName}\nURL: {candidate.RemoteUrl}\nAccepted HEAD: {candidate.Head}\nParent: {candidate.Parent}\nTag: {candidate.AcceptedTag}\n\nBootstrap заканчивается на local Accept. Publication — отдельное текущее подтверждение; только exact fast-forward/tag, без force/tag movement.";
            if (MessageBox.Show(this, preview, "Publish accepted v0.40", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            SetV035PrimaryControlsEnabled(false);
            BeginRun($"publish-v0.40-{DateTime.Now:yyyyMMddHHmmss}");
            var receipt = await _fixedGitHubPublicationV040Service.PublishAsync(candidate, _cts!.Token);
            var path = await FixedGitHubPublicationV040Service.WriteReceiptAsync(WorkspaceRootBox.Text, receipt, _cts.Token);
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Publication = receipt,
                PublicationReceiptPath = path,
                TransitionBootstrapAuthorityCreated = false,
                AutomaticAcceptAuthorityCreated = false,
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

    private async Task TryFailBootstrapAsync(TransitionBootstrapV040Lease lease, string leasePath, string failure)
    {
        try { await _transitionBootstrapV040Service.MarkFailedNoRetryAsync(lease, leasePath, failure, CancellationToken.None); }
        catch { /* best-effort failure evidence; never create retry authority */ }
    }
}
