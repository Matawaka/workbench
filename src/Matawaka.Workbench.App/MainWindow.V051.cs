using System.IO;
using System.Text;
using System.Windows;
using Matawaka.Workbench.Protocol;
using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalCheckpointV051Service _checkpointV051Service = new();
    private readonly FixedGitHubPublicationV051Service _fixedGitHubPublicationV051Service = new();
    private bool _v051LoadedBootstrapChecked;

    internal void ConfigureV051Routing()
    {
        ConfigureV0502Routing();
        Title = "Matawaka Workbench v0.51";
        Loaded -= Window_LoadedV0502;
        Loaded += Window_LoadedV051;
        PublishAcceptedButton.Click -= PublishAcceptedV0502Button_Click;
        PublishAcceptedButton.Click += PublishAcceptedV051Button_Click;
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
    }

    private async void Window_LoadedV051(object sender, RoutedEventArgs e)
    {
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
        if (_v051LoadedBootstrapChecked) return;
        _v051LoadedBootstrapChecked = true;
        TransitionBootstrapV040Claim? claim = null;
        var beganRun = false;
        try
        {
            claim = await _transitionBootstrapV040Service.TryClaimFirstBootAsync(WorkspaceRootBox.Text, LocalCheckpointV051Service.Version, LocalCheckpointV051Service.TargetTag, CancellationToken.None);
            if (claim is null)
            {
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v051 none; automaticValidation=false; automaticAccept=false");
                return;
            }
            SetV035PrimaryControlsEnabled(false);
            OperatorSurfaceV045Contract.Apply(this);
            BeginRun($"first-boot-bootstrap-v0.51-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;
            StatusText.Text = $"RUNNING: v0.51 lease-gated live app browse validation; lease={claim.Lease.LeaseId}";
            var tested = await RunV051AcceptanceArtifactAsync(_cts!.Token);
            _lastAcceptanceReceipt = tested.Receipt;
            _lastAcceptanceArtifactPath = tested.ArtifactPath;
            _lastAcceptanceConsumed = false;
            if (!tested.Receipt.Passed)
            {
                await _transitionBootstrapV040Service.MarkFailedNoRetryAsync(claim.Lease, claim.LeasePath, "v0.51 validation returned Passed=false", CancellationToken.None);
                _currentTerminalState = CommandTerminalState.Failed;
                StatusText.Text = "FAILED: v0.51 validation did not pass; automatic local Accept refused; no retry authority";
                AcceptanceTextBox.Text = CommandCodec.Serialize(new { Bootstrap = claim.Lease, Acceptance = tested.Receipt, tested.ArtifactPath, AutomaticAcceptPerformed = false });
                OutputTabs.SelectedItem = AcceptanceTab;
                return;
            }
            var checkpointCandidate = await _checkpointV051Service.PreviewAsync(WorkspaceRootBox.Text, tested.ArtifactPath, tested.Receipt, _cts.Token);
            var checkpoint = await _checkpointV051Service.AcceptFromBootstrapAsync(checkpointCandidate, claim.Lease.LeaseId, _cts.Token);
            var checkpointPath = await LocalCheckpointV051Service.WriteReceiptAsync(WorkspaceRootBox.Text, checkpoint, _cts.Token);
            var completed = await _transitionBootstrapV040Service.FinalizeAcceptedAsync(claim, tested.ArtifactPath, checkpointPath, _cts.Token);
            _lastAcceptanceConsumed = true;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: v0.51 validation PASS + automatic local Accept; {checkpoint.Tag} -> {checkpoint.NewHead}";
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Bootstrap = completed,
                Acceptance = tested.Receipt,
                AcceptanceArtifactPath = tested.ArtifactPath,
                Checkpoint = checkpoint,
                CheckpointReceiptPath = checkpointPath,
                FourButtonSurfacePreserved = true,
                TwoToolMcpSurface = true,
                DirectoryPrefixBrowseOnly = true,
                ExactFileSiblingEnumerationRefused = true,
                ApplicationRootBrowseRefused = true,
                BrowseConsumesSameLeaseBudgets = true,
                ExistingReadToolPreserved = true,
                V0502TunnelCompatibilityPreserved = true,
                AutomaticPublishPerformed = false,
                AutomaticLifecyclePerformed = false,
                NextExplicitActions = new[] { "Fresh directory-prefix read lease", "Start local MCP", "Start Secure MCP Tunnel", "Refresh ChatGPT plugin tools if needed", "ChatGPT list + read round-trip", "Tunnel Stop -> MCP Stop -> Lease Revoke", "Publish accepted", "Lifecycle receipt" }
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

    private async Task<(WorkbenchAcceptanceReceipt Receipt, string ArtifactPath)> RunV051AcceptanceArtifactAsync(CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(this);
        var context = new RuntimeContext(CatalogRootBox.Text, true, false);
        var receipt = await new WorkbenchV051AcceptanceHarness(_acceptanceHarness, this).RunAsync(context, cancellationToken);
        var dir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"v0.51-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, CommandCodec.Serialize(receipt), new UTF8Encoding(false), cancellationToken);
        return (receipt, path);
    }

    private async void PublishAcceptedV051Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            OperatorSurfaceV045Contract.Apply(this);
            SaveSettings();
            if (_v050ActiveTunnelApplicationId is not null && _secureMcpTunnelV0501Service.IsActiveFor(_v050ActiveTunnelApplicationId)) throw new InvalidDataException("Stop the active Secure MCP Tunnel before publishing accepted Workbench source.");
            if (_v049ActiveAdapterApplicationId is not null) throw new InvalidDataException("Stop the active MCP adapter before publishing accepted Workbench source.");
            var candidate = await _fixedGitHubPublicationV051Service.PreviewAsync(WorkspaceRootBox.Text, CancellationToken.None);
            var preview = $"Опубликовать принятый Workbench v0.51?\n\nRemote: {candidate.RemoteName}\nAccepted HEAD: {candidate.Head}\nParent/base: {candidate.Parent} / {FixedGitHubPublicationV051Service.ExpectedParentTag}\nTarget tag: {candidate.AcceptedTag}\n\nYes только после успешной real-host цепочки: directory-prefix lease -> MCP -> Secure Tunnel -> ChatGPT list_local_app_entries -> read_local_app_chunk -> Tunnel Stop -> MCP Stop -> Lease Revoke.";
            if (MessageBox.Show(this, preview, "Publish accepted v0.51", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            SetV035PrimaryControlsEnabled(false);
            OperatorSurfaceV045Contract.Apply(this);
            BeginRun($"publish-v0.51-{DateTime.Now:yyyyMMddHHmmss}");
            var receipt = await _fixedGitHubPublicationV051Service.PublishAsync(candidate, _cts!.Token);
            var path = await FixedGitHubPublicationV051Service.WriteReceiptAsync(WorkspaceRootBox.Text, receipt, _cts.Token);
            AcceptanceTextBox.Text = CommandCodec.Serialize(new { Publication = receipt, PublicationReceiptPath = path, BrowseRealHostRequiredBeforeConfirmation = true, PrivateAppBytesPublished = false, NextExplicitAction = "Lifecycle receipt" });
            OutputTabs.SelectedItem = AcceptanceTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: remote main/v0.51 tag == {receipt.LocalHead}";
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally { EndRun(); SetV035PrimaryControlsEnabled(true); OperatorSurfaceV045Contract.Apply(this); RefreshInstalledAppsV044(); }
    }
}
