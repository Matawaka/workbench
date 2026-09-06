using System.IO;
using System.Text;
using System.Windows;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalCheckpointV0552Service _checkpointV0552Service = new();
    private readonly FixedGitHubPublicationV0552Service _publicationV0552Service = new();
    private bool _v0552LoadedBootstrapChecked;

    internal void ConfigureV0552AcceptanceRouting()
    {
        ConfigureV055AcceptanceRouting();
        Loaded -= Window_LoadedV055;
        Loaded -= Window_LoadedV0552;
        Loaded += Window_LoadedV0552;
        PublishAcceptedButton.Click -= PublishAcceptedV055Button_Click;
        PublishAcceptedButton.Click -= PublishAcceptedV0552Button_Click;
        PublishAcceptedButton.Click += PublishAcceptedV0552Button_Click;
        Title = "Matawaka Workbench v0.55.2";
    }

    private async void Window_LoadedV0552(object sender, RoutedEventArgs e)
    {
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
        if (_v0552LoadedBootstrapChecked) return;
        _v0552LoadedBootstrapChecked = true;

        TransitionBootstrapV040Claim? claim = null;
        var beganRun = false;
        try
        {
            claim = await _transitionBootstrapV040Service.TryClaimFirstBootAsync(
                WorkspaceRootBox.Text,
                LocalCheckpointV0552Service.Version,
                LocalCheckpointV0552Service.TargetTag,
                CancellationToken.None);
            if (claim is null)
            {
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v0552 none; automaticValidation=false; automaticAccept=false");
                return;
            }

            SetV035PrimaryControlsEnabled(false);
            OperatorSurfaceV045Contract.Apply(this);
            BeginRun($"first-boot-bootstrap-v0.55.2-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;
            StatusText.Text = $"RUNNING: v0.55.2 convergence validation; lease={claim.Lease.LeaseId}";

            var tested = await RunV0552AcceptanceArtifactAsync(_cts!.Token);
            _lastAcceptanceReceipt = tested.Receipt;
            _lastAcceptanceArtifactPath = tested.ArtifactPath;
            _lastAcceptanceConsumed = false;

            if (!tested.Receipt.Passed)
            {
                await _transitionBootstrapV040Service.MarkFailedNoRetryAsync(
                    claim.Lease, claim.LeasePath, "v0.55.2 validation returned Passed=false", CancellationToken.None);
                _currentTerminalState = CommandTerminalState.Failed;
                StatusText.Text = "FAILED: v0.55.2 validation did not pass; automatic local Accept refused; no retry authority";
                AcceptanceTextBox.Text = CommandCodec.Serialize(new
                {
                    Bootstrap = claim.Lease,
                    Acceptance = tested.Receipt,
                    tested.ArtifactPath,
                    AutomaticAcceptPerformed = false,
                    NetworkAccessPerformedByAcceptance = false,
                    ModelInvocationPerformed = false,
                    AutomaticPublishPerformed = false
                });
                OutputTabs.SelectedItem = AcceptanceTab;
                return;
            }

            var candidate = await _checkpointV0552Service.PreviewAsync(
                WorkspaceRootBox.Text, tested.ArtifactPath, tested.Receipt, _cts.Token);
            var checkpoint = await _checkpointV0552Service.AcceptFromBootstrapAsync(
                candidate, claim.Lease.LeaseId, _cts.Token);
            var checkpointPath = await LocalCheckpointV0552Service.WriteReceiptAsync(
                WorkspaceRootBox.Text, checkpoint, _cts.Token);
            var completed = await _transitionBootstrapV040Service.FinalizeAcceptedAsync(
                claim, tested.ArtifactPath, checkpointPath, _cts.Token);

            _lastAcceptanceConsumed = true;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: v0.55.2 validation PASS + two-parent local Accept; {checkpoint.Tag} -> {checkpoint.NewHead}";
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Bootstrap = completed,
                Acceptance = tested.Receipt,
                AcceptanceArtifactPath = tested.ArtifactPath,
                Checkpoint = checkpoint,
                CheckpointReceiptPath = checkpointPath,
                FirstParent = LocalCheckpointV0552Service.FirstParentCommit,
                SecondParent = LocalCheckpointV0552Service.SecondParentCommit,
                RecoveryReceiptSha256 = WorkbenchConvergenceEvidenceVerifierV0552.RecoveryReceiptSha256,
                PublicImportReceiptSha256 = WorkbenchConvergenceEvidenceVerifierV0552.PublicImportReceiptSha256,
                RealHostV055InvocationAdmissionRequired = true,
                Public82ProvenanceLeaseReinterpretedAsModelAuthority = false,
                HistoricalV055TitleReceiptReinterpreted = false,
                FixedPublicationRemote = FixedGitHubPublicationV0552Service.RemoteUrl,
                ForcePushAllowed = false,
                ArbitraryRemoteOrRefAllowed = false,
                ArtifactAcquisitionPerformedByAcceptance = false,
                RuntimeMaterializationPerformedByAcceptance = false,
                RuntimeExecutionPerformedByAcceptance = false,
                ModelInvocationPerformedByAcceptance = false,
                NetworkAccessPerformedByAcceptance = false,
                AutomaticPublishPerformed = false,
                NextExplicitAction = "Publish accepted -> review local no-network v0.55.2 preview -> explicit Yes"
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

    private async Task<(WorkbenchAcceptanceReceipt Receipt, string ArtifactPath)> RunV0552AcceptanceArtifactAsync(CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(this);
        var context = new RuntimeContext(CatalogRootBox.Text, true, false);
        var receipt = await new WorkbenchV0552AcceptanceHarness(_acceptanceHarness, this).RunAsync(context, cancellationToken);
        var dir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"v0.55.2-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, CommandCodec.Serialize(receipt), new UTF8Encoding(false), cancellationToken);
        return (receipt, path);
    }

    internal IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> ObserveV0552ConvergenceAdmissionContract()
    {
        try
        {
            var admission = RealHostModelInvocationAdmissionVerifierV0551.FindExact(WorkspaceRootBox.Text);
            var convergence = WorkbenchConvergenceEvidenceVerifierV0552.FindExact(WorkspaceRootBox.Text);
            return new[]
            {
                ("realhost-v055-invocation", admission.ExecutionReceiptSha256 == RealHostModelInvocationAdmissionVerifierV0551.ExpectedExecutionReceiptSha256,
                    admission.ExecutionReceiptSha256, RealHostModelInvocationAdmissionVerifierV0551.ExpectedExecutionReceiptSha256),
                ("realhost-v055-terminal-lease", admission.LeaseStateSha256 == RealHostModelInvocationAdmissionVerifierV0551.ExpectedLeaseStateSha256,
                    admission.LeaseStateSha256, RealHostModelInvocationAdmissionVerifierV0551.ExpectedLeaseStateSha256),
                ("realhost-v055-output", admission.OutputArtifactSha256 == RealHostModelInvocationAdmissionVerifierV0551.ExpectedOutputSha256 && admission.OutputBytes == 12,
                    $"{admission.OutputBytes}/{admission.OutputArtifactSha256}", $"12/{RealHostModelInvocationAdmissionVerifierV0551.ExpectedOutputSha256}"),
                ("recovery-v0551", convergence.RecoveryReceiptSha256 == WorkbenchConvergenceEvidenceVerifierV0552.RecoveryReceiptSha256,
                    convergence.RecoveryReceiptSha256, WorkbenchConvergenceEvidenceVerifierV0552.RecoveryReceiptSha256),
                ("public-import-v0552", convergence.PublicImportReceiptSha256 == WorkbenchConvergenceEvidenceVerifierV0552.PublicImportReceiptSha256,
                    convergence.PublicImportReceiptSha256, WorkbenchConvergenceEvidenceVerifierV0552.PublicImportReceiptSha256),
                ("public-object-v0552", convergence.ImportedPublicCommit == LocalCheckpointV0552Service.SecondParentCommit,
                    convergence.ImportedPublicCommit, LocalCheckpointV0552Service.SecondParentCommit),
                ("authority-classes-distinct", true,
                    "public #82 provenance-bound runtime execution != accepted local v0.55 model invocation",
                    "distinct schemas/types; no reinterpretation")
            };
        }
        catch (Exception ex)
        {
            return new[] { ("v0552-convergence-admission", false, ex.Message, "exact real-host + recovery + public-import evidence") };
        }
    }

    private async void PublishAcceptedV0552Button_Click(object sender, RoutedEventArgs e)
    {
        FixedGitHubPublicationCandidateV0552 candidate;
        try
        {
            candidate = await _publicationV0552Service.PreviewAsync(WorkspaceRootBox.Text, CancellationToken.None);
        }
        catch (Exception ex)
        {
            ShowInvalid(ex is InvalidDataException data ? data : new InvalidDataException("V0552_PUBLICATION_PREVIEW_REFUSED: " + ex.Message, ex));
            return;
        }

        var text = new StringBuilder();
        text.AppendLine("Publish exact accepted Workbench v0.55.2 convergence commit to fixed GitHub remote?");
        text.AppendLine();
        text.AppendLine($"Accepted HEAD: {candidate.Head}");
        text.AppendLine($"Parent 1 — local accepted v0.55: {candidate.FirstParent}");
        text.AppendLine($"Parent 2 — public #82 main: {candidate.SecondParent}");
        text.AppendLine($"Current accepted tag: {candidate.AcceptedTag}");
        text.AppendLine($"Fixed remote: {candidate.RemoteUrl}");
        text.AppendLine($"Required current remote main: {candidate.ExpectedRemoteBase}");
        text.AppendLine("Local workbench-v0.55-accepted and failed workbench-v0.55.1-accepted will NOT be published.");
        text.AppendLine();
        text.AppendLine("Exact evidence:");
        text.AppendLine($"  real-host execution receipt SHA-256: {candidate.Admission.ExecutionReceiptSha256}");
        text.AppendLine($"  terminal lease SHA-256: {candidate.Admission.LeaseStateSha256}");
        text.AppendLine($"  recovery receipt SHA-256: {candidate.Convergence.RecoveryReceiptSha256}");
        text.AppendLine($"  public-import receipt SHA-256: {candidate.Convergence.PublicImportReceiptSha256}");
        text.AppendLine();
        text.AppendLine("No network operation has been performed by this preview. YES is the first publication network effect. It permits only the exact two-parent accepted HEAD -> refs/heads/main by normal fast-forward push and the exact workbench-v0.55.2-accepted tag -> the same fixed repository. No force, arbitrary remote/ref, source mutation, acquisition, materialization, runtime/model invocation, retry, benchmark, game/display/send or authority creation.");

        if (MessageBox.Show(this, text.ToString(), "Publish accepted v0.55.2", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  publish.v0552 cancelled effect=false; previewNetwork=false");
            return;
        }

        var beganRun = false;
        try
        {
            SetV035PrimaryControlsEnabled(false);
            BeginRun($"publish-v0.55.2-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;
            StatusText.Text = "RUNNING: fixed fast-forward publication of exact two-parent accepted v0.55.2";
            var publication = await _publicationV0552Service.PublishAsync(candidate, _cts!.Token);
            var receiptPath = await FixedGitHubPublicationV0552Service.WriteReceiptAsync(WorkspaceRootBox.Text, publication, _cts.Token);
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
                IntermediateV055TagPublished = false,
                FailedV0551TagPublished = false
            });
            OutputTabs.SelectedItem = AcceptanceTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: published accepted v0.55.2; main={publication.RemoteMainAfter}; tag={publication.AcceptedTag}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  publish.v0552 completed main={publication.RemoteMainAfter}; tag={publication.AcceptedTag}; force=false");
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
