using System.IO;
using System.Text;
using System.Windows;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalCheckpointV051111Service _checkpointV051111Service = new();
    private bool _v051111LoadedBootstrapChecked;

    internal void ConfigureV051111AcceptanceRouting()
    {
        ConfigureV05111AcceptanceRouting();
        Loaded -= Window_LoadedV05111;
        Loaded += Window_LoadedV051111;
        PublishAcceptedButton.Click -= PublishAcceptedV05111Button_Click;
        PublishAcceptedButton.Click += PublishAcceptedV051111Button_Click;
    }

    private async void Window_LoadedV051111(object sender, RoutedEventArgs e)
    {
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
        if (_v051111LoadedBootstrapChecked) return;
        _v051111LoadedBootstrapChecked = true;

        TransitionBootstrapV040Claim? claim = null;
        var beganRun = false;
        try
        {
            claim = await _transitionBootstrapV040Service.TryClaimFirstBootAsync(
                WorkspaceRootBox.Text,
                LocalCheckpointV051111Service.Version,
                LocalCheckpointV051111Service.TargetTag,
                CancellationToken.None);
            if (claim is null)
            {
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v051111 none; automaticValidation=false; automaticAccept=false");
                return;
            }

            SetV035PrimaryControlsEnabled(false);
            OperatorSurfaceV045Contract.Apply(this);
            BeginRun($"first-boot-bootstrap-v0.51.11.1-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;
            StatusText.Text = $"RUNNING: v0.51.11.1 exclusive Local apps routing validation; lease={claim.Lease.LeaseId}";

            var tested = await RunV051111AcceptanceArtifactAsync(_cts!.Token);
            _lastAcceptanceReceipt = tested.Receipt;
            _lastAcceptanceArtifactPath = tested.ArtifactPath;
            _lastAcceptanceConsumed = false;

            if (!tested.Receipt.Passed)
            {
                await _transitionBootstrapV040Service.MarkFailedNoRetryAsync(
                    claim.Lease,
                    claim.LeasePath,
                    "v0.51.11.1 validation returned Passed=false",
                    CancellationToken.None);
                _currentTerminalState = CommandTerminalState.Failed;
                StatusText.Text = "FAILED: v0.51.11.1 routing validation did not pass; automatic local Accept refused; no retry authority";
                AcceptanceTextBox.Text = CommandCodec.Serialize(new
                {
                    Bootstrap = claim.Lease,
                    Acceptance = tested.Receipt,
                    tested.ArtifactPath,
                    AutomaticAcceptPerformed = false
                });
                OutputTabs.SelectedItem = AcceptanceTab;
                return;
            }

            var candidate = await _checkpointV051111Service.PreviewAsync(
                WorkspaceRootBox.Text,
                tested.ArtifactPath,
                tested.Receipt,
                _cts.Token);
            var checkpoint = await _checkpointV051111Service.AcceptFromBootstrapAsync(
                candidate,
                claim.Lease.LeaseId,
                _cts.Token);
            var checkpointPath = await LocalCheckpointV051111Service.WriteReceiptAsync(
                WorkspaceRootBox.Text,
                checkpoint,
                _cts.Token);
            var completed = await _transitionBootstrapV040Service.FinalizeAcceptedAsync(
                claim,
                tested.ArtifactPath,
                checkpointPath,
                _cts.Token);

            _lastAcceptanceConsumed = true;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: v0.51.11.1 routing validation PASS + automatic local Accept; {checkpoint.Tag} -> {checkpoint.NewHead}";
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Bootstrap = completed,
                Acceptance = tested.Receipt,
                AcceptanceArtifactPath = tested.ArtifactPath,
                Checkpoint = checkpoint,
                CheckpointReceiptPath = checkpointPath,
                ExclusiveLocalAppsRouting = true,
                InheritedV0518HandlerDetached = true,
                DirectV05111HandlerReplacedByHotfixWrapper = true,
                ReadSessionLeaseTarget = "CreateOwnedReadLeaseAndAutoStartMcpV05111Async",
                V05111OwnerLeaseTransactionPreserved = true,
                CanonicalLeaseAuthoritySemanticsUnchanged = true,
                AutomaticPublishPerformed = false,
                AutomaticLifecyclePerformed = false,
                PublicRemotePublicationStillDeferred = true,
                NextExplicitActions = new[]
                {
                    "After any legacy v0.51.11 session has expired/stopped, start one normal read session and require local-app.v051111.dispatch exclusive=true before the v0.51.11 owner-to-lease transaction result",
                    "Require final result OWNER_LEASE_BOUND_INDEXED_READ_LEASE_AND_LOCAL_MCP_READY, never the legacy OWNED_INDEXED_READ_LEASE_AND_LOCAL_MCP_READY"
                }
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
            OperatorSurfaceV045Contract.Apply(this);
            RefreshInstalledAppsV044();
            InstallV0441TreeDoubleClickRouting();
        }
    }

    private async Task<(WorkbenchAcceptanceReceipt Receipt, string ArtifactPath)> RunV051111AcceptanceArtifactAsync(
        CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(this);
        var context = new RuntimeContext(CatalogRootBox.Text, true, false);
        var receipt = await new WorkbenchV051111AcceptanceHarness(_acceptanceHarness, this).RunAsync(context, cancellationToken);
        var dir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"v0.51.11.1-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, CommandCodec.Serialize(receipt), new UTF8Encoding(false), cancellationToken);
        return (receipt, path);
    }

    private void PublishAcceptedV051111Button_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "Remote publication is intentionally deferred.\n\nLocal v0.51.11.1 is a routing hotfix over locally accepted v0.51.11; public main remains on v0.50.2 while external bridge admission is paused.\n\nNo GitHub mutation was performed.",
            "Publish accepted v0.51.11.1 — deferred",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  publish.v051111.deferred effect=false; reason=public-v051-gate-unresolved");
    }
}
