using System.IO;
using System.Text;
using System.Windows;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalCheckpointV0532Service _checkpointV0532Service = new();
    private readonly FixedGitHubPublicationV0532Service _publisherV0532Service = new();
    private bool _v0532LoadedBootstrapChecked;

    internal void ConfigureV0532AcceptanceRouting()
    {
        ConfigureV053AcceptanceRouting();
        Loaded -= Window_LoadedV053;
        Loaded += Window_LoadedV0532;
        PublishAcceptedButton.Click -= PublishAcceptedV053Button_Click;
        PublishAcceptedButton.Click += PublishAcceptedV0532Button_Click;
    }

    private async void Window_LoadedV0532(object sender, RoutedEventArgs e)
    {
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
        if (_v0532LoadedBootstrapChecked) return;
        _v0532LoadedBootstrapChecked = true;

        TransitionBootstrapV040Claim? claim = null;
        var beganRun = false;
        try
        {
            claim = await _transitionBootstrapV040Service.TryClaimFirstBootAsync(
                WorkspaceRootBox.Text,
                LocalCheckpointV0532Service.Version,
                LocalCheckpointV0532Service.TargetTag,
                CancellationToken.None);
            if (claim is null)
            {
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v0532 none; automaticValidation=false; automaticAccept=false");
                return;
            }

            SetV035PrimaryControlsEnabled(false);
            OperatorSurfaceV045Contract.Apply(this);
            BeginRun($"first-boot-bootstrap-v0.53.2-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;
            StatusText.Text = $"RUNNING: v0.53.2 real-host admission/publication validation; lease={claim.Lease.LeaseId}";

            var tested = await RunV0532AcceptanceArtifactAsync(_cts!.Token);
            _lastAcceptanceReceipt = tested.Receipt;
            _lastAcceptanceArtifactPath = tested.ArtifactPath;
            _lastAcceptanceConsumed = false;

            if (!tested.Receipt.Passed)
            {
                await _transitionBootstrapV040Service.MarkFailedNoRetryAsync(
                    claim.Lease, claim.LeasePath, "v0.53.2 validation returned Passed=false", CancellationToken.None);
                _currentTerminalState = CommandTerminalState.Failed;
                StatusText.Text = "FAILED: v0.53.2 validation did not pass; automatic local Accept refused; no retry authority";
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

            var candidate = await _checkpointV0532Service.PreviewAsync(
                WorkspaceRootBox.Text, tested.ArtifactPath, tested.Receipt, _cts.Token);
            var checkpoint = await _checkpointV0532Service.AcceptFromBootstrapAsync(
                candidate, claim.Lease.LeaseId, _cts.Token);
            var checkpointPath = await LocalCheckpointV0532Service.WriteReceiptAsync(
                WorkspaceRootBox.Text, checkpoint, _cts.Token);
            var completed = await _transitionBootstrapV040Service.FinalizeAcceptedAsync(
                claim, tested.ArtifactPath, checkpointPath, _cts.Token);

            _lastAcceptanceConsumed = true;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: v0.53.2 validation PASS + automatic local Accept; {checkpoint.Tag} -> {checkpoint.NewHead}";
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Bootstrap = completed,
                Acceptance = tested.Receipt,
                AcceptanceArtifactPath = tested.ArtifactPath,
                Checkpoint = checkpoint,
                CheckpointReceiptPath = checkpointPath,
                V053RuntimeExecutionPrimitivePreserved = true,
                V0531DiagnosticsCandidateReinterpreted = false,
                PublishPreviewPerformsNetwork = false,
                RealHostExecutionAndStopEvidenceRequiredForPublish = true,
                FixedRemoteOnly = FixedGitHubPublicationV0532Service.RemoteUrl,
                ForcePushAllowed = false,
                AutomaticPublishPerformed = false,
                AutomaticLifecyclePerformed = false,
                NextExplicitActions = new[]
                {
                    "Invoke Publish accepted only if remote publication of the exact accepted v0.53.2 frontier is intended",
                    "Review exact local real-host execution/stop evidence and fixed remote shown by the no-effect preview",
                    "Explicitly confirm before any ls-remote/remote-add/push network effect"
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

    private async Task<(WorkbenchAcceptanceReceipt Receipt, string ArtifactPath)> RunV0532AcceptanceArtifactAsync(CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(this);
        var context = new RuntimeContext(CatalogRootBox.Text, true, false);
        var receipt = await new WorkbenchV0532AcceptanceHarness(_acceptanceHarness, this).RunAsync(context, cancellationToken);
        var dir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"v0.53.2-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, CommandCodec.Serialize(receipt), new UTF8Encoding(false), cancellationToken);
        return (receipt, path);
    }

    private async void PublishAcceptedV0532Button_Click(object sender, RoutedEventArgs e)
    {
        FixedGitHubPublicationCandidateV0532 candidate;
        try
        {
            candidate = await _publisherV0532Service.PreviewAsync(WorkspaceRootBox.Text, CancellationToken.None);
        }
        catch (InvalidDataException ex)
        {
            ShowInvalid(ex);
            return;
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
            return;
        }

        var message = new StringBuilder();
        message.AppendLine("Publish the exact accepted Workbench v0.53.2 frontier to the fixed GitHub remote?");
        message.AppendLine();
        message.AppendLine($"Accepted HEAD: {candidate.Head}");
        message.AppendLine($"Parent: {candidate.Parent} / {FixedGitHubPublicationV0532Service.ExpectedAcceptedV053Tag}");
        message.AppendLine($"Tag: {candidate.AcceptedTag}");
        message.AppendLine($"Remote: {candidate.RemoteName} -> {candidate.RemoteUrl}");
        message.AppendLine($"Required remote main before push: {candidate.ExpectedRemoteBase} (or already exact HEAD)");
        message.AppendLine();
        message.AppendLine("Real-host admission evidence:");
        message.AppendLine($"  LeaseId: {candidate.Admission.LeaseId}");
        message.AppendLine($"  PID: {candidate.Admission.ProcessId}");
        message.AppendLine($"  Executable SHA-256: {candidate.Admission.ExecutableSha256}");
        message.AppendLine($"  Execution receipt SHA-256: {candidate.Admission.ExecutionReceiptSha256}");
        message.AppendLine($"  Stop receipt SHA-256: {candidate.Admission.StopReceiptSha256}");
        message.AppendLine();
        message.AppendLine("YES starts the first network effect. It may add only the fixed github-workbench remote when absent, read fixed remote refs, fast-forward this exact HEAD to refs/heads/main, and publish only the current workbench-v0.53.2-accepted tag. No force push or arbitrary ref/remote is permitted.");
        message.AppendLine();
        message.AppendLine("Intermediate local accepted tags remain local; their ancestry is not a separate remote acceptance promotion.");

        if (MessageBox.Show(this, message.ToString(), "Publish accepted Workbench v0.53.2", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  publish.v0532.cancelled effect=false; head={candidate.Head}");
            return;
        }

        var beganRun = false;
        try
        {
            SetV035PrimaryControlsEnabled(false);
            BeginRun($"publish-accepted-v0.53.2-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;
            StatusText.Text = $"RUNNING: fixed publication v0.53.2; head={candidate.Head}";

            var receipt = await _publisherV0532Service.PublishAsync(candidate, _cts!.Token);
            var receiptPath = await FixedGitHubPublicationV0532Service.WriteReceiptAsync(WorkspaceRootBox.Text, receipt, _cts.Token);
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Status = receipt.Status,
                Publication = receipt,
                PublicationReceiptPath = receiptPath,
                RuntimeExecutionPerformedByPublication = false,
                ArtifactAcquisitionPerformedByPublication = false,
                ModelRequestPerformedByPublication = false,
                ForcePushPerformed = false,
                ArbitraryRemoteUsed = false
            });
            OutputTabs.SelectedItem = AcceptanceTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: published accepted v0.53.2; main={receipt.RemoteMainAfter}; tag={receipt.AcceptedTag}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  publish.v0532.completed head={receipt.Head}; mainPush={receipt.MainPushPerformed}; tagPush={receipt.TagPushPerformed}; force=false");
        }
        catch (OperationCanceledException)
        {
            ShowCancelled();
        }
        catch (InvalidDataException ex)
        {
            ShowInvalid(ex);
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
        finally
        {
            if (beganRun) EndRun();
            SetV035PrimaryControlsEnabled(true);
            OperatorSurfaceV045Contract.Apply(this);
        }
    }
}
