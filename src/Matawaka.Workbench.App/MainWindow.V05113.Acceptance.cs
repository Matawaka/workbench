using System.IO;
using System.Text;
using System.Windows;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalCheckpointV05113Service _checkpointV05113Service = new();
    private bool _v05113LoadedBootstrapChecked;

    internal void ConfigureV05113AcceptanceRouting()
    {
        ConfigureV05112AcceptanceRouting();
        Loaded -= Window_LoadedV05112;
        Loaded += Window_LoadedV05113;
        PublishAcceptedButton.Click -= PublishAcceptedV05112Button_Click;
        PublishAcceptedButton.Click += PublishAcceptedV05113Button_Click;
    }

    private async void Window_LoadedV05113(object sender, RoutedEventArgs e)
    {
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
        if (_v05113LoadedBootstrapChecked) return;
        _v05113LoadedBootstrapChecked = true;

        TransitionBootstrapV040Claim? claim = null;
        var beganRun = false;
        try
        {
            claim = await _transitionBootstrapV040Service.TryClaimFirstBootAsync(
                WorkspaceRootBox.Text,
                LocalCheckpointV05113Service.Version,
                LocalCheckpointV05113Service.TargetTag,
                CancellationToken.None);
            if (claim is null)
            {
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v05113 none; automaticValidation=false; automaticAccept=false");
                return;
            }

            SetV035PrimaryControlsEnabled(false);
            OperatorSurfaceV045Contract.Apply(this);
            BeginRun($"first-boot-bootstrap-v0.51.13-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;
            StatusText.Text = $"RUNNING: v0.51.13 shutdown transaction validation; lease={claim.Lease.LeaseId}";

            var tested = await RunV05113AcceptanceArtifactAsync(_cts!.Token);
            _lastAcceptanceReceipt = tested.Receipt;
            _lastAcceptanceArtifactPath = tested.ArtifactPath;
            _lastAcceptanceConsumed = false;

            if (!tested.Receipt.Passed)
            {
                await _transitionBootstrapV040Service.MarkFailedNoRetryAsync(
                    claim.Lease, claim.LeasePath, "v0.51.13 validation returned Passed=false", CancellationToken.None);
                _currentTerminalState = CommandTerminalState.Failed;
                StatusText.Text = "FAILED: v0.51.13 shutdown validation did not pass; automatic local Accept refused; no retry authority";
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

            var candidate = await _checkpointV05113Service.PreviewAsync(
                WorkspaceRootBox.Text, tested.ArtifactPath, tested.Receipt, _cts.Token);
            var checkpoint = await _checkpointV05113Service.AcceptFromBootstrapAsync(
                candidate, claim.Lease.LeaseId, _cts.Token);
            var checkpointPath = await LocalCheckpointV05113Service.WriteReceiptAsync(
                WorkspaceRootBox.Text, checkpoint, _cts.Token);
            var completed = await _transitionBootstrapV040Service.FinalizeAcceptedAsync(
                claim, tested.ArtifactPath, checkpointPath, _cts.Token);

            _lastAcceptanceConsumed = true;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: v0.51.13 validation PASS + automatic local Accept; {checkpoint.Tag} -> {checkpoint.NewHead}";
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Bootstrap = completed,
                Acceptance = tested.Receipt,
                AcceptanceArtifactPath = tested.ArtifactPath,
                Checkpoint = checkpoint,
                CheckpointReceiptPath = checkpointPath,
                V05112ListenerReadinessPreserved = true,
                ShutdownTransactionAdded = true,
                ShutdownStateOrdering = new[]
                {
                    "LISTENER_READY",
                    "SHUTDOWN_PREPARED",
                    "LISTENER_STOPPED",
                    "OWNER_RELEASED",
                    "LEASE_REVOKED_OR_ALREADY_TERMINAL",
                    "SHUTDOWN_COMPLETED"
                },
                ShutdownPreparedClaimsListenerStopped = false,
                ListenerStoppedEqualsOwnerReleased = false,
                OwnerReleasedEqualsLeaseRevoked = false,
                ShutdownRecoveryAutoStartsOrResumesListener = false,
                ShutdownRecoveryAutoRevokesLease = false,
                OwnerReleasedLiveLeaseBlocksSilentSuccessor = true,
                SiblingLeaseRevocationAllowed = false,
                HistoricalCanonicalScanPerformedByShutdownTransaction = false,
                ShutdownTransactionGrantedAuthority = false,
                KONTURIntegrationAnchorsArePlanningOnly = true,
                AutomaticPublishPerformed = false,
                AutomaticLifecyclePerformed = false,
                PublicRemotePublicationStillDeferred = true,
                NextExplicitActions = new[]
                {
                    "Start one normal v0.51.12-style read session and require LISTENER_READY",
                    "End it through v0.51.13 and require SHUTDOWN_PREPARED -> LISTENER_STOPPED -> OWNER_RELEASED -> LEASE_REVOKED -> SHUTDOWN_COMPLETED",
                    "Require final coherent live status live=0/orphan=0"
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

    private async Task<(WorkbenchAcceptanceReceipt Receipt, string ArtifactPath)> RunV05113AcceptanceArtifactAsync(
        CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(this);
        var context = new RuntimeContext(CatalogRootBox.Text, true, false);
        var receipt = await new WorkbenchV05113AcceptanceHarness(_acceptanceHarness, this).RunAsync(context, cancellationToken);
        var dir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"v0.51.13-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, CommandCodec.Serialize(receipt), new UTF8Encoding(false), cancellationToken);
        return (receipt, path);
    }

    private void PublishAcceptedV05113Button_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "Remote publication is intentionally deferred.\n\nLocal v0.51.13 adds reverse shutdown transaction semantics over the locally accepted v0.51.12 frontier. Public main remains on v0.50.2 while external bridge admission is paused.\n\nNo GitHub mutation was performed.",
            "Publish accepted v0.51.13 — deferred",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  publish.v05113.deferred effect=false; reason=public-v051-gate-unresolved");
    }
}
