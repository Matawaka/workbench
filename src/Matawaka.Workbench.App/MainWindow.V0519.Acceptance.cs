using System.IO;
using System.Text;
using System.Windows;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalCheckpointV0519Service _checkpointV0519Service = new();
    private bool _v0519LoadedBootstrapChecked;

    internal void ConfigureV0519AcceptanceRouting()
    {
        ConfigureV0518AcceptanceRouting();
        Loaded -= Window_LoadedV0518;
        Loaded += Window_LoadedV0519;
        PublishAcceptedButton.Click -= PublishAcceptedV0518Button_Click;
        PublishAcceptedButton.Click += PublishAcceptedV0519Button_Click;
    }

    private async void Window_LoadedV0519(object sender, RoutedEventArgs e)
    {
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
        if (_v0519LoadedBootstrapChecked) return;
        _v0519LoadedBootstrapChecked = true;

        TransitionBootstrapV040Claim? claim = null;
        var beganRun = false;
        try
        {
            claim = await _transitionBootstrapV040Service.TryClaimFirstBootAsync(
                WorkspaceRootBox.Text,
                LocalCheckpointV0519Service.Version,
                LocalCheckpointV0519Service.TargetTag,
                CancellationToken.None);
            if (claim is null)
            {
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v0519 none; automaticValidation=false; automaticAccept=false");
                return;
            }

            SetV035PrimaryControlsEnabled(false);
            OperatorSurfaceV045Contract.Apply(this);
            BeginRun($"first-boot-bootstrap-v0.51.9-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;
            StatusText.Text = $"RUNNING: v0.51.9 MCP owner generation continuity validation; lease={claim.Lease.LeaseId}";

            var tested = await RunV0519AcceptanceArtifactAsync(_cts!.Token);
            _lastAcceptanceReceipt = tested.Receipt;
            _lastAcceptanceArtifactPath = tested.ArtifactPath;
            _lastAcceptanceConsumed = false;
            if (!tested.Receipt.Passed)
            {
                await _transitionBootstrapV040Service.MarkFailedNoRetryAsync(
                    claim.Lease, claim.LeasePath,
                    "v0.51.9 validation returned Passed=false",
                    CancellationToken.None);
                _currentTerminalState = CommandTerminalState.Failed;
                StatusText.Text = "FAILED: v0.51.9 validation did not pass; automatic local Accept refused; no retry authority";
                AcceptanceTextBox.Text = CommandCodec.Serialize(new { Bootstrap = claim.Lease, Acceptance = tested.Receipt, tested.ArtifactPath, AutomaticAcceptPerformed = false });
                OutputTabs.SelectedItem = AcceptanceTab;
                return;
            }

            var candidate = await _checkpointV0519Service.PreviewAsync(WorkspaceRootBox.Text, tested.ArtifactPath, tested.Receipt, _cts.Token);
            var checkpoint = await _checkpointV0519Service.AcceptFromBootstrapAsync(candidate, claim.Lease.LeaseId, _cts.Token);
            var checkpointPath = await LocalCheckpointV0519Service.WriteReceiptAsync(WorkspaceRootBox.Text, checkpoint, _cts.Token);
            var completed = await _transitionBootstrapV040Service.FinalizeAcceptedAsync(claim, tested.ArtifactPath, checkpointPath, _cts.Token);

            _lastAcceptanceConsumed = true;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: v0.51.9 validation PASS + automatic local Accept; {checkpoint.Tag} -> {checkpoint.NewHead}";
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Bootstrap = completed,
                Acceptance = tested.Receipt,
                AcceptanceArtifactPath = tested.ArtifactPath,
                Checkpoint = checkpoint,
                CheckpointReceiptPath = checkpointPath,
                FourButtonSurfacePreserved = true,
                V0518OwnershipStatusAndStaleRecoveryPreserved = true,
                CanonicalLeaseStateRemainsAuthority = true,
                ActiveIndexDerivedOnly = true,
                PriorOwnerMetadataPreservedBeforeSuccessorWrite = true,
                PriorOwnerMetadataMaxBytes = LocalAppMcpOwnerGenerationV0519Service.MaxPriorMetadataBytes,
                ValidPriorMetadataArchivedExact = true,
                InvalidPriorMetadataArchivedOpaqueUntrusted = true,
                ArchiveHashVerificationRequired = true,
                PreservationFailureReleasesOwnerBeforeLeaseCreation = true,
                BusyOwnerCreatesGenerationEvidence = false,
                PriorOwnerEvidenceGrantsLeaseAuthority = false,
                PriorOwnerEvidenceGrantsReadAuthority = false,
                PriorOwnerEvidenceGrantsRevokeAuthority = false,
                PriorOwnerEvidenceGrantsResumeAuthority = false,
                HistoricalLeaseScanPerformedByGenerationPreservation = false,
                CanonicalLeaseMutationPerformedByGenerationPreservation = false,
                ActiveIndexMutationPerformedByGenerationPreservation = false,
                AutomaticPublishPerformed = false,
                AutomaticLifecyclePerformed = false,
                PublicRemotePublicationStillDeferred = true,
                NextExplicitActions = new[]
                {
                    "Start the next local MCP read session normally; any prior stale active owner metadata must be preserved automatically before the successor generation appears",
                    "Use v0.51.8 MCP Ownership Status/Acknowledge when explicit stale-metadata cleanup is desired before starting another session",
                    "Treat generation evidence as provenance only, never as lease/read/revoke/resume authority"
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

    private async Task<(WorkbenchAcceptanceReceipt Receipt, string ArtifactPath)> RunV0519AcceptanceArtifactAsync(CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(this);
        var context = new RuntimeContext(CatalogRootBox.Text, true, false);
        var receipt = await new WorkbenchV0519AcceptanceHarness(_acceptanceHarness, this).RunAsync(context, cancellationToken);
        var dir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"v0.51.9-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, CommandCodec.Serialize(receipt), new UTF8Encoding(false), cancellationToken);
        return (receipt, path);
    }

    private void PublishAcceptedV0519Button_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "Remote publication is intentionally deferred.\n\nLocal v0.51.9 may be accepted and used for MCP owner generation provenance continuity, but public main remains on v0.50.2 while external bridge admission is paused.\n\nNo GitHub mutation was performed.",
            "Publish accepted v0.51.9 — deferred",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  publish.v0519.deferred effect=false; reason=public-v051-gate-unresolved");
    }
}
