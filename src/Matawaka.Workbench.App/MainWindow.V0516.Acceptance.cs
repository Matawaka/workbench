using System.IO;
using System.Text;
using System.Windows;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalCheckpointV0516Service _checkpointV0516Service = new();
    private bool _v0516LoadedBootstrapChecked;

    internal void ConfigureV0516AcceptanceRouting()
    {
        Loaded -= Window_LoadedV0515;
        Loaded += Window_LoadedV0516;

        PublishAcceptedButton.Click -= PublishAcceptedV0515Button_Click;
        PublishAcceptedButton.Click += PublishAcceptedV0516Button_Click;
    }

    private async void Window_LoadedV0516(object sender, RoutedEventArgs e)
    {
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();

        if (_v0516LoadedBootstrapChecked) return;
        _v0516LoadedBootstrapChecked = true;

        TransitionBootstrapV040Claim? claim = null;
        var beganRun = false;
        try
        {
            claim = await _transitionBootstrapV040Service.TryClaimFirstBootAsync(
                WorkspaceRootBox.Text,
                LocalCheckpointV0516Service.Version,
                LocalCheckpointV0516Service.TargetTag,
                CancellationToken.None);

            if (claim is null)
            {
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v0516 none; automaticValidation=false; automaticAccept=false");
                return;
            }

            SetV035PrimaryControlsEnabled(false);
            OperatorSurfaceV045Contract.Apply(this);
            BeginRun($"first-boot-bootstrap-v0.51.6-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;
            StatusText.Text = $"RUNNING: v0.51.6 cross-process active-index fence validation; lease={claim.Lease.LeaseId}";

            var tested = await RunV0516AcceptanceArtifactAsync(_cts!.Token);
            _lastAcceptanceReceipt = tested.Receipt;
            _lastAcceptanceArtifactPath = tested.ArtifactPath;
            _lastAcceptanceConsumed = false;

            if (!tested.Receipt.Passed)
            {
                await _transitionBootstrapV040Service.MarkFailedNoRetryAsync(
                    claim.Lease, claim.LeasePath,
                    "v0.51.6 validation returned Passed=false",
                    CancellationToken.None);
                _currentTerminalState = CommandTerminalState.Failed;
                StatusText.Text = "FAILED: v0.51.6 validation did not pass; automatic local Accept refused; no retry authority";
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

            var checkpointCandidate = await _checkpointV0516Service.PreviewAsync(
                WorkspaceRootBox.Text, tested.ArtifactPath, tested.Receipt, _cts.Token);
            var checkpoint = await _checkpointV0516Service.AcceptFromBootstrapAsync(
                checkpointCandidate, claim.Lease.LeaseId, _cts.Token);
            var checkpointPath = await LocalCheckpointV0516Service.WriteReceiptAsync(
                WorkspaceRootBox.Text, checkpoint, _cts.Token);
            var completed = await _transitionBootstrapV040Service.FinalizeAcceptedAsync(
                claim, tested.ArtifactPath, checkpointPath, _cts.Token);

            _lastAcceptanceConsumed = true;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: v0.51.6 validation PASS + automatic local Accept; {checkpoint.Tag} -> {checkpoint.NewHead}";

            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Bootstrap = completed,
                Acceptance = tested.Receipt,
                AcceptanceArtifactPath = tested.ArtifactPath,
                Checkpoint = checkpoint,
                CheckpointReceiptPath = checkpointPath,
                FourButtonSurfacePreserved = true,
                V051BrowseAndReadPreserved = true,
                V0511ReadLeaseAutoMcpPreserved = true,
                V0512ExactBoundSessionClosurePreserved = true,
                V0513BearerFreeStatusAndOrphanClosurePreserved = true,
                V0514BoundedHistoricalPaginationPreserved = true,
                V0515VerifiedActiveIndexPreserved = true,
                CanonicalLeaseStateRemainsAuthority = true,
                ActiveIndexDerivedOnly = true,
                CrossProcessFenceSerializationOnly = true,
                CrossProcessFenceDefaultTimeoutMs = LocalAppActiveIndexFenceV0516Service.DefaultTimeoutMilliseconds,
                FastStatusRequiresRevisionAndDirtyPostCheck = true,
                FenceStoresBearerPlaintext = false,
                FenceStoresBearerHash = false,
                HistoricalEvidenceDeletionOrCompaction = false,
                AutomaticFenceReconciliationPerformedAtFirstBoot = false,
                AutomaticRevocationPerformed = false,
                AutomaticSecureMcpTunnelPerformed = false,
                AutomaticPublishPerformed = false,
                AutomaticLifecyclePerformed = false,
                PublicRemotePublicationStillDeferred = true,
                NextExplicitActions = new[]
                {
                    "Use Read Session Status and verify CrossProcessFenceAcquired=true plus SnapshotCoherent=true",
                    "Treat ACTIVE_INDEX_FENCE_BUSY and ACTIVE_INDEX_SNAPSHOT_CHANGED as fail-closed no-result conditions",
                    "If a crash leaves the v0.51.5 dirty marker, explicitly reconcile before using indexed authority",
                    "Keep canonical lease evidence and v0.51.5 derived index semantics unchanged"
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

    private async Task<(WorkbenchAcceptanceReceipt Receipt, string ArtifactPath)> RunV0516AcceptanceArtifactAsync(
        CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(this);
        var context = new RuntimeContext(CatalogRootBox.Text, true, false);
        var receipt = await new WorkbenchV0516AcceptanceHarness(_acceptanceHarness, this).RunAsync(context, cancellationToken);
        var dir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"v0.51.6-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, CommandCodec.Serialize(receipt), new UTF8Encoding(false), cancellationToken);
        return (receipt, path);
    }

    private void PublishAcceptedV0516Button_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "Remote publication is intentionally deferred.\n\n" +
            "Local v0.51.6 may be accepted and used for cross-process coherent verified-index authority status, but public main remains on v0.50.2 while the external bridge admission is paused.\n\n" +
            "No GitHub mutation was performed.",
            "Publish accepted v0.51.6 — deferred",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  publish.v0516.deferred effect=false; reason=public-v051-gate-unresolved");
    }
}
