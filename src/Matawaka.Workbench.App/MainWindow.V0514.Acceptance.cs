using System.IO;
using System.Text;
using System.Windows;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalCheckpointV0514Service _checkpointV0514Service = new();
    private bool _v0514LoadedBootstrapChecked;

    internal void ConfigureV0514AcceptanceRouting()
    {
        Loaded -= Window_LoadedV0513;
        Loaded += Window_LoadedV0514;

        PublishAcceptedButton.Click -= PublishAcceptedV0513Button_Click;
        PublishAcceptedButton.Click += PublishAcceptedV0514Button_Click;
    }

    private async void Window_LoadedV0514(object sender, RoutedEventArgs e)
    {
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();

        if (_v0514LoadedBootstrapChecked) return;
        _v0514LoadedBootstrapChecked = true;

        TransitionBootstrapV040Claim? claim = null;
        var beganRun = false;
        try
        {
            claim = await _transitionBootstrapV040Service.TryClaimFirstBootAsync(
                WorkspaceRootBox.Text,
                LocalCheckpointV0514Service.Version,
                LocalCheckpointV0514Service.TargetTag,
                CancellationToken.None);

            if (claim is null)
            {
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v0514 none; automaticValidation=false; automaticAccept=false");
                return;
            }

            SetV035PrimaryControlsEnabled(false);
            OperatorSurfaceV045Contract.Apply(this);
            BeginRun($"first-boot-bootstrap-v0.51.4-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;
            StatusText.Text = $"RUNNING: v0.51.4 bounded Read Session Status validation; lease={claim.Lease.LeaseId}";

            var tested = await RunV0514AcceptanceArtifactAsync(_cts!.Token);
            _lastAcceptanceReceipt = tested.Receipt;
            _lastAcceptanceArtifactPath = tested.ArtifactPath;
            _lastAcceptanceConsumed = false;

            if (!tested.Receipt.Passed)
            {
                await _transitionBootstrapV040Service.MarkFailedNoRetryAsync(
                    claim.Lease, claim.LeasePath,
                    "v0.51.4 validation returned Passed=false",
                    CancellationToken.None);
                _currentTerminalState = CommandTerminalState.Failed;
                StatusText.Text = "FAILED: v0.51.4 validation did not pass; automatic local Accept refused; no retry authority";
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

            var checkpointCandidate = await _checkpointV0514Service.PreviewAsync(
                WorkspaceRootBox.Text, tested.ArtifactPath, tested.Receipt, _cts.Token);
            var checkpoint = await _checkpointV0514Service.AcceptFromBootstrapAsync(
                checkpointCandidate, claim.Lease.LeaseId, _cts.Token);
            var checkpointPath = await LocalCheckpointV0514Service.WriteReceiptAsync(
                WorkspaceRootBox.Text, checkpoint, _cts.Token);
            var completed = await _transitionBootstrapV040Service.FinalizeAcceptedAsync(
                claim, tested.ArtifactPath, checkpointPath, _cts.Token);

            _lastAcceptanceConsumed = true;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: v0.51.4 validation PASS + automatic local Accept; {checkpoint.Tag} -> {checkpoint.NewHead}";

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
                LiveAuthoritySilentlyPaginated = false,
                LiveAuthorityHardLimit = LocalAppReadSessionStatusV0514Service.LiveAuthorityHardLimit,
                HistoryDefaultLimit = LocalAppReadSessionStatusV0514Service.DefaultHistoryLimit,
                HistoryHardMax = LocalAppReadSessionStatusV0514Service.MaxHistoryLimit,
                HistoricalEvidenceDeletionOrCompaction = false,
                BearerPlaintextOrHashDisclosed = false,
                AutomaticRevocationPerformed = false,
                AutomaticSecureMcpTunnelPerformed = false,
                AutomaticPublishPerformed = false,
                AutomaticLifecyclePerformed = false,
                PublicRemotePublicationStillDeferred = true,
                NextExplicitActions = new[]
                {
                    "Use bounded Read Session Status; choose a history page only when needed",
                    "Treat LIVE_AUTHORITY_OVERFLOW as a fail-closed recovery condition, never as permission to hide authority",
                    "Use exact orphan closure independently of history pagination",
                    "Keep lease-state evidence intact; filesystem active-index optimization remains a separate future layer"
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

    private async Task<(WorkbenchAcceptanceReceipt Receipt, string ArtifactPath)> RunV0514AcceptanceArtifactAsync(
        CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(this);
        var context = new RuntimeContext(CatalogRootBox.Text, true, false);
        var receipt = await new WorkbenchV0514AcceptanceHarness(_acceptanceHarness, this).RunAsync(context, cancellationToken);
        var dir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"v0.51.4-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, CommandCodec.Serialize(receipt), new UTF8Encoding(false), cancellationToken);
        return (receipt, path);
    }

    private void PublishAcceptedV0514Button_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "Remote publication is intentionally deferred.\n\n" +
            "Local v0.51.4 may be accepted and used for bounded bearer-free session status, but public main remains on v0.50.2 while the external bridge admission is paused.\n\n" +
            "No GitHub mutation was performed.",
            "Publish accepted v0.51.4 — deferred",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  publish.v0514.deferred effect=false; reason=public-v051-gate-unresolved");
    }
}
