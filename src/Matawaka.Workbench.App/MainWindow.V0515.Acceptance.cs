using System.IO;
using System.Text;
using System.Windows;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalCheckpointV0515Service _checkpointV0515Service = new();
    private bool _v0515LoadedBootstrapChecked;

    internal void ConfigureV0515AcceptanceRouting()
    {
        Loaded -= Window_LoadedV0514;
        Loaded += Window_LoadedV0515;

        PublishAcceptedButton.Click -= PublishAcceptedV0514Button_Click;
        PublishAcceptedButton.Click += PublishAcceptedV0515Button_Click;
    }

    private async void Window_LoadedV0515(object sender, RoutedEventArgs e)
    {
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();

        if (_v0515LoadedBootstrapChecked) return;
        _v0515LoadedBootstrapChecked = true;

        TransitionBootstrapV040Claim? claim = null;
        var beganRun = false;
        try
        {
            claim = await _transitionBootstrapV040Service.TryClaimFirstBootAsync(
                WorkspaceRootBox.Text,
                LocalCheckpointV0515Service.Version,
                LocalCheckpointV0515Service.TargetTag,
                CancellationToken.None);

            if (claim is null)
            {
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v0515 none; automaticValidation=false; automaticAccept=false");
                return;
            }

            SetV035PrimaryControlsEnabled(false);
            OperatorSurfaceV045Contract.Apply(this);
            BeginRun($"first-boot-bootstrap-v0.51.5-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;
            StatusText.Text = $"RUNNING: v0.51.5 verified active lease index validation; lease={claim.Lease.LeaseId}";

            var tested = await RunV0515AcceptanceArtifactAsync(_cts!.Token);
            _lastAcceptanceReceipt = tested.Receipt;
            _lastAcceptanceArtifactPath = tested.ArtifactPath;
            _lastAcceptanceConsumed = false;

            if (!tested.Receipt.Passed)
            {
                await _transitionBootstrapV040Service.MarkFailedNoRetryAsync(
                    claim.Lease, claim.LeasePath,
                    "v0.51.5 validation returned Passed=false",
                    CancellationToken.None);
                _currentTerminalState = CommandTerminalState.Failed;
                StatusText.Text = "FAILED: v0.51.5 validation did not pass; automatic local Accept refused; no retry authority";
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

            var checkpointCandidate = await _checkpointV0515Service.PreviewAsync(
                WorkspaceRootBox.Text, tested.ArtifactPath, tested.Receipt, _cts.Token);
            var checkpoint = await _checkpointV0515Service.AcceptFromBootstrapAsync(
                checkpointCandidate, claim.Lease.LeaseId, _cts.Token);
            var checkpointPath = await LocalCheckpointV0515Service.WriteReceiptAsync(
                WorkspaceRootBox.Text, checkpoint, _cts.Token);
            var completed = await _transitionBootstrapV040Service.FinalizeAcceptedAsync(
                claim, tested.ArtifactPath, checkpointPath, _cts.Token);

            _lastAcceptanceConsumed = true;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: v0.51.5 validation PASS + automatic local Accept; {checkpoint.Tag} -> {checkpoint.NewHead}";

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
                CanonicalLeaseStateRemainsAuthority = true,
                ActiveIndexDerivedOnly = true,
                ActiveIndexStoresBearerPlaintext = false,
                ActiveIndexStoresBearerHash = false,
                ActiveIndexReconciliationHardCeiling = LocalAppActiveLeaseIndexV0515Service.MaxReconciliationStateFiles,
                LiveAuthorityHardLimit = LocalAppActiveLeaseIndexV0515Service.LiveAuthorityHardLimit,
                FastStatusCanonicalHistoricalScanPerformed = false,
                HistoricalEvidenceDeletionOrCompaction = false,
                AutomaticReconciliationPerformedAtFirstBoot = false,
                AutomaticRevocationPerformed = false,
                AutomaticSecureMcpTunnelPerformed = false,
                AutomaticPublishPerformed = false,
                AutomaticLifecyclePerformed = false,
                PublicRemotePublicationStillDeferred = true,
                NextExplicitActions = new[]
                {
                    "On first Read Session Status use, explicitly approve bounded active-index reconciliation for the selected app",
                    "Use fast verified live-authority status without historical enumeration after reconciliation",
                    "Use Read Session History Page only when canonical historical evidence is needed",
                    "Treat ACTIVE_INDEX_RECONCILIATION_REQUIRED, ACTIVE_INDEX_INCONSISTENT and LIVE_AUTHORITY_OVERFLOW as fail-closed conditions"
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

    private async Task<(WorkbenchAcceptanceReceipt Receipt, string ArtifactPath)> RunV0515AcceptanceArtifactAsync(
        CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(this);
        var context = new RuntimeContext(CatalogRootBox.Text, true, false);
        var receipt = await new WorkbenchV0515AcceptanceHarness(_acceptanceHarness, this).RunAsync(context, cancellationToken);
        var dir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"v0.51.5-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, CommandCodec.Serialize(receipt), new UTF8Encoding(false), cancellationToken);
        return (receipt, path);
    }

    private void PublishAcceptedV0515Button_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "Remote publication is intentionally deferred.\n\n" +
            "Local v0.51.5 may be accepted and used for verified indexed live-authority status, but public main remains on v0.50.2 while the external bridge admission is paused.\n\n" +
            "No GitHub mutation was performed.",
            "Publish accepted v0.51.5 — deferred",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  publish.v0515.deferred effect=false; reason=public-v051-gate-unresolved");
    }
}
