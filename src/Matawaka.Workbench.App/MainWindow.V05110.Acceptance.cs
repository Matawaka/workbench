using System.IO;
using System.Text;
using System.Windows;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalCheckpointV05110Service _checkpointV05110Service = new();
    private bool _v05110LoadedBootstrapChecked;

    internal void ConfigureV05110AcceptanceRouting()
    {
        ConfigureV0519AcceptanceRouting();
        Loaded -= Window_LoadedV0519;
        Loaded += Window_LoadedV05110;
        PublishAcceptedButton.Click -= PublishAcceptedV0519Button_Click;
        PublishAcceptedButton.Click += PublishAcceptedV05110Button_Click;
    }

    private async void Window_LoadedV05110(object sender, RoutedEventArgs e)
    {
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
        if (_v05110LoadedBootstrapChecked) return;
        _v05110LoadedBootstrapChecked = true;

        TransitionBootstrapV040Claim? claim = null;
        var beganRun = false;
        try
        {
            claim = await _transitionBootstrapV040Service.TryClaimFirstBootAsync(
                WorkspaceRootBox.Text,
                LocalCheckpointV05110Service.Version,
                LocalCheckpointV05110Service.TargetTag,
                CancellationToken.None);
            if (claim is null)
            {
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v05110 none; automaticValidation=false; automaticAccept=false");
                return;
            }

            SetV035PrimaryControlsEnabled(false);
            OperatorSurfaceV045Contract.Apply(this);
            BeginRun($"first-boot-bootstrap-v0.51.10-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;
            StatusText.Text = $"RUNNING: v0.51.10 MCP owner generation transaction validation; lease={claim.Lease.LeaseId}";

            var tested = await RunV05110AcceptanceArtifactAsync(_cts!.Token);
            _lastAcceptanceReceipt = tested.Receipt;
            _lastAcceptanceArtifactPath = tested.ArtifactPath;
            _lastAcceptanceConsumed = false;
            if (!tested.Receipt.Passed)
            {
                await _transitionBootstrapV040Service.MarkFailedNoRetryAsync(
                    claim.Lease, claim.LeasePath,
                    "v0.51.10 validation returned Passed=false",
                    CancellationToken.None);
                _currentTerminalState = CommandTerminalState.Failed;
                StatusText.Text = "FAILED: v0.51.10 validation did not pass; automatic local Accept refused; no retry authority";
                AcceptanceTextBox.Text = CommandCodec.Serialize(new { Bootstrap = claim.Lease, Acceptance = tested.Receipt, tested.ArtifactPath, AutomaticAcceptPerformed = false });
                OutputTabs.SelectedItem = AcceptanceTab;
                return;
            }

            var candidate = await _checkpointV05110Service.PreviewAsync(WorkspaceRootBox.Text, tested.ArtifactPath, tested.Receipt, _cts.Token);
            var checkpoint = await _checkpointV05110Service.AcceptFromBootstrapAsync(candidate, claim.Lease.LeaseId, _cts.Token);
            var checkpointPath = await LocalCheckpointV05110Service.WriteReceiptAsync(WorkspaceRootBox.Text, checkpoint, _cts.Token);
            var completed = await _transitionBootstrapV040Service.FinalizeAcceptedAsync(claim, tested.ArtifactPath, checkpointPath, _cts.Token);

            _lastAcceptanceConsumed = true;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: v0.51.10 validation PASS + automatic local Accept; {checkpoint.Tag} -> {checkpoint.NewHead}";
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Bootstrap = completed,
                Acceptance = tested.Receipt,
                AcceptanceArtifactPath = tested.ArtifactPath,
                Checkpoint = checkpoint,
                CheckpointReceiptPath = checkpointPath,
                FourButtonSurfacePreserved = true,
                V0519OwnerGenerationContinuityPreserved = true,
                CanonicalLeaseStateRemainsAuthority = true,
                ActiveIndexDerivedOnly = true,
                PreparedDoesNotMeanCommitted = true,
                ReconciliationRunsUnderExistingOwnerLock = true,
                ContentAddressedPriorEvidence = true,
                AbandonedPreparedTransitionReusesVerifiedArchive = true,
                CommittedRecoveredRequiresExactSuccessorMetadata = true,
                MetadataAbsentDoesNotInferCommit = true,
                CommitRequiresExactSuccessorMetadataObservation = true,
                GenerationTransactionGrantsLeaseAuthority = false,
                GenerationTransactionGrantsReadAuthority = false,
                GenerationTransactionGrantsRevokeAuthority = false,
                GenerationTransactionGrantsResumeAuthority = false,
                HistoricalLeaseScanPerformedByGenerationTransaction = false,
                CanonicalLeaseMutationPerformedByGenerationTransaction = false,
                ActiveIndexMutationPerformedByGenerationTransaction = false,
                AutomaticPublishPerformed = false,
                AutomaticLifecyclePerformed = false,
                PublicRemotePublicationStillDeferred = true,
                NextExplicitActions = new[]
                {
                    "Start a normal local MCP read session; inspect the newest v0.51.10 generation transaction receipt and require COMMITTED only after exact successor metadata observation",
                    "Treat PREPARED/ABANDONED/COMMITTED_RECOVERED as provenance states only, never as lease/read/revoke/resume authority",
                    "Use inherited v0.51.8 ownership status/acknowledgement and exact read-session closure normally"
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

    private async Task<(WorkbenchAcceptanceReceipt Receipt, string ArtifactPath)> RunV05110AcceptanceArtifactAsync(CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(this);
        var context = new RuntimeContext(CatalogRootBox.Text, true, false);
        var receipt = await new WorkbenchV05110AcceptanceHarness(_acceptanceHarness, this).RunAsync(context, cancellationToken);
        var dir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"v0.51.10-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, CommandCodec.Serialize(receipt), new UTF8Encoding(false), cancellationToken);
        return (receipt, path);
    }

    private void PublishAcceptedV05110Button_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "Remote publication is intentionally deferred.\n\nLocal v0.51.10 may be accepted and used for crash-consistent MCP owner-generation transaction provenance, but public main remains on v0.50.2 while external bridge admission is paused.\n\nNo GitHub mutation was performed.",
            "Publish accepted v0.51.10 — deferred",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  publish.v05110.deferred effect=false; reason=public-v051-gate-unresolved");
    }
}
