using System.IO;
using System.Text;
using System.Windows;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalCheckpointV052Service _checkpointV052Service = new();
    private bool _v052LoadedBootstrapChecked;

    internal void ConfigureV052AcceptanceRouting()
    {
        ConfigureV05113AcceptanceRouting();
        Loaded -= Window_LoadedV05113;
        Loaded += Window_LoadedV052;
        PublishAcceptedButton.Click -= PublishAcceptedV05113Button_Click;
        PublishAcceptedButton.Click += PublishAcceptedV052Button_Click;
    }

    private async void Window_LoadedV052(object sender, RoutedEventArgs e)
    {
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
        if (_v052LoadedBootstrapChecked) return;
        _v052LoadedBootstrapChecked = true;

        TransitionBootstrapV040Claim? claim = null;
        var beganRun = false;
        try
        {
            claim = await _transitionBootstrapV040Service.TryClaimFirstBootAsync(
                WorkspaceRootBox.Text,
                LocalCheckpointV052Service.Version,
                LocalCheckpointV052Service.TargetTag,
                CancellationToken.None);
            if (claim is null)
            {
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v052 none; automaticValidation=false; automaticAccept=false");
                return;
            }

            SetV035PrimaryControlsEnabled(false);
            OperatorSurfaceV045Contract.Apply(this);
            BeginRun($"first-boot-bootstrap-v0.52-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;
            StatusText.Text = $"RUNNING: v0.52 bounded artifact acquisition validation; lease={claim.Lease.LeaseId}";

            var tested = await RunV052AcceptanceArtifactAsync(_cts!.Token);
            _lastAcceptanceReceipt = tested.Receipt;
            _lastAcceptanceArtifactPath = tested.ArtifactPath;
            _lastAcceptanceConsumed = false;

            if (!tested.Receipt.Passed)
            {
                await _transitionBootstrapV040Service.MarkFailedNoRetryAsync(
                    claim.Lease, claim.LeasePath, "v0.52 validation returned Passed=false", CancellationToken.None);
                _currentTerminalState = CommandTerminalState.Failed;
                StatusText.Text = "FAILED: v0.52 artifact acquisition validation did not pass; automatic local Accept refused; no retry authority";
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

            var candidate = await _checkpointV052Service.PreviewAsync(
                WorkspaceRootBox.Text, tested.ArtifactPath, tested.Receipt, _cts.Token);
            var checkpoint = await _checkpointV052Service.AcceptFromBootstrapAsync(
                candidate, claim.Lease.LeaseId, _cts.Token);
            var checkpointPath = await LocalCheckpointV052Service.WriteReceiptAsync(
                WorkspaceRootBox.Text, checkpoint, _cts.Token);
            var completed = await _transitionBootstrapV040Service.FinalizeAcceptedAsync(
                claim, tested.ArtifactPath, checkpointPath, _cts.Token);

            _lastAcceptanceConsumed = true;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: v0.52 validation PASS + automatic local Accept; {checkpoint.Tag} -> {checkpoint.NewHead}";
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Bootstrap = completed,
                Acceptance = tested.Receipt,
                AcceptanceArtifactPath = tested.ArtifactPath,
                Checkpoint = checkpoint,
                CheckpointReceiptPath = checkpointPath,
                GenericArtifactAcquisitionPrimitive = true,
                RequestSchema = BoundedArtifactAcquisitionV052Service.RequestSchema,
                SelectedEqualsAuthority = false,
                PreviewEqualsAuthority = false,
                OneShotCallBudget = 1,
                CredentialFreeHttpsOnly = true,
                RedirectPolicyRevalidated = true,
                FixedExternalToGitDestinationRequired = true,
                ReparseRefused = true,
                PartialBeforeVerification = true,
                ExactSizeRequired = true,
                ExactSha256Required = true,
                AtomicPromotionAfterVerificationOnly = true,
                AutomaticRetryOrResume = false,
                ExtractionAuthority = false,
                ProcessExecutionAuthority = false,
                RuntimeStartAuthority = false,
                BenchmarkAuthority = false,
                ModelRequestAuthority = false,
                GameAccessAuthority = false,
                KONTURSpecificRuntimeBehavior = false,
                V05113McpLifecyclePreserved = true,
                AutomaticPublishPerformed = false,
                AutomaticLifecyclePerformed = false,
                PublicRemotePublicationStillDeferred = true,
                NextExplicitActions = new[]
                {
                    "Run one small exact public-artifact acquisition real-host smoke request",
                    "Require ACQUISITION_VERIFIED with exact local size/SHA-256 and no extraction/execution",
                    "Then prepare KONTUR LM1/LM3-A requests as separate human-reviewed acquisition authorities"
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

    private async Task<(WorkbenchAcceptanceReceipt Receipt, string ArtifactPath)> RunV052AcceptanceArtifactAsync(
        CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(this);
        var context = new RuntimeContext(CatalogRootBox.Text, true, false);
        var receipt = await new WorkbenchV052AcceptanceHarness(_acceptanceHarness, this).RunAsync(context, cancellationToken);
        var dir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"v0.52-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, CommandCodec.Serialize(receipt), new UTF8Encoding(false), cancellationToken);
        return (receipt, path);
    }

    private void PublishAcceptedV052Button_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "Remote publication is intentionally deferred.\n\nLocal v0.52 adds a provider-neutral bounded artifact-acquisition primitive over the locally accepted v0.51.13 frontier. It introduces no general network, extraction, execution, runtime, model or game authority. Public main remains deferred.\n\nNo GitHub mutation was performed.",
            "Publish accepted v0.52 — deferred",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  publish.v052.deferred effect=false; reason=public-v052-gate-unresolved");
    }
}
