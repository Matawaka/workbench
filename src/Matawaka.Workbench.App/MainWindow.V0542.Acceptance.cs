using System.IO;
using System.Text;
using System.Windows;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalCheckpointV0542Service _checkpointV0542Service = new();
    private readonly FixedGitHubPublicationV0542Service _publicationV0542Service = new();
    private bool _v0542LoadedBootstrapChecked;

    internal void ConfigureV0542AcceptanceRouting()
    {
        ConfigureV0541AcceptanceRouting();
        Loaded -= Window_LoadedV0541;
        Loaded -= Window_LoadedV0542;
        Loaded += Window_LoadedV0542;
        PublishAcceptedButton.Click -= PublishAcceptedV0541Button_Click;
        PublishAcceptedButton.Click -= PublishAcceptedV0542Button_Click;
        PublishAcceptedButton.Click += PublishAcceptedV0542Button_Click;
        Title = "Matawaka Workbench v0.54.2";
    }

    private async void Window_LoadedV0542(object sender, RoutedEventArgs e)
    {
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
        if (_v0542LoadedBootstrapChecked) return;
        _v0542LoadedBootstrapChecked = true;

        TransitionBootstrapV040Claim? claim = null;
        var beganRun = false;
        try
        {
            claim = await _transitionBootstrapV040Service.TryClaimFirstBootAsync(
                WorkspaceRootBox.Text,
                LocalCheckpointV0542Service.Version,
                LocalCheckpointV0542Service.TargetTag,
                CancellationToken.None);
            if (claim is null)
            {
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v0542 none; automaticValidation=false; automaticAccept=false");
                return;
            }

            SetV035PrimaryControlsEnabled(false);
            OperatorSurfaceV045Contract.Apply(this);
            BeginRun($"first-boot-bootstrap-v0.54.2-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;
            StatusText.Text = $"RUNNING: v0.54.2 real-host materialization admission + fixed publication validation; lease={claim.Lease.LeaseId}";

            var tested = await RunV0542AcceptanceArtifactAsync(_cts!.Token);
            _lastAcceptanceReceipt = tested.Receipt;
            _lastAcceptanceArtifactPath = tested.ArtifactPath;
            _lastAcceptanceConsumed = false;

            if (!tested.Receipt.Passed)
            {
                await _transitionBootstrapV040Service.MarkFailedNoRetryAsync(
                    claim.Lease, claim.LeasePath, "v0.54.2 validation returned Passed=false", CancellationToken.None);
                _currentTerminalState = CommandTerminalState.Failed;
                StatusText.Text = "FAILED: v0.54.2 validation did not pass; automatic local Accept refused; no retry authority";
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

            var candidate = await _checkpointV0542Service.PreviewAsync(
                WorkspaceRootBox.Text, tested.ArtifactPath, tested.Receipt, _cts.Token);
            var checkpoint = await _checkpointV0542Service.AcceptFromBootstrapAsync(
                candidate, claim.Lease.LeaseId, _cts.Token);
            var checkpointPath = await LocalCheckpointV0542Service.WriteReceiptAsync(
                WorkspaceRootBox.Text, checkpoint, _cts.Token);
            var completed = await _transitionBootstrapV040Service.FinalizeAcceptedAsync(
                claim, tested.ArtifactPath, checkpointPath, _cts.Token);

            _lastAcceptanceConsumed = true;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: v0.54.2 validation PASS + automatic local Accept; {checkpoint.Tag} -> {checkpoint.NewHead}";
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Bootstrap = completed,
                Acceptance = tested.Receipt,
                AcceptanceArtifactPath = tested.ArtifactPath,
                Checkpoint = checkpoint,
                CheckpointReceiptPath = checkpointPath,
                RealHostMaterializationAdmissionRequired = true,
                FixedPublicationRemote = FixedGitHubPublicationV0542Service.RemoteUrl,
                FixedPublicationExpectedRemoteBase = FixedGitHubPublicationV0542Service.ExpectedRemoteBase,
                ForcePushAllowed = false,
                ArbitraryRemoteOrRefAllowed = false,
                RuntimeMaterializationPerformedByAcceptance = false,
                ArtifactAcquisitionPerformedByAcceptance = false,
                RuntimeExecutionPerformedByAcceptance = false,
                ModelRequestPerformedByAcceptance = false,
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

    private async Task<(WorkbenchAcceptanceReceipt Receipt, string ArtifactPath)> RunV0542AcceptanceArtifactAsync(CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(this);
        var context = new RuntimeContext(CatalogRootBox.Text, true, false);
        var receipt = await new WorkbenchV0542AcceptanceHarness(_acceptanceHarness, this).RunAsync(context, cancellationToken);
        var dir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"v0.54.2-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, CommandCodec.Serialize(receipt), new UTF8Encoding(false), cancellationToken);
        return (receipt, path);
    }

    internal IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> ObserveV0542PublicationAdmissionContract()
    {
        try
        {
            var admission = RealHostMaterializationAdmissionVerifierV0542.FindExact(WorkspaceRootBox.Text);
            return new[]
            {
                ("realhost-materialization-admission", true, admission.MaterializationReceiptSha256, "exact v0.54.1 real-host materialization evidence"),
                ("realhost-materialization-tree", admission.TreeDigestSha256 == RealHostMaterializationAdmissionVerifierV0542.ExpectedTreeDigestSha256,
                    admission.TreeDigestSha256, RealHostMaterializationAdmissionVerifierV0542.ExpectedTreeDigestSha256),
                ("realhost-materialization-no-execution", true, "materialization receipt requires process/runtime/network/model/benchmark/game=false", "true")
            };
        }
        catch (Exception ex)
        {
            return new[] { ("realhost-materialization-admission", false, ex.Message, "exact v0.54.1 real-host RUNTIME_TREE_MATERIALIZATION_VERIFIED evidence") };
        }
    }

    private async void PublishAcceptedV0542Button_Click(object sender, RoutedEventArgs e)
    {
        FixedGitHubPublicationCandidateV0542 candidate;
        try
        {
            candidate = await _publicationV0542Service.PreviewAsync(WorkspaceRootBox.Text, CancellationToken.None);
        }
        catch (Exception ex)
        {
            ShowInvalid(ex is InvalidDataException data ? data : new InvalidDataException("V0542_PUBLICATION_PREVIEW_REFUSED: " + ex.Message, ex));
            return;
        }

        var text = new StringBuilder();
        text.AppendLine("Publish exact accepted Workbench v0.54.2 to fixed GitHub remote?");
        text.AppendLine();
        text.AppendLine($"Accepted HEAD: {candidate.Head}");
        text.AppendLine($"Exact parent: {candidate.Parent} / {LocalCheckpointV0542Service.ExpectedPredecessorTag}");
        text.AppendLine($"Current tag: {candidate.AcceptedTag}");
        text.AppendLine($"Fixed remote: {candidate.RemoteName} -> {candidate.RemoteUrl}");
        text.AppendLine($"Required last public base: {candidate.ExpectedRemoteBase}");
        text.AppendLine();
        text.AppendLine("Real-host materialization admission:");
        text.AppendLine($"  receipt: {candidate.Admission.MaterializationReceiptPath}");
        text.AppendLine($"  receipt SHA-256: {candidate.Admission.MaterializationReceiptSha256}");
        text.AppendLine($"  request: {candidate.Admission.RequestId}");
        text.AppendLine($"  transaction: {candidate.Admission.TransactionId}");
        text.AppendLine($"  manifest SHA-256: {candidate.Admission.RuntimeManifestSha256}");
        text.AppendLine($"  tree SHA-256: {candidate.Admission.TreeDigestSha256}");
        text.AppendLine();
        text.AppendLine("No network operation has been performed by this preview. YES is the first remote/network operation. It permits only exact accepted HEAD -> refs/heads/main fast-forward and the current accepted tag -> the same fixed repository. No force push, arbitrary remote/ref, intermediate tag promotion, acquisition, materialization, process execution, model request or automatic retry.");

        if (MessageBox.Show(this, text.ToString(), "Publish accepted v0.54.2", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  publish.v0542 cancelled effect=false; previewNetwork=false");
            return;
        }

        var beganRun = false;
        try
        {
            SetV035PrimaryControlsEnabled(false);
            BeginRun($"publish-v0.54.2-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;
            StatusText.Text = "RUNNING: fixed publication of exact accepted v0.54.2; no force; exact remote/ref only";
            var publication = await _publicationV0542Service.PublishAsync(candidate, _cts!.Token);
            var receiptPath = await FixedGitHubPublicationV0542Service.WriteReceiptAsync(WorkspaceRootBox.Text, publication, _cts.Token);
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Status = publication.Status,
                Publication = publication,
                PublicationReceiptPath = receiptPath,
                RuntimeMaterializationPerformedByPublication = false,
                ArtifactAcquisitionPerformedByPublication = false,
                RuntimeExecutionPerformedByPublication = false,
                ModelRequestPerformedByPublication = false,
                ForcePushPerformed = false,
                ArbitraryRemoteUsed = false
            });
            OutputTabs.SelectedItem = AcceptanceTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: published accepted v0.54.2; main={publication.RemoteMainAfter}; tag={publication.AcceptedTag}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  publish.v0542 completed main={publication.RemoteMainAfter}; tag={publication.AcceptedTag}; force=false");
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
