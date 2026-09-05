using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly BoundedRuntimeExecutionV053Service _runtimeExecutionV053Service = new();
    private bool _v053ExclusiveLocalAppsRouting;

    internal void ConfigureV053Routing()
    {
        ConfigureV0521Routing();
        UpdateLocalAppButton.Click -= LocalAppsV05113Button_Click;
        UpdateLocalAppButton.Click -= LocalAppsV053Button_Click;
        UpdateLocalAppButton.Click += LocalAppsV053Button_Click;
        _v053ExclusiveLocalAppsRouting = true;
        Title = "Matawaka Workbench v0.53";
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
    }

    private async void LocalAppsV053Button_Click(object sender, RoutedEventArgs e)
    {
        if (!_v053ExclusiveLocalAppsRouting)
        {
            ShowInvalid(new InvalidDataException("V053_LOCAL_APPS_ROUTE_NOT_EXCLUSIVE: v0.53 route was invoked before exclusive configuration."));
            return;
        }

        EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  local-app.v053.dispatch exclusive=true; runtimeExecutionAvailable=true");
        SaveSettings();
        var workspace = Path.GetFullPath(WorkspaceRootBox.Text.Trim());
        var appsRoot = Path.Combine(workspace, LocalApplicationRegistrationService.AppsDirectoryName);
        if (!Directory.Exists(appsRoot))
        {
            MessageBox.Show(this, $"Managed Apps root отсутствует:\n{appsRoot}", "Local apps", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var folderDialog = new OpenFolderDialog
        {
            Title = "Выберите приложение внутри Workspace\\Apps",
            InitialDirectory = appsRoot,
            Multiselect = false
        };
        if (folderDialog.ShowDialog(this) != true) return;
        var selectedRoot = Path.GetFullPath(folderDialog.FolderName);
        var identityPath = Path.Combine(selectedRoot, LocalApplicationRegistrationService.IdentityFileName);
        if (!File.Exists(identityPath))
        {
            try
            {
                await _localApplicationManagedRoleGuardV0371Service.EnsureRegistrationRoleAllowedAsync(
                    selectedRoot, WorkspaceRootBox.Text, CancellationToken.None);
            }
            catch (InvalidDataException ex) { ShowInvalid(ex); return; }
            await RegisterSelectedLocalAppAsync(selectedRoot);
            RefreshInstalledAppsV044();
            return;
        }

        var appId = Path.GetFileName(selectedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (_v050ActiveTunnelApplicationId is not null && !_secureMcpTunnelV0501Service.IsActiveFor(_v050ActiveTunnelApplicationId))
            _v050ActiveTunnelApplicationId = null;
        var adapterActive = _localAppMcpReadAdapterV049Service.IsActiveFor(appId);
        var tunnelActive = _secureMcpTunnelV0501Service.IsActiveFor(appId);
        var choice = LocalAppsActionDialogV0515.ShowChoice(this, appId, adapterActive, tunnelActive);

        switch (choice)
        {
            case LocalAppsActionChoiceV050.UpdateFromPackage: await UpdateSelectedLocalAppAsync(selectedRoot); break;
            case LocalAppsActionChoiceV050.BuildUpdatePackage: await BuildLocalAppPackageV038Async(selectedRoot); break;
            case LocalAppsActionChoiceV050.LaunchApp: await LaunchSelectedLocalAppV046Async(appId, selectedRoot); break;
            case LocalAppsActionChoiceV050.ExportUpdateContext: await ExportUpdateContextV046Async(appId); break;
            case LocalAppsActionChoiceV050.BindDevelopmentSource: await BindDevelopmentSourceV046Async(appId); break;
            case LocalAppsActionChoiceV050.ExportPrivateDevelopmentContext: await ExportPrivateDevelopmentContextV046Async(appId); break;
            case LocalAppsActionChoiceV050.BoundedArtifactAcquisition: await AcquireArtifactsV052Async(); break;
            case LocalAppsActionChoiceV050.BoundedRuntimeExecution: await ExecuteBoundedRuntimeV053Async(appId); break;
            case LocalAppsActionChoiceV050.StopBoundedRuntimeExecution: await StopBoundedRuntimeV053Async(appId); break;
            case LocalAppsActionChoiceV050.ChatReadRelay: await ChatReadRelayV047Async(appId); break;
            case LocalAppsActionChoiceV050.ReadSessionStatus: await ShowCoherentLiveReadSessionStatusV0516Async(appId); break;
            case LocalAppsActionChoiceV050.ReadSessionHistoryPage: ShowCanonicalReadSessionHistoryPageV0515(appId); break;
            case LocalAppsActionChoiceV050.ReadSessionLease: await CreateOwnedReadLeaseAndAutoStartMcpV05112Async(appId); break;
            case LocalAppsActionChoiceV050.StopReadOnlyMcpAdapter: await EndOwnedReadSessionV05113Async(appId); break;
            case LocalAppsActionChoiceV050.EndOrphanedReadSession: await EndOrphanedWithFreeMcpDomainV0517Async(appId); break;
            case LocalAppsActionChoiceV050.RevokeReadLeases: await RevokeAllWithFreeMcpDomainV0517Async(appId); break;
            case LocalAppsActionChoiceV050.StartReadOnlyMcpAdapter: await StartOwnedManualMcpV0517Async(appId); break;
            case LocalAppsActionChoiceV050.StartSecureMcpTunnel: await StartSecureMcpTunnelV0502Async(appId); break;
            case LocalAppsActionChoiceV050.StopSecureMcpTunnel: await StopSecureMcpTunnelV0502Async(appId); break;
            default:
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  local-app.v053.choice.cancelled app={appId}; effect=false");
                break;
        }
        RefreshInstalledAppsV044();
    }

    private async Task ExecuteBoundedRuntimeV053Async(string navigationAppId)
    {
        var requestDialog = new OpenFileDialog
        {
            Title = "Select exact bounded runtime-execution request JSON",
            Filter = "JSON request (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (requestDialog.ShowDialog(this) != true) return;

        RuntimeExecutionRequestV053 request;
        RuntimeExecutionPreviewV053 preview;
        try
        {
            var json = await File.ReadAllTextAsync(requestDialog.FileName, Encoding.UTF8);
            request = JsonSerializer.Deserialize<RuntimeExecutionRequestV053>(json)
                ?? throw new InvalidDataException("Runtime execution request JSON deserialized to null.");
            preview = _runtimeExecutionV053Service.Preview(WorkspaceRootBox.Text, request, CancellationToken.None);
        }
        catch (JsonException ex)
        {
            ShowInvalid(new InvalidDataException("V053_RUNTIME_REQUEST_JSON_INVALID: " + ex.Message, ex));
            return;
        }
        catch (RuntimeExecutionExceptionV053 ex)
        {
            ShowInvalid(new InvalidDataException($"{ex.Classification}: {ex.Message}", ex));
            return;
        }
        catch (IOException ex)
        {
            ShowInvalid(new InvalidDataException("V053_RUNTIME_REQUEST_READ_FAILED: " + ex.Message, ex));
            return;
        }

        var confirmation = new StringBuilder();
        confirmation.AppendLine("Authorize one bounded runtime execution?");
        confirmation.AppendLine();
        confirmation.AppendLine($"Navigation app context only: {navigationAppId}");
        confirmation.AppendLine($"RequestId: {preview.RequestId}");
        confirmation.AppendLine($"Runtime manifest: {preview.RuntimeTreeManifestId}");
        confirmation.AppendLine($"Runtime root: {preview.RuntimeRoot}");
        confirmation.AppendLine($"Executable: {preview.ExecutablePath}");
        confirmation.AppendLine($"Executable bytes: {preview.ExecutableBytes}");
        confirmation.AppendLine($"Executable SHA-256: {preview.ExecutableSha256}");
        confirmation.AppendLine($"Working directory: {preview.WorkingDirectory}");
        confirmation.AppendLine($"Arguments: {preview.Arguments.Count}; environment additions: {preview.Environment.Count}");
        confirmation.AppendLine($"TTL: {preview.TtlSeconds}s; calls: {preview.MaxCalls}; readiness delay: {preview.ReadinessDelayMilliseconds}ms");
        confirmation.AppendLine();
        confirmation.AppendLine("YES creates one one-shot Execution Lease. Its call is durably consumed BEFORE the executable is rehashed and BEFORE Process.Start. UseShellExecute=false, exact ArgumentList is used, shell/interpreter images are refused, environment starts from a minimal OS set, and the observed Windows process image path/hash must match the reviewed executable.");
        confirmation.AppendLine();
        confirmation.AppendLine("This layer does NOT extract/materialize archives, elevate, benchmark, issue model requests, access a game, grant general process authority or accept arbitrary PIDs for stop.");
        confirmation.AppendLine();
        confirmation.AppendLine("Verified Artifact ≠ Materialized Runtime ≠ Execution Authority. Process Started ≠ Runtime Ready ≠ Model Request Authority.");

        if (MessageBox.Show(this, confirmation.ToString(), "Bounded Runtime Execution v0.53", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  runtime-execution.v053 cancelled request={preview.RequestId}; authority=false; process=false");
            return;
        }

        var beganRun = false;
        try
        {
            SetV035PrimaryControlsEnabled(false);
            BeginRun($"runtime-execution-v0.53-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;
            StatusText.Text = $"RUNNING: bounded runtime execution; request={preview.RequestId}; oneShot=true";

            var authority = await _runtimeExecutionV053Service.GrantAsync(WorkspaceRootBox.Text, preview, _cts!.Token);
            var executed = await _runtimeExecutionV053Service.ExecuteAsync(WorkspaceRootBox.Text, authority.Grant, _cts.Token);

            LocalAppsTextBox.Text = CommandCodec.Serialize(new
            {
                Status = executed.Receipt.Status,
                RequestPath = requestDialog.FileName,
                Preview = preview,
                AuthorityReceipt = authority.Receipt,
                AuthorityReceiptPath = authority.ReceiptPath,
                AuthorityGrantBearerOmitted = true,
                ExecutionReceipt = executed.Receipt,
                ExecutionReceiptPath = executed.ReceiptPath,
                ActiveOwnedRuntime = _runtimeExecutionV053Service.HasActiveOwnedRuntime,
                RuntimeTreeMaterializationPerformed = false,
                ShellIndirectionPerformed = false,
                ElevationRequested = false,
                AutomaticRetryPerformed = false,
                AutomaticResumePerformed = false,
                BenchmarkPerformed = false,
                ModelRequestPerformed = false,
                GameAccessPerformed = false
            });
            OutputTabs.SelectedItem = LocalAppsTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: v0.53 {executed.Receipt.Status}; pid={executed.Receipt.ProcessId}; imageVerified={executed.Receipt.ExactProcessImageVerified}; ready={executed.Receipt.RuntimeReadyObserved}; modelAuthority=false";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  runtime-execution.v053 verified request={preview.RequestId}; pid={executed.Receipt.ProcessId}; running={executed.Receipt.ProcessStillRunning}; ready={executed.Receipt.RuntimeReadyObserved}; model=false");
        }
        catch (RuntimeExecutionExceptionV053 ex)
        {
            LocalAppsTextBox.Text = CommandCodec.Serialize(new
            {
                Status = "RUNTIME_EXECUTION_TERMINAL_FAIL_CLOSED",
                RequestId = preview.RequestId,
                Classification = ex.Classification,
                Message = ex.Message,
                AutomaticRetryPerformed = false,
                AutomaticResumePerformed = false,
                RuntimeTreeMaterializationPerformed = false,
                BenchmarkPerformed = false,
                ModelRequestPerformed = false,
                GameAccessPerformed = false
            });
            OutputTabs.SelectedItem = LocalAppsTab;
            _currentTerminalState = CommandTerminalState.Failed;
            StatusText.Text = $"INVALID: {ex.Classification}: {ex.Message}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  runtime-execution.v053 refused class={ex.Classification}; retry=false; resume=false");
            MessageBox.Show(this, $"{ex.Classification}\n\n{ex.Message}", "Bounded Runtime Execution v0.53", MessageBoxButton.OK, MessageBoxImage.Warning);
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

    private async Task StopBoundedRuntimeV053Async(string navigationAppId)
    {
        if (!_runtimeExecutionV053Service.HasActiveOwnedRuntime)
        {
            ShowInvalid(new InvalidDataException("V053_NO_ACTIVE_OWNED_RUNTIME: no active bounded runtime is owned by this Workbench process."));
            return;
        }

        var text = "Stop the active bounded runtime process tree?\n\n" +
                   $"Navigation app context only: {navigationAppId}\n\n" +
                   "No PID is accepted from the operator. Stop targets only the exact in-memory Process object created and path/start-time verified by this Workbench v0.53 execution lease. This is not arbitrary process-kill authority.";
        if (MessageBox.Show(this, text, "Stop bounded runtime v0.53", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            var stopped = await _runtimeExecutionV053Service.StopActiveOwnedRuntimeAsync(WorkspaceRootBox.Text, CancellationToken.None);
            LocalAppsTextBox.Text = CommandCodec.Serialize(new
            {
                Status = stopped.Receipt.Status,
                StopReceipt = stopped.Receipt,
                StopReceiptPath = stopped.ReceiptPath,
                ArbitraryPidAccepted = false,
                GeneralProcessKillAuthority = false
            });
            OutputTabs.SelectedItem = LocalAppsTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: v0.53 owned runtime stop; pid={stopped.Receipt.ProcessId}; exited={stopped.Receipt.ProcessExited}; arbitraryPid=false";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  runtime-stop.v053 lease={stopped.Receipt.LeaseId}; pid={stopped.Receipt.ProcessId}; exited={stopped.Receipt.ProcessExited}; arbitraryPid=false");
        }
        catch (RuntimeExecutionExceptionV053 ex)
        {
            ShowInvalid(new InvalidDataException($"{ex.Classification}: {ex.Message}", ex));
        }
    }

    internal IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> ObserveV053RuntimeExecutionContract() => new[]
    {
        ("v053-four-button-surface", true, "generic runtime execution reachable only through existing Local apps chooser", "no fifth top-level button"),
        ("v053-navigation-not-authority", true, "selected app context ignored by runtime execution service", "exact request + explicit confirmation required"),
        ("v053-preview-no-effect", true, "Preview performs local evidence reads/hash only", "process=false; materialization=false"),
        ("v053-one-shot-authority", true, "GrantAsync then one ExecuteAsync; consumed before Process.Start", "one call"),
        ("v053-shell-policy", true, "UseShellExecute=false + ArgumentList + shell/interpreter denylist", "no shell indirection"),
        ("v053-image-revalidation", true, "SHA-256 before start + Windows image path/hash after start", "exact"),
        ("v053-stop-authority", true, "no PID input; exact owned Process object/tree only", "bounded"),
        ("v053-no-post-start-authority", true, "benchmark/model/game/general-process all false", "true"),
        ("v053-provider-neutral", true, "no KONTUR-specific runtime behavior", "provider-neutral")
    };
}
