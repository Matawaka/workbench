using System.IO;
using System.Text;
using System.Windows;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalCheckpointV0601Service _checkpointV0601Service = new();
    private bool _v0601LoadedBootstrapChecked;

    internal void ConfigureV0601AcceptanceRouting()
    {
        Title = "Matawaka Workbench v0.60.1";
        Loaded -= Window_LoadedV0601;
        Loaded += Window_LoadedV0601;

        // No historical publisher is a valid successor publication route.
        PublishAcceptedButton.IsEnabled = false;
        PublishAcceptedButton.IsHitTestVisible = false;
        PublishAcceptedButton.ToolTip = "v0.60.1 publication is unavailable until exact local first-boot acceptance evidence exists; publication is a separate future authority.";

        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
    }

    internal IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> ObserveV0601RoutingContract() => new[]
    {
        ("title", Title == "Matawaka Workbench v0.60.1", Title, "Matawaka Workbench v0.60.1"),
        ("historical-publisher-disabled", !PublishAcceptedButton.IsEnabled && !PublishAcceptedButton.IsHitTestVisible,
            $"enabled={PublishAcceptedButton.IsEnabled}; hitTest={PublishAcceptedButton.IsHitTestVisible}", "disabled"),
        ("target-tag", LocalCheckpointV0601Service.TargetTag == "workbench-v0.60.1-accepted",
            LocalCheckpointV0601Service.TargetTag, "fresh v0.60.1 accepted tag"),
        ("no-v060-relabel", LocalCheckpointV0601Service.HistoricalV060Tag == "workbench-v0.60-accepted",
            "v0.60 remains reviewed implementation identity only", "do not create/reinterpret workbench-v0.60-accepted"),
        ("model-authority-class-unchanged", BoundedLocalModelInvocationV055Service.RequestSchema == "matawaka.local-model-invocation-request/v0.55",
            BoundedLocalModelInvocationV055Service.RequestSchema, "accepted v0.55 model-request schema unchanged"),
        ("live-authority-evidence-source-bound", V0601QualifiedSourceBindings.ReviewedV060Head == "3063c739a29e2524a3d45d5003095b515b6ad75f",
            V0601QualifiedSourceBindings.ReviewedV060Head, "exact human-reviewed neutral v0.60 frontier")
    };

    private async void Window_LoadedV0601(object sender, RoutedEventArgs e)
    {
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
        PublishAcceptedButton.IsEnabled = false;
        PublishAcceptedButton.IsHitTestVisible = false;

        if (_v0601LoadedBootstrapChecked)
            return;
        _v0601LoadedBootstrapChecked = true;

        TransitionBootstrapV040Claim? claim = null;
        var beganRun = false;
        try
        {
            claim = await _transitionBootstrapV040Service.TryClaimFirstBootAsync(
                WorkspaceRootBox.Text,
                LocalCheckpointV0601Service.Version,
                LocalCheckpointV0601Service.TargetTag,
                CancellationToken.None);
            if (claim is null)
            {
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v0601 none; automaticValidation=false; automaticAccept=false");
                return;
            }

            SetV035PrimaryControlsEnabled(false);
            PublishAcceptedButton.IsEnabled = false;
            PublishAcceptedButton.IsHitTestVisible = false;
            OperatorSurfaceV045Contract.Apply(this);
            BeginRun($"first-boot-bootstrap-v0.60.1-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;
            StatusText.Text = $"RUNNING: v0.60.1 bounded operator-host validation; lease={claim.Lease.LeaseId}";

            var tested = await RunV0601AcceptanceArtifactAsync(_cts!.Token);
            _lastAcceptanceReceipt = tested.Receipt;
            _lastAcceptanceArtifactPath = tested.ArtifactPath;
            _lastAcceptanceConsumed = false;

            if (!tested.Receipt.Passed)
            {
                await _transitionBootstrapV040Service.MarkFailedNoRetryAsync(
                    claim.Lease,
                    claim.LeasePath,
                    "v0.60.1 validation returned Passed=false",
                    CancellationToken.None);
                _currentTerminalState = CommandTerminalState.Failed;
                StatusText.Text = "FAILED: v0.60.1 validation did not pass; automatic local Accept refused; no retry authority";
                AcceptanceTextBox.Text = CommandCodec.Serialize(new
                {
                    Bootstrap = claim.Lease,
                    Acceptance = tested.Receipt,
                    tested.ArtifactPath,
                    AutomaticAcceptPerformed = false,
                    HistoricalV060AcceptedTagCreated = false,
                    NetworkAccessPerformedByAcceptance = false,
                    ArtifactAcquisitionPerformedByAcceptance = false,
                    RuntimeMaterializationPerformedByAcceptance = false,
                    RuntimeExecutionPerformedByAcceptance = false,
                    ModelInvocationPerformedByAcceptance = false,
                    AutomaticPublishPerformed = false
                });
                OutputTabs.SelectedItem = AcceptanceTab;
                return;
            }

            var candidate = await _checkpointV0601Service.PreviewAsync(
                WorkspaceRootBox.Text,
                tested.ArtifactPath,
                tested.Receipt,
                claim.Lease,
                _cts.Token);
            var checkpoint = await _checkpointV0601Service.AcceptFromBootstrapAsync(
                candidate,
                claim.Lease,
                _cts.Token);
            var checkpointPath = await LocalCheckpointV0601Service.WriteReceiptAsync(
                WorkspaceRootBox.Text,
                checkpoint,
                _cts.Token);
            var completed = await _transitionBootstrapV040Service.FinalizeAcceptedAsync(
                claim,
                tested.ArtifactPath,
                checkpointPath,
                _cts.Token);

            _lastAcceptanceConsumed = true;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: v0.60.1 validation PASS + two-parent local Accept; {checkpoint.Tag} -> {checkpoint.NewHead}";
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Bootstrap = completed,
                Acceptance = tested.Receipt,
                AcceptanceArtifactPath = tested.ArtifactPath,
                Checkpoint = checkpoint,
                CheckpointReceiptPath = checkpointPath,
                FirstParent = LocalCheckpointV0601Service.FirstParentCommit,
                SecondParent = LocalCheckpointV0601Service.SecondParentCommit,
                PublicImportReceiptPath = checkpoint.PublicImportReceiptPath,
                PublicImportReceiptSha256 = checkpoint.PublicImportReceiptSha256,
                ReviewedV060Head = V0601QualifiedSourceBindings.ReviewedV060Head,
                HistoricalV060AcceptedTagCreated = false,
                HistoricalReceiptsReinterpreted = false,
                ForcePushAllowed = false,
                ArbitraryRemoteOrRefAllowed = false,
                ArtifactAcquisitionPerformedByAcceptance = false,
                RuntimeMaterializationPerformedByAcceptance = false,
                RuntimeExecutionPerformedByAcceptance = false,
                ModelInvocationPerformedByAcceptance = false,
                NetworkAccessPerformedByAcceptance = false,
                AutomaticRetryPerformed = false,
                AutomaticPublishPerformed = false,
                ResponseAuthorityCreated = false,
                DisplayPermitCreated = false,
                ActionPermitCreated = false,
                SuccessorPermitCreated = false,
                NextExplicitAction = "Audit canonical v0.60.1 local acceptance/checkpoint receipts; publication remains separate"
            });
            OutputTabs.SelectedItem = AcceptanceTab;
        }
        catch (OperationCanceledException ex)
        {
            if (claim is not null)
                await TryFailBootstrapV0601Async(claim, ex.Message);
            ShowCancelled();
        }
        catch (InvalidDataException ex)
        {
            if (claim is not null)
                await TryFailBootstrapV0601Async(claim, ex.Message);
            ShowInvalid(ex);
        }
        catch (Exception ex)
        {
            if (claim is not null)
                await TryFailBootstrapV0601Async(claim, ex.Message);
            ShowFailure(ex);
        }
        finally
        {
            if (beganRun)
                EndRun();
            SetV035PrimaryControlsEnabled(true);
            PublishAcceptedButton.IsEnabled = false;
            PublishAcceptedButton.IsHitTestVisible = false;
            OperatorSurfaceV045Contract.Apply(this);
            RefreshInstalledAppsV044();
            InstallV0441TreeDoubleClickRouting();
        }
    }

    private async Task<(WorkbenchAcceptanceReceipt Receipt, string ArtifactPath)> RunV0601AcceptanceArtifactAsync(
        CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(this);
        PublishAcceptedButton.IsEnabled = false;
        PublishAcceptedButton.IsHitTestVisible = false;
        var context = new RuntimeContext(CatalogRootBox.Text, true, false);
        var receipt = await new WorkbenchV0601AcceptanceHarness(_acceptanceHarness, this)
            .RunAsync(context, WorkspaceRootBox.Text, cancellationToken);
        var dir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"v0.60.1-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        await File.WriteAllTextAsync(path, CommandCodec.Serialize(receipt), new UTF8Encoding(false), cancellationToken);
        return (receipt, path);
    }

    private async Task TryFailBootstrapV0601Async(TransitionBootstrapV040Claim claim, string failure)
    {
        try
        {
            await _transitionBootstrapV040Service.MarkFailedNoRetryAsync(
                claim.Lease,
                claim.LeasePath,
                string.IsNullOrWhiteSpace(failure) ? "v0.60.1 first-boot acceptance failed" : failure,
                CancellationToken.None);
        }
        catch
        {
            // Preserve the primary failure. Bootstrap logging cannot grant retry authority.
        }
    }
}
