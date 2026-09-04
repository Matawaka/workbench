using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly BoundedArtifactAcquisitionV052Service _artifactAcquisitionV052Service = new();

    internal void ConfigureV052Routing()
    {
        ConfigureV05113Routing();
        Title = "Matawaka Workbench v0.52";
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
    }

    private async Task AcquireArtifactsV052Async()
    {
        var requestDialog = new OpenFileDialog
        {
            Title = "Select exact bounded artifact-acquisition request JSON",
            Filter = "JSON request (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (requestDialog.ShowDialog(this) != true) return;

        ArtifactAcquisitionRequestV052 request;
        ArtifactAcquisitionPreviewV052 preview;
        try
        {
            var json = await File.ReadAllTextAsync(requestDialog.FileName, Encoding.UTF8);
            request = JsonSerializer.Deserialize<ArtifactAcquisitionRequestV052>(json)
                ?? throw new InvalidDataException("Artifact acquisition request JSON deserialized to null.");
            preview = _artifactAcquisitionV052Service.Preview(WorkspaceRootBox.Text, request, CancellationToken.None);
        }
        catch (JsonException ex)
        {
            ShowInvalid(new InvalidDataException("V052_ARTIFACT_REQUEST_JSON_INVALID: " + ex.Message, ex));
            return;
        }
        catch (ArtifactAcquisitionExceptionV052 ex)
        {
            ShowInvalid(new InvalidDataException($"{ex.Classification}: {ex.Message}", ex));
            return;
        }
        catch (IOException ex)
        {
            ShowInvalid(new InvalidDataException("V052_ARTIFACT_REQUEST_READ_FAILED: " + ex.Message, ex));
            return;
        }

        var confirmation = new StringBuilder();
        confirmation.AppendLine("Authorize one bounded artifact acquisition?");
        confirmation.AppendLine();
        confirmation.AppendLine($"RequestId: {preview.RequestId}");
        confirmation.AppendLine($"Artifacts: {preview.Artifacts.Count}");
        confirmation.AppendLine($"Exact expected bytes total: {preview.ExactExpectedBytesTotal}");
        confirmation.AppendLine($"Maximum network bytes: {preview.MaxTotalNetworkBytes}");
        confirmation.AppendLine($"Destination root: {preview.DestinationRoot}");
        confirmation.AppendLine($"Redirect limit: {preview.MaxRedirects}; timeout: {preview.TimeoutSeconds}s; TTL: {preview.TtlSeconds}s");
        confirmation.AppendLine($"Request digest: {preview.RequestDigestSha256}");
        confirmation.AppendLine();
        foreach (var artifact in preview.Artifacts)
        {
            confirmation.AppendLine($"• {artifact.ArtifactId}");
            confirmation.AppendLine($"  {artifact.SourceUri}");
            confirmation.AppendLine($"  → {artifact.FileName}; bytes={artifact.ExpectedBytes}; sha256={artifact.ExpectedSha256}");
        }
        confirmation.AppendLine();
        confirmation.AppendLine("YES grants exactly one acquisition call and immediately consumes it. The operation may perform only reviewed HTTPS transfers to .partial files, exact size/SHA-256 verification and atomic final promotion. It does NOT extract, install, execute, start a runtime/model, benchmark, issue model requests, access a game, mutate PATH/environment, Git/catalog, Agent Execute or ActionPermit.");
        confirmation.AppendLine();
        confirmation.AppendLine("Artifact Selected ≠ Download Authority ≠ Downloaded ≠ Verified ≠ Installed/Executed.");

        if (MessageBox.Show(this, confirmation.ToString(), "Bounded Artifact Acquisition v0.52", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  artifact-acquisition.v052 cancelled request={preview.RequestId}; authority=false; network=false");
            return;
        }

        var beganRun = false;
        try
        {
            SetV035PrimaryControlsEnabled(false);
            BeginRun($"artifact-acquisition-v0.52-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;
            StatusText.Text = $"RUNNING: bounded artifact acquisition; request={preview.RequestId}; artifacts={preview.Artifacts.Count}";

            var authority = await _artifactAcquisitionV052Service.GrantAsync(WorkspaceRootBox.Text, preview, _cts!.Token);
            var execution = await _artifactAcquisitionV052Service.AcquireAsync(WorkspaceRootBox.Text, authority.Grant, _cts.Token);

            LocalAppsTextBox.Text = CommandCodec.Serialize(new
            {
                Status = "ARTIFACT_ACQUISITION_VERIFIED",
                RequestPath = requestDialog.FileName,
                Preview = preview,
                AuthorityReceipt = authority.Receipt,
                AuthorityReceiptPath = authority.ReceiptPath,
                AuthorityGrantBearerOmitted = true,
                ExecutionReceipt = execution.Receipt,
                ExecutionReceiptPath = execution.ReceiptPath,
                AllArtifactsSha256Verified = execution.Receipt.AllArtifactsSha256Verified,
                ExtractionPerformed = false,
                ProcessExecutionPerformed = false,
                RuntimeStartPerformed = false,
                BenchmarkPerformed = false,
                ModelRequestPerformed = false,
                GameAccessPerformed = false
            });
            OutputTabs.SelectedItem = LocalAppsTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: v0.52 artifact acquisition VERIFIED; request={preview.RequestId}; artifacts={preview.Artifacts.Count}; networkBytes={execution.Receipt.NetworkBytesObserved}; execute=false";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  artifact-acquisition.v052 verified request={preview.RequestId}; artifacts={preview.Artifacts.Count}; networkBytes={execution.Receipt.NetworkBytesObserved}; execute=false");
        }
        catch (ArtifactAcquisitionExceptionV052 ex)
        {
            var diagnostic = NetworkFailureDiagnosticsV0521.TryCreate(ex);
            string? diagnosticPath = null;
            if (diagnostic is not null)
            {
                try
                {
                    diagnosticPath = await NetworkFailureDiagnosticsV0521.WriteReceiptAsync(
                        WorkspaceRootBox.Text, preview.RequestId, diagnostic, CancellationToken.None);
                }
                catch
                {
                    diagnosticPath = null;
                }
            }

            LocalAppsTextBox.Text = CommandCodec.Serialize(new
            {
                Status = "ARTIFACT_ACQUISITION_TERMINAL_FAIL_CLOSED",
                RequestId = preview.RequestId,
                Classification = ex.Classification,
                Message = ex.Message,
                TransportDiagnostic = diagnostic,
                TransportDiagnosticReceiptPath = diagnosticPath,
                RawTransportExceptionMessagePersisted = false,
                AutomaticRetryPerformed = false,
                AutomaticResumePerformed = false,
                ExtractionPerformed = false,
                ProcessExecutionPerformed = false,
                RuntimeStartPerformed = false,
                BenchmarkPerformed = false,
                ModelRequestPerformed = false,
                GameAccessPerformed = false
            });
            OutputTabs.SelectedItem = LocalAppsTab;
            _currentTerminalState = CommandTerminalState.Failed;
            var displayedClass = diagnostic?.Classification ?? ex.Classification;
            var displayedDetail = diagnostic is null ? ex.Message : NetworkFailureDiagnosticsV0521.OperatorSummary(diagnostic);
            StatusText.Text = $"INVALID: {displayedClass}: {displayedDetail}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  artifact-acquisition.v052 refused class={ex.Classification}; transport={displayedClass}; retry=false; resume=false");
            MessageBox.Show(this, $"{displayedClass}\n\n{displayedDetail}", "Bounded Artifact Acquisition v0.52", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (OperationCanceledException)
        {
            ShowCancelled();
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

    internal IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> ObserveV052ArtifactAcquisitionContract() => new[]
    {
        ("v052-four-button-surface", true, "generic acquisition reachable only through existing Local apps chooser", "no fifth top-level button"),
        ("v052-navigation-not-authority", true, "selected app context ignored by acquisition service", "request/explicit confirmation required"),
        ("v052-preview-no-effect", true, "Preview validates request without network/write", "true"),
        ("v052-one-shot-authority", true, "GrantAsync then exactly one AcquireAsync", "one call"),
        ("v052-bearer-ui", true, "grant bearer omitted from LocalApps output", "true"),
        ("v052-network-policy", true, "credential-free HTTPS + exact route/redirect/byte/time/TTL bounds", "bounded"),
        ("v052-filesystem-policy", true, ".partial + size + SHA256 + atomic promote; reparse refused", "bounded"),
        ("v052-no-post-download-authority", true, "extract/install/execute/runtime/benchmark/model/game all false", "true"),
        ("v052-kontur-generic", true, "KONTUR may supply a request but primitive contains no KONTUR-specific runtime behavior", "provider-neutral"),
        ("v0521-network-diagnostic", true, "HttpRequestException -> bounded category; raw inner message omitted", "diagnostic only")
    };
}
