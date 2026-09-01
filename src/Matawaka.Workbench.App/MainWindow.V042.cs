using System.IO;
using System.Text;
using System.Windows;
using Matawaka.Workbench.Protocol;
using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalCheckpointV042Service _checkpointV042Service = new();
    private readonly FixedGitHubPublicationV042Service _fixedGitHubPublicationV042Service = new();
    private bool _v042LoadedBootstrapChecked;

    internal void ConfigureV042Routing()
    {
        // Preserve all accepted v0.41.2 behavior, then replace only the v0.42
        // release boundary and visible shell routing.
        ConfigureV0412Routing();
        Title = "Matawaka Workbench v0.42";

        Loaded -= Window_LoadedV0412;
        Loaded += Window_LoadedV042;
        PublishAcceptedButton.Click -= PublishAcceptedV0412Button_Click;
        PublishAcceptedButton.Click += PublishAcceptedV042Button_Click;

        // Manual validation/accept/cancel controls are compatibility bindings only.
        // Their internal mechanisms remain available to the bootstrap path, but the
        // hidden controls themselves have no click authority in v0.42.
        SelfTestButton.Click -= SelfTestV0412Button_Click;
        AcceptCheckpointButton.Click -= AcceptCheckpointV0412Button_Click;
        CancelButton.Click -= CancelButton_Click;
        DisableLegacyManualControlsV042();

        Activated += WindowV042_Activated;
        UpdateLocalAppButton.Click += (_, _) => Dispatcher.BeginInvoke(RefreshInstalledAppsV042);
        RefreshInstalledAppsV042();
    }

    private void WindowV042_Activated(object? sender, EventArgs e)
        => RefreshInstalledAppsV042();

    private void DisableLegacyManualControlsV042()
    {
        SelfTestButton.IsEnabled = false;
        AcceptCheckpointButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
    }

    private void RefreshInstalledAppsV042()
    {
        try
        {
            var apps = InstalledAppsV042Service.Read(WorkspaceRootBox.Text);
            InstalledAppsList.ItemsSource = apps.Select(app => app.Display).ToArray();
            InstalledAppsSummaryText.Text = $"Apps ({apps.Count})";
        }
        catch (Exception ex)
        {
            InstalledAppsList.ItemsSource = new[] { "⚠ unavailable" };
            InstalledAppsSummaryText.Text = "Apps";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  apps.observation.warning    {ex.Message}");
        }
    }

    private async void Window_LoadedV042(object sender, RoutedEventArgs e)
    {
        RefreshInstalledAppsV042();
        if (_v042LoadedBootstrapChecked) return;
        _v042LoadedBootstrapChecked = true;
        TransitionBootstrapV040Claim? claim = null;
        var beganRun = false;
        try
        {
            claim = await _transitionBootstrapV040Service.TryClaimFirstBootAsync(
                WorkspaceRootBox.Text,
                LocalCheckpointV042Service.Version,
                LocalCheckpointV042Service.TargetTag,
                CancellationToken.None);
            if (claim is null)
            {
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v042 none; automaticValidation=false; automaticAccept=false");
                return;
            }

            SetV035PrimaryControlsEnabled(false);
            DisableLegacyManualControlsV042();
            BeginRun($"first-boot-bootstrap-v0.42-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;
            StatusText.Text = $"RUNNING: v0.42 first-boot validation; lease={claim.Lease.LeaseId}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v042 consuming lease={claim.Lease.LeaseId}; pid={Environment.ProcessId}; retry=false");

            var tested = await RunV042AcceptanceArtifactAsync(_cts!.Token);
            _lastAcceptanceReceipt = tested.Receipt;
            _lastAcceptanceArtifactPath = tested.ArtifactPath;
            _lastAcceptanceConsumed = false;

            if (!tested.Receipt.Passed)
            {
                await _transitionBootstrapV040Service.MarkFailedNoRetryAsync(
                    claim.Lease, claim.LeasePath, "v0.42 first-boot validation returned Passed=false", CancellationToken.None);
                _currentTerminalState = CommandTerminalState.Failed;
                StatusText.Text = "FAILED: v0.42 first-boot validation did not pass; automatic local Accept refused; no retry authority";
                AcceptanceTextBox.Text = CommandCodec.Serialize(new
                {
                    Bootstrap = claim.Lease,
                    BootstrapLeasePath = claim.LeasePath,
                    Acceptance = tested.Receipt,
                    AcceptanceArtifactPath = tested.ArtifactPath,
                    AutomaticAcceptPerformed = false,
                    AutomaticRetryAuthorized = false,
                    ManualSelfTestButtonAvailable = false,
                    ManualAcceptButtonAvailable = false
                });
                OutputTabs.SelectedItem = AcceptanceTab;
                return;
            }

            var checkpointCandidate = await _checkpointV042Service.PreviewAsync(
                WorkspaceRootBox.Text, tested.ArtifactPath, tested.Receipt, _cts.Token);
            var checkpoint = await _checkpointV042Service.AcceptFromBootstrapAsync(
                checkpointCandidate, claim.Lease.LeaseId, _cts.Token);
            var checkpointPath = await LocalCheckpointV042Service.WriteReceiptAsync(
                WorkspaceRootBox.Text, checkpoint, _cts.Token);
            var completed = await _transitionBootstrapV040Service.FinalizeAcceptedAsync(
                claim, tested.ArtifactPath, checkpointPath, _cts.Token);

            _lastAcceptanceConsumed = true;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: v0.42 first-boot validation PASS + automatic local Accept; {checkpoint.Tag} -> {checkpoint.NewHead}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v042 completed lease={completed.LeaseId}; validated=true; accepted=true; publish=false; lifecycle=false");
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
                ManualSelfTestButtonAvailable = false,
                ManualAcceptButtonAvailable = false,
                StopButtonAvailable = false,
                VisibleTopLevelMaintenanceButtons = 5,
                WorkspaceAndCatalogFieldsVisible = false,
                InstalledAppsObservationOnly = true,
                AutomaticPublishPerformed = false,
                AutomaticLifecyclePerformed = false,
                NextExplicitActions = new[] { "Publish accepted", "Lifecycle receipt" }
            });
            OutputTabs.SelectedItem = AcceptanceTab;
            RefreshInstalledAppsV042();
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
            RefreshInstalledAppsV042();
        }
    }

    private async Task<(WorkbenchAcceptanceReceipt Receipt, string ArtifactPath)> RunV042AcceptanceArtifactAsync(CancellationToken cancellationToken)
    {
        var context = new RuntimeContext(CatalogRootBox.Text, true, false);
        var receipt = await new WorkbenchV042AcceptanceHarness(_acceptanceHarness).RunAsync(context, cancellationToken);
        var dir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"v0.42-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, CommandCodec.Serialize(receipt), new UTF8Encoding(false), cancellationToken);
        return (receipt, path);
    }

    private async void PublishAcceptedV042Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveSettings();
            var candidate = await _fixedGitHubPublicationV042Service.PreviewAsync(WorkspaceRootBox.Text, CancellationToken.None);
            var preview = $"Опубликовать принятый Workbench v0.42?\n\nRemote: {candidate.RemoteName}\nURL: {candidate.RemoteUrl}\nAccepted HEAD: {candidate.Head}\nParent: {candidate.Parent} / {FixedGitHubPublicationV042Service.ExpectedParentTag}\nTag: {candidate.AcceptedTag}\n\nUI-упрощение не создаёт publication authority. Только exact fast-forward/tag, без force/tag movement. Lifecycle остаётся отдельным действием.";
            if (MessageBox.Show(this, preview, "Publish accepted v0.42", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            SetV035PrimaryControlsEnabled(false);
            DisableLegacyManualControlsV042();
            BeginRun($"publish-v0.42-{DateTime.Now:yyyyMMddHHmmss}");
            StatusText.Text = "RUNNING: publish accepted v0.42 to fixed GitHub remote";
            var receipt = await _fixedGitHubPublicationV042Service.PublishAsync(candidate, _cts!.Token);
            var path = await FixedGitHubPublicationV042Service.WriteReceiptAsync(WorkspaceRootBox.Text, receipt, _cts.Token);
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Publication = receipt,
                PublicationReceiptPath = path,
                ManualValidationAuthorityCreated = false,
                InstalledAppsAuthorityCreated = false,
                StatusPresentationAuthorityCreated = false,
                NextExplicitAction = "Lifecycle receipt"
            });
            OutputTabs.SelectedItem = AcceptanceTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: remote main/v0.42 tag == {receipt.LocalHead}";
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally
        {
            EndRun();
            SetV035PrimaryControlsEnabled(true);
            DisableLegacyManualControlsV042();
            RefreshInstalledAppsV042();
        }
    }
}
