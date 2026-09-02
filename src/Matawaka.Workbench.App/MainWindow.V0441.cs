using System.IO;
using System.Text;
using System.Windows;
using Matawaka.Workbench.Protocol;
using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalCheckpointV0441Service _checkpointV0441Service = new();
    private readonly FixedGitHubPublicationV0441Service _fixedGitHubPublicationV0441Service = new();
    private bool _v0441LoadedBootstrapChecked;

    internal void ConfigureV0441Routing()
    {
        ConfigureV044Routing();
        Title = "Matawaka Workbench v0.44.1";

        Loaded -= Window_LoadedV044;
        Loaded += Window_LoadedV0441;
        PublishAcceptedButton.Click -= PublishAcceptedV044Button_Click;
        PublishAcceptedButton.Click += PublishAcceptedV0441Button_Click;

        InstallV0441TreeDoubleClickRouting();
        DisableLegacyManualControlsV042();
        LaunchCandidateButton.IsEnabled = false;
        RefreshInstalledAppsV044();
    }

    private async void Window_LoadedV0441(object sender, RoutedEventArgs e)
    {
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
        if (_v0441LoadedBootstrapChecked) return;
        _v0441LoadedBootstrapChecked = true;
        TransitionBootstrapV040Claim? claim = null;
        var beganRun = false;
        try
        {
            claim = await _transitionBootstrapV040Service.TryClaimFirstBootAsync(
                WorkspaceRootBox.Text,
                LocalCheckpointV0441Service.Version,
                LocalCheckpointV0441Service.TargetTag,
                CancellationToken.None);
            if (claim is null)
            {
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v0441 none; automaticValidation=false; automaticAccept=false");
                return;
            }

            SetV035PrimaryControlsEnabled(false);
            DisableLegacyManualControlsV042();
            LaunchCandidateButton.IsEnabled = false;
            BeginRun($"first-boot-bootstrap-v0.44.1-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;
            StatusText.Text = $"RUNNING: v0.44.1 first-boot stabilization validation; lease={claim.Lease.LeaseId}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v0441 consuming lease={claim.Lease.LeaseId}; pid={Environment.ProcessId}; retry=false");

            var tested = await RunV0441AcceptanceArtifactAsync(_cts!.Token);
            _lastAcceptanceReceipt = tested.Receipt;
            _lastAcceptanceArtifactPath = tested.ArtifactPath;
            _lastAcceptanceConsumed = false;

            if (!tested.Receipt.Passed)
            {
                await _transitionBootstrapV040Service.MarkFailedNoRetryAsync(
                    claim.Lease, claim.LeasePath, "v0.44.1 first-boot stabilization validation returned Passed=false", CancellationToken.None);
                _currentTerminalState = CommandTerminalState.Failed;
                StatusText.Text = "FAILED: v0.44.1 stabilization validation did not pass; automatic local Accept refused; no retry authority";
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

            var checkpointCandidate = await _checkpointV0441Service.PreviewAsync(
                WorkspaceRootBox.Text, tested.ArtifactPath, tested.Receipt, _cts.Token);
            var checkpoint = await _checkpointV0441Service.AcceptFromBootstrapAsync(
                checkpointCandidate, claim.Lease.LeaseId, _cts.Token);
            var checkpointPath = await LocalCheckpointV0441Service.WriteReceiptAsync(
                WorkspaceRootBox.Text, checkpoint, _cts.Token);
            var completed = await _transitionBootstrapV040Service.FinalizeAcceptedAsync(
                claim, tested.ArtifactPath, checkpointPath, _cts.Token);

            _lastAcceptanceConsumed = true;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: v0.44.1 stabilization validation PASS + automatic local Accept; {checkpoint.Tag} -> {checkpoint.NewHead}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v0441 completed lease={completed.LeaseId}; validated=true; accepted=true; publish=false; lifecycle=false");
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Bootstrap = completed,
                BootstrapLeasePath = claim.LeasePath,
                Acceptance = tested.Receipt,
                AcceptanceArtifactPath = tested.ArtifactPath,
                Checkpoint = checkpoint,
                CheckpointReceiptPath = checkpointPath,
                AutomaticValidationPerformed = true,
                AutomaticAcceptPerformed = true,
                ExactFailedV044Predecessor = LocalCheckpointV0441Service.ExpectedPredecessorCommit,
                NestedTreeDoubleClickRoutingRepair = true,
                AppTextReadOnly = true,
                DynamicInspectionTabsClosable = true,
                VisibleTopLevelMaintenanceButtons = 4,
                LaunchCandidateVisible = false,
                AutomaticPublishPerformed = false,
                AutomaticLifecyclePerformed = false,
                NextExplicitActions = new[] { "Real-host nested file double-click check", "Publish accepted", "Lifecycle receipt" }
            });
            OutputTabs.SelectedItem = AcceptanceTab;
            RefreshInstalledAppsV044();
            InstallV0441TreeDoubleClickRouting();
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
            DisableLegacyManualControlsV042();
            LaunchCandidateButton.IsEnabled = false;
            RefreshInstalledAppsV044();
            InstallV0441TreeDoubleClickRouting();
        }
    }

    private async Task<(WorkbenchAcceptanceReceipt Receipt, string ArtifactPath)> RunV0441AcceptanceArtifactAsync(CancellationToken cancellationToken)
    {
        var context = new RuntimeContext(CatalogRootBox.Text, true, false);
        var receipt = await new WorkbenchV0441AcceptanceHarness(_acceptanceHarness).RunAsync(context, cancellationToken);
        var dir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"v0.44.1-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, CommandCodec.Serialize(receipt), new UTF8Encoding(false), cancellationToken);
        return (receipt, path);
    }

    private async void PublishAcceptedV0441Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveSettings();
            var candidate = await _fixedGitHubPublicationV0441Service.PreviewAsync(WorkspaceRootBox.Text, CancellationToken.None);
            var preview = $"Опубликовать принятый Workbench v0.44.1?\n\nRemote: {candidate.RemoteName}\nURL: {candidate.RemoteUrl}\nAccepted HEAD: {candidate.Head}\nLocal parent: {candidate.Parent} / {FixedGitHubPublicationV0441Service.ExpectedParentTag}\nRemote base: {FixedGitHubPublicationV0441Service.ExpectedRemoteBase} / {FixedGitHubPublicationV0441Service.ExpectedRemoteBaseTag}\nTag: {candidate.AcceptedTag}\n\nНажимайте Yes только после real-host проверки nested file double-click. Failed v0.44 tag MUST remain absent remotely. Publish fast-forwards the exact v0.44.1 chain; Lifecycle remains separate.";
            if (MessageBox.Show(this, preview, "Publish accepted v0.44.1", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            SetV035PrimaryControlsEnabled(false);
            DisableLegacyManualControlsV042();
            LaunchCandidateButton.IsEnabled = false;
            BeginRun($"publish-v0.44.1-{DateTime.Now:yyyyMMddHHmmss}");
            StatusText.Text = "RUNNING: publish accepted v0.44.1 to fixed GitHub remote";
            var receipt = await _fixedGitHubPublicationV0441Service.PublishAsync(candidate, _cts!.Token);
            var path = await FixedGitHubPublicationV0441Service.WriteReceiptAsync(WorkspaceRootBox.Text, receipt, _cts.Token);
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Publication = receipt,
                PublicationReceiptPath = path,
                FailedV044RemoteTagPublished = false,
                NestedRoutingAuthorityCreated = false,
                AppTextWriteAuthorityCreated = false,
                LaunchCandidateVisible = false,
                NextExplicitAction = "Lifecycle receipt"
            });
            OutputTabs.SelectedItem = AcceptanceTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: remote main/v0.44.1 tag == {receipt.LocalHead}; failed v0.44 tag remains absent";
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally
        {
            EndRun();
            SetV035PrimaryControlsEnabled(true);
            DisableLegacyManualControlsV042();
            LaunchCandidateButton.IsEnabled = false;
            RefreshInstalledAppsV044();
            InstallV0441TreeDoubleClickRouting();
        }
    }
}
