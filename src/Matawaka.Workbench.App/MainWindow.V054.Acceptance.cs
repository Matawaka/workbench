using System.IO;
using System.Text;
using System.Windows;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalCheckpointV054Service _checkpointV054Service = new();
    private bool _v054LoadedBootstrapChecked;

    internal void ConfigureV054AcceptanceRouting()
    {
        ConfigureV0532AcceptanceRouting();
        Loaded -= Window_LoadedV0532;
        Loaded += Window_LoadedV054;
        PublishAcceptedButton.Click -= PublishAcceptedV0532Button_Click;
        PublishAcceptedButton.Click += PublishAcceptedV054Button_Click;
    }

    private async void Window_LoadedV054(object sender, RoutedEventArgs e)
    {
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
        if (_v054LoadedBootstrapChecked) return;
        _v054LoadedBootstrapChecked = true;

        TransitionBootstrapV040Claim? claim = null;
        var beganRun = false;
        try
        {
            claim = await _transitionBootstrapV040Service.TryClaimFirstBootAsync(
                WorkspaceRootBox.Text,
                LocalCheckpointV054Service.Version,
                LocalCheckpointV054Service.TargetTag,
                CancellationToken.None);
            if (claim is null)
            {
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v054 none; automaticValidation=false; automaticAccept=false");
                return;
            }

            SetV035PrimaryControlsEnabled(false);
            OperatorSurfaceV045Contract.Apply(this);
            BeginRun($"first-boot-bootstrap-v0.54-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;
            StatusText.Text = $"RUNNING: v0.54 bounded runtime-tree materialization validation; lease={claim.Lease.LeaseId}";

            var tested = await RunV054AcceptanceArtifactAsync(_cts!.Token);
            _lastAcceptanceReceipt = tested.Receipt;
            _lastAcceptanceArtifactPath = tested.ArtifactPath;
            _lastAcceptanceConsumed = false;

            if (!tested.Receipt.Passed)
            {
                await _transitionBootstrapV040Service.MarkFailedNoRetryAsync(
                    claim.Lease, claim.LeasePath, "v0.54 validation returned Passed=false", CancellationToken.None);
                _currentTerminalState = CommandTerminalState.Failed;
                StatusText.Text = "FAILED: v0.54 materialization validation did not pass; automatic local Accept refused; no retry authority";
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

            var candidate = await _checkpointV054Service.PreviewAsync(
                WorkspaceRootBox.Text, tested.ArtifactPath, tested.Receipt, _cts.Token);
            var checkpoint = await _checkpointV054Service.AcceptFromBootstrapAsync(
                candidate, claim.Lease.LeaseId, _cts.Token);
            var checkpointPath = await LocalCheckpointV054Service.WriteReceiptAsync(
                WorkspaceRootBox.Text, checkpoint, _cts.Token);
            var completed = await _transitionBootstrapV040Service.FinalizeAcceptedAsync(
                claim, tested.ArtifactPath, checkpointPath, _cts.Token);

            _lastAcceptanceConsumed = true;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: v0.54 validation PASS + automatic local Accept; {checkpoint.Tag} -> {checkpoint.NewHead}";
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Bootstrap = completed,
                Acceptance = tested.Receipt,
                AcceptanceArtifactPath = tested.ArtifactPath,
                Checkpoint = checkpoint,
                CheckpointReceiptPath = checkpointPath,
                GenericBoundedRuntimeTreeMaterializationPrimitive = true,
                RequestSchema = BoundedRuntimeTreeMaterializationV054Service.RequestSchema,
                SourceAcquisitionReceiptSchema = BoundedArtifactAcquisitionV052Service.ExecutionReceiptSchema,
                RuntimeTreeManifestSchema = BoundedRuntimeExecutionV053Service.RuntimeTreeManifestSchema,
                VerifiedArtifactEqualsMaterializedRuntime = false,
                MaterializedRuntimeEqualsExecutionAuthority = false,
                OneShotMaterializationCallBudget = 1,
                AuthorityConsumedBeforeDestinationMutation = true,
                RuntimeTreeManifestV053Compatible = true,
                NetworkAuthority = false,
                ProcessExecutionAuthority = false,
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
                    "Run one tiny non-KONTUR real-host ZIP materialization smoke from exact v0.52 acquisition receipt evidence",
                    "Require RUNTIME_TREE_MATERIALIZATION_VERIFIED + exact v0.53-compatible MATERIALIZED_VERIFIED manifest",
                    "Optionally preview that manifest with unchanged v0.53 execution service without starting a process",
                    "Only after the real-host materialization gate decide whether to Publish accepted v0.54"
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

    private async Task<(WorkbenchAcceptanceReceipt Receipt, string ArtifactPath)> RunV054AcceptanceArtifactAsync(CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(this);
        var context = new RuntimeContext(CatalogRootBox.Text, true, false);
        var receipt = await new WorkbenchV054AcceptanceHarness(_acceptanceHarness, this).RunAsync(context, cancellationToken);
        var dir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"v0.54-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, CommandCodec.Serialize(receipt), new UTF8Encoding(false), cancellationToken);
        return (receipt, path);
    }

    private void PublishAcceptedV054Button_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "Remote publication is intentionally deferred.\n\nv0.54 introduces a generic bounded runtime-tree materialization lease between exact v0.52 acquisition evidence and the existing v0.53 execution lease. Complete one tiny non-KONTUR real-host ZIP materialization smoke before publication.\n\nNo GitHub mutation was performed.",
            "Publish accepted v0.54 — deferred",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  publish.v054.deferred effect=false; reason=runtime-materialization-realhost-smoke-unresolved");
    }
}
