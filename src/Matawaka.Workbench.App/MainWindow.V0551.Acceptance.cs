using System.IO;
using System.Text;
using System.Windows;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalCheckpointV0551Service _checkpointV0551Service = new();
    private readonly FixedGitHubPublicationV0551Service _publicationV0551Service = new();
    private bool _v0551LoadedBootstrapChecked;

    internal void ConfigureV0551AcceptanceRouting()
    {
        ConfigureV055AcceptanceRouting();
        Loaded -= Window_LoadedV055;
        Loaded -= Window_LoadedV0551;
        Loaded += Window_LoadedV0551;
        PublishAcceptedButton.Click -= PublishAcceptedV055Button_Click;
        PublishAcceptedButton.Click -= PublishAcceptedV0551Button_Click;
        PublishAcceptedButton.Click += PublishAcceptedV0551Button_Click;
        Title = "Matawaka Workbench v0.55.1";
    }

    private async void Window_LoadedV0551(object sender, RoutedEventArgs e)
    {
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
        if (_v0551LoadedBootstrapChecked) return;
        _v0551LoadedBootstrapChecked = true;

        TransitionBootstrapV040Claim? claim = null;
        var beganRun = false;
        try
        {
            claim = await _transitionBootstrapV040Service.TryClaimFirstBootAsync(
                WorkspaceRootBox.Text,
                LocalCheckpointV0551Service.Version,
                LocalCheckpointV0551Service.TargetTag,
                CancellationToken.None);
            if (claim is null)
            {
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v0551 none; automaticValidation=false; automaticAccept=false");
                return;
            }

            SetV035PrimaryControlsEnabled(false);
            OperatorSurfaceV045Contract.Apply(this);
            BeginRun($"first-boot-bootstrap-v0.55.1-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;
            StatusText.Text = $"RUNNING: v0.55.1 real-host invocation admission + fixed publication validation; lease={claim.Lease.LeaseId}";

            var tested = await RunV0551AcceptanceArtifactAsync(_cts!.Token);
            _lastAcceptanceReceipt = tested.Receipt;
            _lastAcceptanceArtifactPath = tested.ArtifactPath;
            _lastAcceptanceConsumed = false;

            if (!tested.Receipt.Passed)
            {
                await _transitionBootstrapV040Service.MarkFailedNoRetryAsync(
                    claim.Lease, claim.LeasePath, "v0.55.1 validation returned Passed=false", CancellationToken.None);
                _currentTerminalState = CommandTerminalState.Failed;
                StatusText.Text = "FAILED: v0.55.1 validation did not pass; automatic local Accept refused; no retry authority";
                AcceptanceTextBox.Text = CommandCodec.Serialize(new
                {
                    Bootstrap = claim.Lease,
                    Acceptance = tested.Receipt,
                    tested.ArtifactPath,
                    AutomaticAcceptPerformed = false,
                    ModelInvocationPerformed = false,
                    AutomaticPublishPerformed = false
                });
                OutputTabs.SelectedItem = AcceptanceTab;
                return;
            }

            var candidate = await _checkpointV0551Service.PreviewAsync(
                WorkspaceRootBox.Text, tested.ArtifactPath, tested.Receipt, _cts.Token);
            var checkpoint = await _checkpointV0551Service.AcceptFromBootstrapAsync(
                candidate, claim.Lease.LeaseId, _cts.Token);
            var checkpointPath = await LocalCheckpointV0551Service.WriteReceiptAsync(
                WorkspaceRootBox.Text, checkpoint, _cts.Token);
            var completed = await _transitionBootstrapV040Service.FinalizeAcceptedAsync(
                claim, tested.ArtifactPath, checkpointPath, _cts.Token);

            _lastAcceptanceConsumed = true;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: v0.55.1 validation PASS + automatic local Accept; {checkpoint.Tag} -> {checkpoint.NewHead}";
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Bootstrap = completed,
                Acceptance = tested.Receipt,
                AcceptanceArtifactPath = tested.ArtifactPath,
                Checkpoint = checkpoint,
                CheckpointReceiptPath = checkpointPath,
                RealHostV055InvocationAdmissionRequired = true,
                FixedPublicationRemote = FixedGitHubPublicationV0551Service.RemoteUrl,
                FixedPublicationExpectedRemoteBase = FixedGitHubPublicationV0551Service.ExpectedRemoteBase,
                LocalV055Predecessor = FixedGitHubPublicationV0551Service.ExpectedAcceptedV055Commit,
                IntermediateV055TagPublicationAllowed = false,
                ForcePushAllowed = false,
                ArbitraryRemoteOrRefAllowed = false,
                ArtifactAcquisitionPerformedByAcceptance = false,
                RuntimeMaterializationPerformedByAcceptance = false,
                RuntimeExecutionPerformedByAcceptance = false,
                ModelInvocationPerformedByAcceptance = false,
                NetworkAccessPerformedByAcceptance = false,
                AutomaticPublishPerformed = false,
                NextExplicitAction = "Publish accepted -> review local no-network preview -> explicit Yes"
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

    private async Task<(WorkbenchAcceptanceReceipt Receipt, string ArtifactPath)> RunV0551AcceptanceArtifactAsync(CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(this);
        var context = new RuntimeContext(CatalogRootBox.Text, true, false);
        var receipt = await new WorkbenchV0551AcceptanceHarness(_acceptanceHarness, this).RunAsync(context, cancellationToken);
        var dir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"v0.55.1-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, CommandCodec.Serialize(receipt), new UTF8Encoding(false), cancellationToken);
        return (receipt, path);
    }

    internal IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> ObserveV0551PublicationAdmissionContract()
    {
        try
        {
            var admission = RealHostModelInvocationAdmissionVerifierV0551.FindExact(WorkspaceRootBox.Text);
            return new[]
            {
                ("realhost-v055-invocation-admission", true, admission.ExecutionReceiptSha256, RealHostModelInvocationAdmissionVerifierV0551.ExpectedExecutionReceiptSha256),
                ("realhost-v055-terminal-lease", admission.LeaseStateSha256 == RealHostModelInvocationAdmissionVerifierV0551.ExpectedLeaseStateSha256,
                    admission.LeaseStateSha256, RealHostModelInvocationAdmissionVerifierV0551.ExpectedLeaseStateSha256),
                ("realhost-v055-output", admission.OutputArtifactSha256 == RealHostModelInvocationAdmissionVerifierV0551.ExpectedOutputSha256 &&
                    admission.OutputBytes == RealHostModelInvocationAdmissionVerifierV0551.ExpectedOutputBytes,
                    $"{admission.OutputBytes}/{admission.OutputArtifactSha256}",
                    $"{RealHostModelInvocationAdmissionVerifierV0551.ExpectedOutputBytes}/{RealHostModelInvocationAdmissionVerifierV0551.ExpectedOutputSha256}"),
                ("realhost-v055-no-replay", true, "terminal lease requires RemainingCalls=0; validator performed no replay", "true")
            };
        }
        catch (Exception ex)
        {
            return new[] { ("realhost-v055-invocation-admission", false, ex.Message, "exact v0.55 real-host UNTRUSTED_LOCAL_MODEL_OUTPUT + terminal consumed lease") };
        }
    }

    private async void PublishAcceptedV0551Button_Click(object sender, RoutedEventArgs e)
    {
        FixedGitHubPublicationCandidateV0551 candidate;
        try
        {
            candidate = await _publicationV0551Service.PreviewAsync(WorkspaceRootBox.Text, CancellationToken.None);
        }
        catch (Exception ex)
        {
            ShowInvalid(ex is InvalidDataException data ? data : new InvalidDataException("V0551_PUBLICATION_PREVIEW_REFUSED: " + ex.Message, ex));
            return;
        }

        var text = new StringBuilder();
        text.AppendLine("Publish exact accepted Workbench v0.55.1 to fixed GitHub remote?");
        text.AppendLine();
        text.AppendLine($"Accepted HEAD: {candidate.Head}");
        text.AppendLine($"Exact local predecessor: {candidate.Parent} / {LocalCheckpointV0551Service.ExpectedPredecessorTag}");
        text.AppendLine($"Current accepted tag: {candidate.AcceptedTag}");
        text.AppendLine($"Fixed remote: {candidate.RemoteName} -> {candidate.RemoteUrl}");
        text.AppendLine($"Required current public base: {candidate.ExpectedRemoteBase} / workbench-v0.54.2-accepted");
        text.AppendLine("Intermediate workbench-v0.55-accepted will NOT be published.");
        text.AppendLine();
        text.AppendLine("Real-host v0.55 invocation admission:");
        text.AppendLine($"  execution receipt: {candidate.Admission.ExecutionReceiptPath}");
        text.AppendLine($"  receipt SHA-256: {candidate.Admission.ExecutionReceiptSha256}");
        text.AppendLine($"  terminal lease SHA-256: {candidate.Admission.LeaseStateSha256}");
        text.AppendLine($"  transaction: {candidate.Admission.TransactionId}");
        text.AppendLine($"  lease: {candidate.Admission.LeaseId}");
        text.AppendLine($"  runtime manifest SHA-256: {candidate.Admission.RuntimeManifestSha256}");
        text.AppendLine($"  executable SHA-256: {candidate.Admission.ExecutableSha256}");
        text.AppendLine($"  model SHA-256: {candidate.Admission.ModelSha256}");
        text.AppendLine($"  output: {candidate.Admission.OutputBytes} bytes / SHA-256 {candidate.Admission.OutputArtifactSha256}");
        text.AppendLine();
        text.AppendLine("No network operation has been performed by this preview. YES is the first remote/network operation. It permits only exact accepted v0.55.1 HEAD -> refs/heads/main fast-forward and the current v0.55.1 accepted tag -> the same fixed repository. No force push, arbitrary remote/ref, intermediate v0.55 tag, retry, source mutation, acquisition, materialization, process/runtime/model invocation, benchmark, game/display/send or authority creation.");

        if (MessageBox.Show(this, text.ToString(), "Publish accepted v0.55.1", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  publish.v0551 cancelled effect=false; previewNetwork=false");
            return;
        }

        var beganRun = false;
        try
        {
            SetV035PrimaryControlsEnabled(false);
            BeginRun($"publish-v0.55.1-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;
            StatusText.Text = "RUNNING: fixed publication of exact accepted v0.55.1; no force; exact remote/ref only";
            var publication = await _publicationV0551Service.PublishAsync(candidate, _cts!.Token);
            var receiptPath = await FixedGitHubPublicationV0551Service.WriteReceiptAsync(WorkspaceRootBox.Text, publication, _cts.Token);
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Status = publication.Status,
                Publication = publication,
                PublicationReceiptPath = receiptPath,
                ArtifactAcquisitionPerformedByPublication = false,
                RuntimeMaterializationPerformedByPublication = false,
                RuntimeExecutionPerformedByPublication = false,
                ModelInvocationPerformedByPublication = false,
                ForcePushPerformed = false,
                ArbitraryRemoteUsed = false,
                ArbitraryRefUsed = false,
                AutomaticRetryPerformed = false,
                IntermediateV055TagPublished = false
            });
            OutputTabs.SelectedItem = AcceptanceTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: published accepted v0.55.1; main={publication.RemoteMainAfter}; tag={publication.AcceptedTag}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  publish.v0551 completed main={publication.RemoteMainAfter}; tag={publication.AcceptedTag}; force=false; intermediateV055=false");
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally
        {
            if (beganRun) EndRun();
            SetV035PrimaryControlsEnabled(true);
            OperatorSurfaceV045Contract.Apply(this);
        }
    }
}
