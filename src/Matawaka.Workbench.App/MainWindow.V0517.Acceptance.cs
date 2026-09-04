using System.IO;
using System.Text;
using System.Windows;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalCheckpointV0517Service _checkpointV0517Service = new();
    private bool _v0517LoadedBootstrapChecked;

    internal void ConfigureV0517AcceptanceRouting()
    {
        ConfigureV0516AcceptanceRouting();
        Loaded -= Window_LoadedV0516;
        Loaded += Window_LoadedV0517;

        PublishAcceptedButton.Click -= PublishAcceptedV0516Button_Click;
        PublishAcceptedButton.Click += PublishAcceptedV0517Button_Click;
    }

    private async void Window_LoadedV0517(object sender, RoutedEventArgs e)
    {
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();

        if (_v0517LoadedBootstrapChecked) return;
        _v0517LoadedBootstrapChecked = true;

        TransitionBootstrapV040Claim? claim = null;
        var beganRun = false;
        try
        {
            claim = await _transitionBootstrapV040Service.TryClaimFirstBootAsync(
                WorkspaceRootBox.Text,
                LocalCheckpointV0517Service.Version,
                LocalCheckpointV0517Service.TargetTag,
                CancellationToken.None);

            if (claim is null)
            {
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v0517 none; automaticValidation=false; automaticAccept=false");
                return;
            }

            SetV035PrimaryControlsEnabled(false);
            OperatorSurfaceV045Contract.Apply(this);
            BeginRun($"first-boot-bootstrap-v0.51.7-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;
            StatusText.Text = $"RUNNING: v0.51.7 cross-process MCP session ownership validation; lease={claim.Lease.LeaseId}";

            var tested = await RunV0517AcceptanceArtifactAsync(_cts!.Token);
            _lastAcceptanceReceipt = tested.Receipt;
            _lastAcceptanceArtifactPath = tested.ArtifactPath;
            _lastAcceptanceConsumed = false;

            if (!tested.Receipt.Passed)
            {
                await _transitionBootstrapV040Service.MarkFailedNoRetryAsync(
                    claim.Lease, claim.LeasePath,
                    "v0.51.7 validation returned Passed=false",
                    CancellationToken.None);
                _currentTerminalState = CommandTerminalState.Failed;
                StatusText.Text = "FAILED: v0.51.7 validation did not pass; automatic local Accept refused; no retry authority";
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

            var checkpointCandidate = await _checkpointV0517Service.PreviewAsync(
                WorkspaceRootBox.Text, tested.ArtifactPath, tested.Receipt, _cts.Token);
            var checkpoint = await _checkpointV0517Service.AcceptFromBootstrapAsync(
                checkpointCandidate, claim.Lease.LeaseId, _cts.Token);
            var checkpointPath = await LocalCheckpointV0517Service.WriteReceiptAsync(
                WorkspaceRootBox.Text, checkpoint, _cts.Token);
            var completed = await _transitionBootstrapV040Service.FinalizeAcceptedAsync(
                claim, tested.ArtifactPath, checkpointPath, _cts.Token);

            _lastAcceptanceConsumed = true;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: v0.51.7 validation PASS + automatic local Accept; {checkpoint.Tag} -> {checkpoint.NewHead}";

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
                CanonicalLeaseStateRemainsAuthority = true,
                ActiveIndexDerivedOnly = true,
                CrossProcessMcpOwnershipSerializationOnly = true,
                OwnerAcquiredBeforeAutoLeaseCreation = true,
                SameAppBusyCreatesReplacementLease = false,
                OwnerHandleHeldForListenerLifetime = true,
                ListenerStopRequiredBeforeOwnerRelease = true,
                ExactLeaseRevokeOccursAfterOwnerRelease = true,
                OwnerCrashRevokesCanonicalLease = false,
                StaleOwnerMetadataAuthorizesMcpResume = false,
                DestructiveRecoveryRequiresFreeMcpDomain = true,
                DifferentApplicationsRemainIndependent = true,
                McpOwnershipDefaultTimeoutMs = LocalAppMcpSessionOwnershipV0517Service.DefaultTimeoutMilliseconds,
                OwnerMetadataStoresBearerPlaintext = false,
                OwnerMetadataStoresBearerHash = false,
                OwnerMetadataStoresEndpointPathToken = false,
                HistoricalEvidenceDeletionOrCompaction = false,
                AutomaticOwnerRecoveryRestartPerformedAtFirstBoot = false,
                AutomaticLeaseRevocationPerformed = false,
                AutomaticSecureMcpTunnelPerformed = false,
                AutomaticPublishPerformed = false,
                AutomaticLifecyclePerformed = false,
                PublicRemotePublicationStillDeferred = true,
                NextExplicitActions = new[]
                {
                    "Create a Read Session normally; v0.51.7 must acquire app MCP ownership before creating the exact lease",
                    "If another Workbench already owns MCP for that app, treat MCP_SESSION_OWNED_BY_OTHER_PROCESS as a fail-closed no-lease result",
                    "End Read Session only through listener stop -> ownership release -> exact LeaseId revoke",
                    "After a process crash, treat a surviving live lease as orphan authority requiring existing explicit closure/expiry semantics; never infer MCP resume authority from stale owner metadata"
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

    private async Task<(WorkbenchAcceptanceReceipt Receipt, string ArtifactPath)> RunV0517AcceptanceArtifactAsync(
        CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(this);
        var context = new RuntimeContext(CatalogRootBox.Text, true, false);
        var receipt = await new WorkbenchV0517AcceptanceHarness(_acceptanceHarness, this).RunAsync(context, cancellationToken);
        var dir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"v0.51.7-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, CommandCodec.Serialize(receipt), new UTF8Encoding(false), cancellationToken);
        return (receipt, path);
    }

    private void PublishAcceptedV0517Button_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "Remote publication is intentionally deferred.\n\n" +
            "Local v0.51.7 may be accepted and used for cross-process MCP session ownership, but public main remains on v0.50.2 while the external bridge admission is paused.\n\n" +
            "No GitHub mutation was performed.",
            "Publish accepted v0.51.7 — deferred",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  publish.v0517.deferred effect=false; reason=public-v051-gate-unresolved");
    }
}
