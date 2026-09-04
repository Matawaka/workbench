using System.IO;
using System.Text;
using System.Windows;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalCheckpointV053Service _checkpointV053Service = new();
    private bool _v053LoadedBootstrapChecked;

    internal void ConfigureV053AcceptanceRouting()
    {
        ConfigureV0521AcceptanceRouting();
        Loaded -= Window_LoadedV0521;
        Loaded += Window_LoadedV053;
        PublishAcceptedButton.Click -= PublishAcceptedV0521Button_Click;
        PublishAcceptedButton.Click += PublishAcceptedV053Button_Click;
    }

    private async void Window_LoadedV053(object sender, RoutedEventArgs e)
    {
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
        if (_v053LoadedBootstrapChecked) return;
        _v053LoadedBootstrapChecked = true;

        TransitionBootstrapV040Claim? claim = null;
        var beganRun = false;
        try
        {
            claim = await _transitionBootstrapV040Service.TryClaimFirstBootAsync(
                WorkspaceRootBox.Text,
                LocalCheckpointV053Service.Version,
                LocalCheckpointV053Service.TargetTag,
                CancellationToken.None);
            if (claim is null)
            {
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v053 none; automaticValidation=false; automaticAccept=false");
                return;
            }

            SetV035PrimaryControlsEnabled(false);
            OperatorSurfaceV045Contract.Apply(this);
            BeginRun($"first-boot-bootstrap-v0.53-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;
            StatusText.Text = $"RUNNING: v0.53 bounded runtime execution validation; lease={claim.Lease.LeaseId}";

            var tested = await RunV053AcceptanceArtifactAsync(_cts!.Token);
            _lastAcceptanceReceipt = tested.Receipt;
            _lastAcceptanceArtifactPath = tested.ArtifactPath;
            _lastAcceptanceConsumed = false;

            if (!tested.Receipt.Passed)
            {
                await _transitionBootstrapV040Service.MarkFailedNoRetryAsync(
                    claim.Lease, claim.LeasePath, "v0.53 validation returned Passed=false", CancellationToken.None);
                _currentTerminalState = CommandTerminalState.Failed;
                StatusText.Text = "FAILED: v0.53 runtime execution validation did not pass; automatic local Accept refused; no retry authority";
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

            var candidate = await _checkpointV053Service.PreviewAsync(
                WorkspaceRootBox.Text, tested.ArtifactPath, tested.Receipt, _cts.Token);
            var checkpoint = await _checkpointV053Service.AcceptFromBootstrapAsync(
                candidate, claim.Lease.LeaseId, _cts.Token);
            var checkpointPath = await LocalCheckpointV053Service.WriteReceiptAsync(
                WorkspaceRootBox.Text, checkpoint, _cts.Token);
            var completed = await _transitionBootstrapV040Service.FinalizeAcceptedAsync(
                claim, tested.ArtifactPath, checkpointPath, _cts.Token);

            _lastAcceptanceConsumed = true;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: v0.53 validation PASS + automatic local Accept; {checkpoint.Tag} -> {checkpoint.NewHead}";
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Bootstrap = completed,
                Acceptance = tested.Receipt,
                AcceptanceArtifactPath = tested.ArtifactPath,
                Checkpoint = checkpoint,
                CheckpointReceiptPath = checkpointPath,
                GenericBoundedRuntimeExecutionPrimitive = true,
                RequestSchema = BoundedRuntimeExecutionV053Service.RequestSchema,
                RuntimeTreeManifestSchema = BoundedRuntimeExecutionV053Service.RuntimeTreeManifestSchema,
                VerifiedArtifactEqualsMaterializedRuntime = false,
                MaterializedRuntimeEqualsExecutionAuthority = false,
                OneShotExecutionCallBudget = 1,
                AuthorityConsumedBeforeProcessStart = true,
                ShellIndirectionAllowed = false,
                ElevationAllowed = false,
                ExactExecutableSha256RevalidatedBeforeStart = true,
                ExactProcessImageVerifiedAfterStart = true,
                ProcessStartedEqualsRuntimeReady = false,
                ArbitraryPidStopAuthority = false,
                RuntimeTreeMaterializationAuthority = false,
                BenchmarkAuthority = false,
                ModelRequestAuthority = false,
                GameAccessAuthority = false,
                KONTURSpecificRuntimeBehavior = false,
                AutomaticRetryOrResume = false,
                AutomaticPublishPerformed = false,
                AutomaticLifecyclePerformed = false,
                PublicRemotePublicationStillDeferred = true,
                NextExplicitActions = new[]
                {
                    "Prepare one tiny non-KONTUR already-materialized runtime-tree manifest outside the Workbench Git repository",
                    "Run one real-host bounded execution smoke and require exact Windows process-image verification",
                    "If process stays alive, exercise exact-owned-process stop; never supply or accept an arbitrary PID",
                    "Keep LM1/LM3-A benchmark/model/game authority separate"
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

    private async Task<(WorkbenchAcceptanceReceipt Receipt, string ArtifactPath)> RunV053AcceptanceArtifactAsync(CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(this);
        var context = new RuntimeContext(CatalogRootBox.Text, true, false);
        var receipt = await new WorkbenchV053AcceptanceHarness(_acceptanceHarness, this).RunAsync(context, cancellationToken);
        var dir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"v0.53-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, CommandCodec.Serialize(receipt), new UTF8Encoding(false), cancellationToken);
        return (receipt, path);
    }

    private void PublishAcceptedV053Button_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "Remote publication is intentionally deferred.\n\nv0.53 introduces a provider-neutral bounded runtime execution lease above separately materialized runtime-tree evidence. A tiny non-KONTUR real-host execution smoke and exact owned-process stop must be completed before publication.\n\nNo GitHub mutation was performed.",
            "Publish accepted v0.53 — deferred",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  publish.v053.deferred effect=false; reason=runtime-execution-realhost-smoke-unresolved");
    }
}
