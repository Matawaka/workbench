using System.IO;
using System.Text;
using System.Windows;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalCheckpointV0518Service _checkpointV0518Service = new();
    private bool _v0518LoadedBootstrapChecked;

    internal void ConfigureV0518AcceptanceRouting()
    {
        ConfigureV0517AcceptanceRouting();
        Loaded -= Window_LoadedV0517;
        Loaded += Window_LoadedV0518;

        PublishAcceptedButton.Click -= PublishAcceptedV0517Button_Click;
        PublishAcceptedButton.Click += PublishAcceptedV0518Button_Click;
    }

    private async void Window_LoadedV0518(object sender, RoutedEventArgs e)
    {
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();

        if (_v0518LoadedBootstrapChecked) return;
        _v0518LoadedBootstrapChecked = true;

        TransitionBootstrapV040Claim? claim = null;
        var beganRun = false;
        try
        {
            claim = await _transitionBootstrapV040Service.TryClaimFirstBootAsync(
                WorkspaceRootBox.Text,
                LocalCheckpointV0518Service.Version,
                LocalCheckpointV0518Service.TargetTag,
                CancellationToken.None);

            if (claim is null)
            {
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v0518 none; automaticValidation=false; automaticAccept=false");
                return;
            }

            SetV035PrimaryControlsEnabled(false);
            OperatorSurfaceV045Contract.Apply(this);
            BeginRun($"first-boot-bootstrap-v0.51.8-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;
            StatusText.Text = $"RUNNING: v0.51.8 MCP ownership status + stale metadata recovery validation; lease={claim.Lease.LeaseId}";

            var tested = await RunV0518AcceptanceArtifactAsync(_cts!.Token);
            _lastAcceptanceReceipt = tested.Receipt;
            _lastAcceptanceArtifactPath = tested.ArtifactPath;
            _lastAcceptanceConsumed = false;

            if (!tested.Receipt.Passed)
            {
                await _transitionBootstrapV040Service.MarkFailedNoRetryAsync(
                    claim.Lease, claim.LeasePath,
                    "v0.51.8 validation returned Passed=false",
                    CancellationToken.None);
                _currentTerminalState = CommandTerminalState.Failed;
                StatusText.Text = "FAILED: v0.51.8 validation did not pass; automatic local Accept refused; no retry authority";
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

            var checkpointCandidate = await _checkpointV0518Service.PreviewAsync(
                WorkspaceRootBox.Text, tested.ArtifactPath, tested.Receipt, _cts.Token);
            var checkpoint = await _checkpointV0518Service.AcceptFromBootstrapAsync(
                checkpointCandidate, claim.Lease.LeaseId, _cts.Token);
            var checkpointPath = await LocalCheckpointV0518Service.WriteReceiptAsync(
                WorkspaceRootBox.Text, checkpoint, _cts.Token);
            var completed = await _transitionBootstrapV040Service.FinalizeAcceptedAsync(
                claim, tested.ArtifactPath, checkpointPath, _cts.Token);

            _lastAcceptanceConsumed = true;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: v0.51.8 validation PASS + automatic local Accept; {checkpoint.Tag} -> {checkpoint.NewHead}";

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
                V0516CrossProcessActiveIndexFencePreserved = true,
                V0517CrossProcessMcpOwnershipPreserved = true,
                CanonicalLeaseStateRemainsAuthority = true,
                ActiveIndexDerivedOnly = true,
                OwnerMetadataRemainsNonAuthoritative = true,
                OwnershipStatusStates = new[] { "OWNED", "FREE_NO_METADATA", "FREE_STALE_METADATA" },
                OwnershipStatusCreatesOwnerLock = false,
                OwnershipStatusPerformsHistoricalLeaseScan = false,
                OwnershipStatusMutatesCanonicalLease = false,
                StaleMetadataAcknowledgementExplicitOnly = true,
                StaleMetadataAcknowledgementRequiresFreeOwnerDomain = true,
                StaleMetadataEvidencePreserved = true,
                StaleMetadataAcknowledgementRevokesLease = false,
                StaleMetadataAcknowledgementResumesMcp = false,
                LiveOrphanClosureRemainsSeparateExplicitAction = true,
                StatusOrMetadataGrantsResumeAuthority = false,
                StatusOrMetadataGrantsRevokeAuthority = false,
                BearerPlaintextDisclosed = false,
                BearerHashDisclosed = false,
                EndpointPathTokenDisclosed = false,
                HistoricalEvidenceDeletionOrCompaction = false,
                AutomaticStaleMetadataAcknowledgementPerformedAtFirstBoot = false,
                AutomaticOwnerRecoveryRestartPerformedAtFirstBoot = false,
                AutomaticLeaseRevocationPerformed = false,
                AutomaticSecureMcpTunnelPerformed = false,
                AutomaticPublishPerformed = false,
                AutomaticLifecyclePerformed = false,
                PublicRemotePublicationStillDeferred = true,
                NextExplicitActions = new[]
                {
                    "Use MCP Ownership Status to distinguish live owner handle from stale/nonexistent metadata without changing lease/index authority",
                    "After a crash, treat FREE_STALE_METADATA + LIVE_ORPHAN as observation only; exact orphan closure remains separate",
                    "Use Acknowledge stale MCP owner metadata only when evidence rotation is desired; it must preserve archive evidence and leave canonical lease authority unchanged",
                    "Never treat FREE_NO_METADATA, stale metadata or archived owner evidence as authorization to create, resume or revoke a read session"
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

    private async Task<(WorkbenchAcceptanceReceipt Receipt, string ArtifactPath)> RunV0518AcceptanceArtifactAsync(
        CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(this);
        var context = new RuntimeContext(CatalogRootBox.Text, true, false);
        var receipt = await new WorkbenchV0518AcceptanceHarness(_acceptanceHarness, this).RunAsync(context, cancellationToken);
        var dir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"v0.51.8-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, CommandCodec.Serialize(receipt), new UTF8Encoding(false), cancellationToken);
        return (receipt, path);
    }

    private void PublishAcceptedV0518Button_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "Remote publication is intentionally deferred.\n\n" +
            "Local v0.51.8 may be accepted and used for MCP ownership observation and stale metadata evidence recovery, but public main remains on v0.50.2 while the external bridge admission is paused.\n\n" +
            "No GitHub mutation was performed.",
            "Publish accepted v0.51.8 — deferred",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  publish.v0518.deferred effect=false; reason=public-v051-gate-unresolved");
    }
}
