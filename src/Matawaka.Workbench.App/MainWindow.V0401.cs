using System.IO;
using System.Text;
using System.Windows;
using Matawaka.Workbench.Protocol;
using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalCheckpointV0401Service _checkpointV0401Service = new();
    private readonly FixedGitHubPublicationV0401Service _fixedGitHubPublicationV0401Service = new();
    private bool _v0401LoadedBootstrapChecked;

    internal void ConfigureV0401Routing()
    {
        Title = "Matawaka Workbench v0.40.1";

        // Keep the accepted v0.40 Update Workbench and v0.39 manual launch handlers.
        // Only successor-version startup/acceptance/publication routing changes.
        Loaded -= Window_LoadedV040;
        Loaded += Window_LoadedV0401;
        SelfTestButton.Click -= SelfTestV040Button_Click;
        SelfTestButton.Click += SelfTestV0401Button_Click;
        AcceptCheckpointButton.Click -= AcceptCheckpointV040Button_Click;
        AcceptCheckpointButton.Click += AcceptCheckpointV0401Button_Click;
        PublishAcceptedButton.Click -= PublishAcceptedV040Button_Click;
        PublishAcceptedButton.Click += PublishAcceptedV0401Button_Click;
    }

    private async void Window_LoadedV0401(object sender, RoutedEventArgs e)
    {
        if (_v0401LoadedBootstrapChecked) return;
        _v0401LoadedBootstrapChecked = true;
        TransitionBootstrapV040Claim? claim = null;
        var beganRun = false;
        try
        {
            claim = await _transitionBootstrapV040Service.TryClaimFirstBootAsync(
                WorkspaceRootBox.Text,
                LocalCheckpointV0401Service.Version,
                LocalCheckpointV0401Service.TargetTag,
                CancellationToken.None);
            if (claim is null)
            {
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v0401 none; automaticSelfTest=false; automaticAccept=false");
                return;
            }

            SetV035PrimaryControlsEnabled(false);
            BeginRun($"first-boot-bootstrap-v0.40.1-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;
            StatusText.Text = $"RUNNING: v0.40.1 first-boot one-shot Self-test; lease={claim.Lease.LeaseId}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v0401 consuming lease={claim.Lease.LeaseId}; pid={Environment.ProcessId}; retry=false");

            var tested = await RunV0401AcceptanceArtifactAsync(_cts!.Token);
            _lastAcceptanceReceipt = tested.Receipt;
            _lastAcceptanceArtifactPath = tested.ArtifactPath;
            _lastAcceptanceConsumed = false;

            if (!tested.Receipt.Passed)
            {
                await _transitionBootstrapV040Service.MarkFailedNoRetryAsync(
                    claim.Lease, claim.LeasePath, "v0.40.1 first-boot Self-test returned Passed=false", CancellationToken.None);
                _currentTerminalState = CommandTerminalState.Failed;
                StatusText.Text = "FAILED: v0.40.1 first-boot Self-test did not pass; automatic Accept refused; no retry authority";
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

            var checkpointCandidate = await _checkpointV0401Service.PreviewAsync(
                WorkspaceRootBox.Text, tested.ArtifactPath, tested.Receipt, _cts.Token);
            var checkpoint = await _checkpointV0401Service.AcceptFromBootstrapAsync(
                checkpointCandidate, claim.Lease.LeaseId, _cts.Token);
            var checkpointPath = await LocalCheckpointV0401Service.WriteReceiptAsync(
                WorkspaceRootBox.Text, checkpoint, _cts.Token);
            var completed = await _transitionBootstrapV040Service.FinalizeAcceptedAsync(
                claim, tested.ArtifactPath, checkpointPath, _cts.Token);

            _lastAcceptanceConsumed = true;
            AcceptCheckpointButton.IsEnabled = false;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: v0.40.1 first-boot Self-test PASS + automatic local Accept; {checkpoint.Tag} -> {checkpoint.NewHead}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v0401 completed lease={completed.LeaseId}; selfTest=true; accepted=true; publish=false; lifecycle=false");
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
                AcceptedV040BootstrapRuntimeChanged = false,
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

    private async Task<(WorkbenchAcceptanceReceipt Receipt, string ArtifactPath)> RunV0401AcceptanceArtifactAsync(CancellationToken cancellationToken)
    {
        var context = new RuntimeContext(CatalogRootBox.Text, true, false);
        var receipt = await new WorkbenchV0401AcceptanceHarness(_acceptanceHarness).RunAsync(context, cancellationToken);
        var dir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"v0.40.1-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, CommandCodec.Serialize(receipt), new UTF8Encoding(false), cancellationToken);
        return (receipt, path);
    }

    private async void SelfTestV0401Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveSettings();
            SetV035PrimaryControlsEnabled(false);
            BeginRun($"self-test-v0.40.1-{DateTime.Now:yyyyMMddHHmmss}");
            StatusText.Text = "RUNNING: manual v0.40.1 Self-test; no bootstrap authority created";
            var tested = await RunV0401AcceptanceArtifactAsync(_cts!.Token);
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
            StatusText.Text = tested.Receipt.Passed ? $"COMPLETED: manual v0.40.1 Self-test PASSED; {tested.ArtifactPath}" : "FAILED: v0.40.1 acceptance matrix has failing checks";
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally
        {
            EndRun();
            SetV035PrimaryControlsEnabled(true);
            AcceptCheckpointButton.IsEnabled = _lastAcceptanceReceipt?.Passed == true && !_lastAcceptanceConsumed &&
                                               _lastAcceptanceReceipt.Version == LocalCheckpointV0401Service.Version;
        }
    }

    private async void AcceptCheckpointV0401Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_lastAcceptanceReceipt is null || !_lastAcceptanceReceipt.Passed ||
                _lastAcceptanceReceipt.Version != LocalCheckpointV0401Service.Version || string.IsNullOrWhiteSpace(_lastAcceptanceArtifactPath))
                throw new InvalidDataException("Run a passing v0.40.1 Self-test before manual Accept.");
            if (_lastAcceptanceConsumed) throw new InvalidDataException("The latest v0.40.1 Self-test receipt has already been consumed.");
            SaveSettings();
            var candidate = await _checkpointV0401Service.PreviewAsync(
                WorkspaceRootBox.Text, _lastAcceptanceArtifactPath, _lastAcceptanceReceipt, CancellationToken.None);
            var preview = $"Создать локальный accepted checkpoint Workbench v0.40.1 вручную?\n\nPredecessor: {candidate.PreviousHead} / {candidate.ExpectedPredecessorTag}\nTarget tag: {candidate.TargetTag}\nAcceptance SHA-256: {candidate.AcceptanceArtifactSha256}\n\nЭто только ручной fallback, если one-shot first-boot path не был использован. Publish и Lifecycle остаются отдельными решениями.";
            if (MessageBox.Show(this, preview, "Принять Workbench v0.40.1", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            SetV035PrimaryControlsEnabled(false);
            BeginRun($"accept-v0.40.1-{DateTime.Now:yyyyMMddHHmmss}");
            var receipt = await _checkpointV0401Service.AcceptAsync(candidate, _cts!.Token);
            var path = await LocalCheckpointV0401Service.WriteReceiptAsync(WorkspaceRootBox.Text, receipt, _cts.Token);
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

    private async void PublishAcceptedV0401Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveSettings();
            var candidate = await _fixedGitHubPublicationV0401Service.PreviewAsync(WorkspaceRootBox.Text, CancellationToken.None);
            var preview = $"Опубликовать принятый Workbench v0.40.1?\n\nRemote: {candidate.RemoteName}\nURL: {candidate.RemoteUrl}\nAccepted HEAD: {candidate.Head}\nParent: {candidate.Parent}\nTag: {candidate.AcceptedTag}\n\nOne-shot bootstrap заканчивается на local Accept. Publication остаётся отдельным текущим подтверждением; только exact fast-forward/tag, без force/tag movement.";
            if (MessageBox.Show(this, preview, "Publish accepted v0.40.1", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            SetV035PrimaryControlsEnabled(false);
            BeginRun($"publish-v0.40.1-{DateTime.Now:yyyyMMddHHmmss}");
            var receipt = await _fixedGitHubPublicationV0401Service.PublishAsync(candidate, _cts!.Token);
            var path = await FixedGitHubPublicationV0401Service.WriteReceiptAsync(WorkspaceRootBox.Text, receipt, _cts.Token);
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Publication = receipt,
                PublicationReceiptPath = path,
                TransitionBootstrapAuthorityCreated = false,
                AutomaticAcceptAuthorityCreated = false,
                AcceptedV040BootstrapRuntimeChanged = false,
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
