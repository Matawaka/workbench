using System.IO;
using System.Text;
using System.Windows;
using Matawaka.Workbench.Protocol;
using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalCheckpointV0491Service _checkpointV0491Service = new();
    private readonly FixedGitHubPublicationV0491Service _fixedGitHubPublicationV0491Service = new();
    private bool _v0491LoadedBootstrapChecked;

    internal void ConfigureV0491Routing()
    {
        ConfigureV049Routing();
        Title = "Matawaka Workbench v0.49.1";
        Loaded -= Window_LoadedV049;
        Loaded += Window_LoadedV0491;
        PublishAcceptedButton.Click -= PublishAcceptedV049Button_Click;
        PublishAcceptedButton.Click += PublishAcceptedV0491Button_Click;
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
    }

    private async void Window_LoadedV0491(object sender, RoutedEventArgs e)
    {
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
        if (_v0491LoadedBootstrapChecked) return;
        _v0491LoadedBootstrapChecked = true;
        TransitionBootstrapV040Claim? claim = null;
        var beganRun = false;
        try
        {
            claim = await _transitionBootstrapV040Service.TryClaimFirstBootAsync(
                WorkspaceRootBox.Text,
                LocalCheckpointV0491Service.Version,
                LocalCheckpointV0491Service.TargetTag,
                CancellationToken.None);
            if (claim is null)
            {
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v0491 none; automaticValidation=false; automaticAccept=false");
                return;
            }

            SetV035PrimaryControlsEnabled(false);
            OperatorSurfaceV045Contract.Apply(this);
            BeginRun($"first-boot-bootstrap-v0.49.1-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;
            StatusText.Text = $"RUNNING: v0.49.1 MCP runtime-closure validation; lease={claim.Lease.LeaseId}";
            var tested = await RunV0491AcceptanceArtifactAsync(_cts!.Token);
            _lastAcceptanceReceipt = tested.Receipt;
            _lastAcceptanceArtifactPath = tested.ArtifactPath;
            _lastAcceptanceConsumed = false;
            if (!tested.Receipt.Passed)
            {
                await _transitionBootstrapV040Service.MarkFailedNoRetryAsync(claim.Lease, claim.LeasePath, "v0.49.1 validation returned Passed=false", CancellationToken.None);
                _currentTerminalState = CommandTerminalState.Failed;
                StatusText.Text = "FAILED: v0.49.1 validation did not pass; automatic local Accept refused; no retry authority";
                AcceptanceTextBox.Text = CommandCodec.Serialize(new { Bootstrap = claim.Lease, Acceptance = tested.Receipt, tested.ArtifactPath, AutomaticAcceptPerformed = false });
                OutputTabs.SelectedItem = AcceptanceTab;
                return;
            }

            var checkpointCandidate = await _checkpointV0491Service.PreviewAsync(WorkspaceRootBox.Text, tested.ArtifactPath, tested.Receipt, _cts!.Token);
            var checkpoint = await _checkpointV0491Service.AcceptFromBootstrapAsync(checkpointCandidate, claim.Lease.LeaseId, _cts.Token);
            var checkpointPath = await LocalCheckpointV0491Service.WriteReceiptAsync(WorkspaceRootBox.Text, checkpoint, _cts.Token);
            var completed = await _transitionBootstrapV040Service.FinalizeAcceptedAsync(claim, tested.ArtifactPath, checkpointPath, _cts.Token);
            _lastAcceptanceConsumed = true;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: v0.49.1 validation PASS + automatic local Accept; {checkpoint.Tag} -> {checkpoint.NewHead}";
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Bootstrap = completed,
                Acceptance = tested.Receipt,
                AcceptanceArtifactPath = tested.ArtifactPath,
                Checkpoint = checkpoint,
                CheckpointReceiptPath = checkpointPath,
                FourButtonSurfacePreserved = true,
                FailedV049LocalPredecessorPreserved = true,
                FailedV049RemoteTagMustRemainAbsent = true,
                BaseDotNetTcpRuntime = true,
                MicrosoftAspNetCoreRuntimeDependency = false,
                OfficialMcpSdkPinnedForQualification = true,
                AdapterLeaseGated = true,
                AdapterLoopbackOnly = true,
                SecureMcpTunnelImplemented = false,
                AutomaticPublishPerformed = false,
                AutomaticLifecyclePerformed = false,
                NextExplicitActions = new[] { "Real-host Start -> Stop -> Revoke", "Publish accepted", "Lifecycle receipt" }
            });
            OutputTabs.SelectedItem = AcceptanceTab;
        }
        catch (OperationCanceledException ex) { if (claim is not null) await TryFailBootstrapAsync(claim.Lease, claim.LeasePath, ex.Message); ShowCancelled(); }
        catch (InvalidDataException ex) { if (claim is not null) await TryFailBootstrapAsync(claim.Lease, claim.LeasePath, ex.Message); ShowInvalid(ex); }
        catch (Exception ex) { if (claim is not null) await TryFailBootstrapAsync(claim.Lease, claim.LeasePath, ex.Message); ShowFailure(ex); }
        finally
        {
            if (beganRun) EndRun();
            SetV035PrimaryControlsEnabled(true);
            OperatorSurfaceV045Contract.Apply(this);
            RefreshInstalledAppsV044();
            InstallV0441TreeDoubleClickRouting();
        }
    }

    private async Task<(WorkbenchAcceptanceReceipt Receipt, string ArtifactPath)> RunV0491AcceptanceArtifactAsync(CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(this);
        var context = new RuntimeContext(CatalogRootBox.Text, true, false);
        var receipt = await new WorkbenchV0491AcceptanceHarness(_acceptanceHarness, this).RunAsync(context, cancellationToken);
        var dir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"v0.49.1-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, CommandCodec.Serialize(receipt), new UTF8Encoding(false), cancellationToken);
        return (receipt, path);
    }

    private async void PublishAcceptedV0491Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            OperatorSurfaceV045Contract.Apply(this);
            SaveSettings();
            if (_v049ActiveAdapterApplicationId is not null)
                throw new InvalidDataException($"Stop the active MCP adapter for {_v049ActiveAdapterApplicationId} before publishing accepted Workbench source.");
            var candidate = await _fixedGitHubPublicationV0491Service.PreviewAsync(WorkspaceRootBox.Text, CancellationToken.None);
            var preview = $"Опубликовать принятый Workbench v0.49.1?\n\nRemote: {candidate.RemoteName}\nAccepted HEAD: {candidate.Head}\nLocal failed-v0.49 parent: {candidate.Parent} / {FixedGitHubPublicationV0491Service.ExpectedParentTag}\nRemote base must remain: {FixedGitHubPublicationV0491Service.ExpectedRemoteBase} / {FixedGitHubPublicationV0491Service.ExpectedRemoteBaseTag}\nTarget tag: {candidate.AcceptedTag}\n\nYes только после успешной real-host проверки Start -> Stop -> Revoke. Failed workbench-v0.49-accepted tag не публикуется. Lease/bearer/private app data/endpoint tokens не входят в publication.";
            if (MessageBox.Show(this, preview, "Publish accepted v0.49.1", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            SetV035PrimaryControlsEnabled(false);
            OperatorSurfaceV045Contract.Apply(this);
            BeginRun($"publish-v0.49.1-{DateTime.Now:yyyyMMddHHmmss}");
            var receipt = await _fixedGitHubPublicationV0491Service.PublishAsync(candidate, _cts!.Token);
            var path = await FixedGitHubPublicationV0491Service.WriteReceiptAsync(WorkspaceRootBox.Text, receipt, _cts.Token);
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Publication = receipt,
                PublicationReceiptPath = path,
                FailedV049TagPublished = false,
                LeaseStatePublished = false,
                EndpointTokenPublished = false,
                PrivateAppBytesPublished = false,
                NextExplicitAction = "Lifecycle receipt"
            });
            OutputTabs.SelectedItem = AcceptanceTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: remote main/v0.49.1 tag == {receipt.LocalHead}; failed v0.49 tag remains absent";
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally
        {
            EndRun();
            SetV035PrimaryControlsEnabled(true);
            OperatorSurfaceV045Contract.Apply(this);
            RefreshInstalledAppsV044();
        }
    }
}
