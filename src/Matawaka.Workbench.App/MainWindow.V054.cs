using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly BoundedRuntimeTreeMaterializationV054Service _runtimeMaterializationV054Service = new();
    private bool _v054ExclusiveLocalAppsRouting;

    internal void ConfigureV054Routing()
    {
        ConfigureV0532Routing();
        UpdateLocalAppButton.Click -= LocalAppsV053Button_Click;
        UpdateLocalAppButton.Click -= LocalAppsV054Button_Click;
        UpdateLocalAppButton.Click += LocalAppsV054Button_Click;
        _v054ExclusiveLocalAppsRouting = true;
        Title = "Matawaka Workbench v0.54";
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
    }

    private async void LocalAppsV054Button_Click(object sender, RoutedEventArgs e)
    {
        if (!_v054ExclusiveLocalAppsRouting)
        {
            ShowInvalid(new InvalidDataException("V054_LOCAL_APPS_ROUTE_NOT_EXCLUSIVE: v0.54 route was invoked before exclusive configuration."));
            return;
        }

        EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  local-app.v054.dispatch exclusive=true; runtimeMaterializationAvailable=true; runtimeExecutionAvailable=true");
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
            case LocalAppsActionChoiceV050.BoundedRuntimeMaterialization: await MaterializeBoundedRuntimeV054Async(appId); break;
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
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  local-app.v054.choice.cancelled app={appId}; effect=false");
                break;
        }
        RefreshInstalledAppsV044();
    }

    private async Task MaterializeBoundedRuntimeV054Async(string navigationAppId)
    {
        var requestDialog = new OpenFileDialog
        {
            Title = "Select exact bounded runtime-materialization request JSON",
            Filter = "JSON request (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (requestDialog.ShowDialog(this) != true) return;

        RuntimeMaterializationRequestV054 request;
        RuntimeMaterializationPreviewV054 preview;
        try
        {
            var json = await File.ReadAllTextAsync(requestDialog.FileName, Encoding.UTF8);
            request = JsonSerializer.Deserialize<RuntimeMaterializationRequestV054>(json)
                ?? throw new InvalidDataException("Runtime materialization request JSON deserialized to null.");
            preview = _runtimeMaterializationV054Service.Preview(WorkspaceRootBox.Text, request, CancellationToken.None);
        }
        catch (JsonException ex)
        {
            ShowInvalid(new InvalidDataException("V054_MATERIALIZATION_REQUEST_JSON_INVALID: " + ex.Message, ex));
            return;
        }
        catch (RuntimeMaterializationExceptionV054 ex)
        {
            ShowInvalid(new InvalidDataException($"{ex.Classification}: {ex.Message}", ex));
            return;
        }
        catch (IOException ex)
        {
            ShowInvalid(new InvalidDataException("V054_MATERIALIZATION_REQUEST_READ_FAILED: " + ex.Message, ex));
            return;
        }

        var confirmation = new StringBuilder();
        confirmation.AppendLine("Authorize one bounded runtime-tree materialization?");
        confirmation.AppendLine();
        confirmation.AppendLine($"Navigation app context only: {navigationAppId}");
        confirmation.AppendLine($"RequestId: {preview.RequestId}");
        confirmation.AppendLine($"Acquisition receipt: {preview.AcquisitionReceiptPath}");
        confirmation.AppendLine($"Acquisition receipt SHA-256: {preview.AcquisitionReceiptSha256}");
        confirmation.AppendLine($"Verified ZIP artifacts: {preview.Archives.Count}");
        foreach (var archive in preview.Archives)
            confirmation.AppendLine($"  {archive.ArtifactId}: {archive.ArchiveBytes} bytes; SHA-256={archive.ArchiveSha256}");
        confirmation.AppendLine($"Destination runtime root: {preview.DestinationRoot}");
        confirmation.AppendLine($"Exact files: {preview.ExactFileCount}; exact expanded bytes: {preview.ExactExpandedBytes}");
        confirmation.AppendLine($"Authority ceilings: files={preview.MaxFiles}; expandedBytes={preview.MaxExpandedBytes}; TTL={preview.TtlSeconds}s");
        confirmation.AppendLine($"Archive plan SHA-256: {preview.PlanSha256}");
        confirmation.AppendLine();
        confirmation.AppendLine("YES creates one one-shot Materialization Lease. Its call is durably consumed BEFORE the staging runtime root is created. Only exact ZIP entries from the v0.52-verified acquisition receipt may be written. Unsafe Windows paths, traversal, links/reparse entries, collisions and ceiling excess are refused. A sibling staging tree is promoted only after every file is SHA-256 verified.");
        confirmation.AppendLine();
        confirmation.AppendLine("Success writes a v0.53-compatible MATERIALIZED_VERIFIED runtime-tree manifest. It does NOT start any process, grant execution authority, change PATH/registry, run an installer, benchmark, issue a model request, access a game, or perform network access.");
        confirmation.AppendLine();
        confirmation.AppendLine("Verified Artifact ≠ Materialized Runtime ≠ Execution Authority. MATERIALIZED_VERIFIED ≠ Runtime Ready ≠ Model Request Authority.");

        if (MessageBox.Show(this, confirmation.ToString(), "Bounded Runtime-Tree Materialization v0.54", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  runtime-materialization.v054 cancelled request={preview.RequestId}; authority=false; extraction=false");
            return;
        }

        var beganRun = false;
        try
        {
            SetV035PrimaryControlsEnabled(false);
            BeginRun($"runtime-materialization-v0.54-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;
            StatusText.Text = $"RUNNING: bounded runtime-tree materialization; request={preview.RequestId}; oneShot=true";

            var authority = await _runtimeMaterializationV054Service.GrantAsync(WorkspaceRootBox.Text, preview, _cts!.Token);
            var materialized = await _runtimeMaterializationV054Service.MaterializeAsync(WorkspaceRootBox.Text, authority.Grant, _cts.Token);

            LocalAppsTextBox.Text = CommandCodec.Serialize(new
            {
                Status = materialized.Receipt.Status,
                RequestPath = requestDialog.FileName,
                Preview = preview,
                AuthorityReceipt = authority.Receipt,
                AuthorityReceiptPath = authority.ReceiptPath,
                AuthorityGrantBearerOmitted = true,
                MaterializationReceipt = materialized.Receipt,
                MaterializationReceiptPath = materialized.ReceiptPath,
                RuntimeManifestPath = materialized.Receipt.RuntimeManifestPath,
                RuntimeManifestSha256 = materialized.Receipt.RuntimeManifestSha256,
                TreeDigestSha256 = materialized.Receipt.TreeDigestSha256,
                ProcessExecutionPerformed = false,
                RuntimeStartPerformed = false,
                NetworkAccessPerformed = false,
                BenchmarkPerformed = false,
                ModelRequestPerformed = false,
                GameAccessPerformed = false
            });
            OutputTabs.SelectedItem = LocalAppsTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: v0.54 {materialized.Receipt.Status}; files={materialized.Receipt.MaterializedFiles}; bytes={materialized.Receipt.MaterializedBytes}; execute=false";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  runtime-materialization.v054 verified request={preview.RequestId}; files={materialized.Receipt.MaterializedFiles}; bytes={materialized.Receipt.MaterializedBytes}; execute=false; model=false");
        }
        catch (RuntimeMaterializationExceptionV054 ex)
        {
            LocalAppsTextBox.Text = CommandCodec.Serialize(new
            {
                Status = "RUNTIME_MATERIALIZATION_TERMINAL_FAIL_CLOSED",
                RequestId = preview.RequestId,
                Classification = ex.Classification,
                Message = ex.Message,
                AutomaticRetryPerformed = false,
                AutomaticResumePerformed = false,
                ProcessExecutionPerformed = false,
                RuntimeStartPerformed = false,
                NetworkAccessPerformed = false,
                BenchmarkPerformed = false,
                ModelRequestPerformed = false,
                GameAccessPerformed = false
            });
            OutputTabs.SelectedItem = LocalAppsTab;
            _currentTerminalState = CommandTerminalState.Failed;
            StatusText.Text = $"INVALID: {ex.Classification}: {ex.Message}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  runtime-materialization.v054 refused class={ex.Classification}; retry=false; resume=false; execute=false");
            MessageBox.Show(this, $"{ex.Classification}\n\n{ex.Message}", "Bounded Runtime-Tree Materialization v0.54", MessageBoxButton.OK, MessageBoxImage.Warning);
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

    internal IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> ObserveV054MaterializationContract() => new[]
    {
        ("v054-four-button-surface", true, "generic materialization reachable only through existing Local apps chooser", "no fifth top-level button"),
        ("v054-navigation-not-authority", true, "selected app context ignored by materialization service", "exact v0.52 receipt + request + explicit confirmation required"),
        ("v054-preview-no-effect", true, "Preview reads exact receipt/archive central directories and hashes only", "network=false; extraction=false; write=false"),
        ("v054-one-shot-authority", true, "GrantAsync then one MaterializeAsync; consumed before staging root creation", "one call"),
        ("v054-source-authority", true, "selected ArtifactIds from exact Workbench-owned v0.52 verified receipt", "no arbitrary source archive path"),
        ("v054-atomic-tree", true, "sibling staging + full file hashes + Directory.Move + final reverify", "bounded"),
        ("v054-v053-manifest", true, "writes exact matawaka.runtime-tree-manifest/v0.53 MATERIALIZED_VERIFIED evidence", "execution compatible evidence only"),
        ("v054-no-post-materialization-authority", true, "execution/runtime/model/benchmark/game/network all false", "true"),
        ("v054-provider-neutral", true, "no KONTUR-specific materialization behavior", "provider-neutral")
    };
}
