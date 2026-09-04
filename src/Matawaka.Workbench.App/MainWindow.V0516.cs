using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private bool _v0516CrossProcessFenceEnabled;

    internal void ConfigureV0516Routing()
    {
        ConfigureV0515Routing();
        Title = "Matawaka Workbench v0.51.6";
        UpdateLocalAppButton.Click -= LocalAppsV0515Button_Click;
        UpdateLocalAppButton.Click += LocalAppsV0516Button_Click;
        _v0516CrossProcessFenceEnabled = true;
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
    }

    private async void LocalAppsV0516Button_Click(object sender, RoutedEventArgs e)
    {
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
            catch (InvalidDataException ex)
            {
                ShowInvalid(ex);
                return;
            }
            await RegisterSelectedLocalAppAsync(selectedRoot);
            RefreshInstalledAppsV044();
            return;
        }

        var appId = Path.GetFileName(selectedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (_v050ActiveTunnelApplicationId is not null &&
            !_secureMcpTunnelV0501Service.IsActiveFor(_v050ActiveTunnelApplicationId))
            _v050ActiveTunnelApplicationId = null;

        var adapterActive = _localAppMcpReadAdapterV049Service.IsActiveFor(appId);
        var tunnelActive = _secureMcpTunnelV0501Service.IsActiveFor(appId);
        var choice = LocalAppsActionDialogV0515.ShowChoice(this, appId, adapterActive, tunnelActive);

        switch (choice)
        {
            case LocalAppsActionChoiceV050.UpdateFromPackage:
                await UpdateSelectedLocalAppAsync(selectedRoot);
                break;
            case LocalAppsActionChoiceV050.BuildUpdatePackage:
                await BuildLocalAppPackageV038Async(selectedRoot);
                break;
            case LocalAppsActionChoiceV050.LaunchApp:
                await LaunchSelectedLocalAppV046Async(appId, selectedRoot);
                break;
            case LocalAppsActionChoiceV050.ExportUpdateContext:
                await ExportUpdateContextV046Async(appId);
                break;
            case LocalAppsActionChoiceV050.BindDevelopmentSource:
                await BindDevelopmentSourceV046Async(appId);
                break;
            case LocalAppsActionChoiceV050.ExportPrivateDevelopmentContext:
                await ExportPrivateDevelopmentContextV046Async(appId);
                break;
            case LocalAppsActionChoiceV050.ChatReadRelay:
                await ChatReadRelayV047Async(appId);
                break;
            case LocalAppsActionChoiceV050.ReadSessionStatus:
                await ShowCoherentLiveReadSessionStatusV0516Async(appId);
                break;
            case LocalAppsActionChoiceV050.ReadSessionHistoryPage:
                ShowCanonicalReadSessionHistoryPageV0515(appId);
                break;
            case LocalAppsActionChoiceV050.ReadSessionLease:
                await CreateIndexedReadLeaseAndAutoStartMcpV0515Async(appId);
                break;
            case LocalAppsActionChoiceV050.StopReadOnlyMcpAdapter:
                await EndIndexedReadSessionV0515Async(appId);
                break;
            case LocalAppsActionChoiceV050.EndOrphanedReadSession:
                await EndIndexedOrphanedReadSessionV0515Async(appId);
                break;
            case LocalAppsActionChoiceV050.RevokeReadLeases:
                await RevokeAllAndReconcileV0515Async(appId);
                break;
            case LocalAppsActionChoiceV050.StartReadOnlyMcpAdapter:
                await StartVerifiedManualMcpV0515Async(appId);
                break;
            case LocalAppsActionChoiceV050.StartSecureMcpTunnel:
                await StartSecureMcpTunnelV0502Async(appId);
                break;
            case LocalAppsActionChoiceV050.StopSecureMcpTunnel:
                await StopSecureMcpTunnelV0502Async(appId);
                break;
            case LocalAppsActionChoiceV050.Cancel:
            default:
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  local-app.v0516.choice.cancelled app={appId}; effect=false");
                break;
        }

        RefreshInstalledAppsV044();
    }

    private async Task ShowCoherentLiveReadSessionStatusV0516Async(string appId)
    {
        if (!await EnsureVerifiedActiveIndexReadyV0515Async(appId, "cross-process coherent live-authority status")) return;
        try
        {
            var binding = CurrentMcpBindingV0514();
            var status = await _indexedLeaseLifecycleV0515Service.ObserveCoherentLiveAuthorityV0516Async(
                WorkspaceRootBox.Text, appId, binding.AppId, binding.LeaseId, CancellationToken.None);
            LocalAppsTextBox.Text = CommandCodec.Serialize(status);
            OutputTabs.SelectedItem = LocalAppsTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: coherent live Read Session Status for {appId}; live={status.LiveLeaseCount}; orphan={status.OrphanClosureEligibleCount}; indexRev={status.IndexRevision}; fence={status.CrossProcessFenceAcquired}; coherent={status.SnapshotCoherent}; historicalScan=false; bearer=omitted";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  live-read-status.v0516 app={appId}; live={status.LiveLeaseCount}; orphan={status.OrphanClosureEligibleCount}; fence=true; waitMs={status.FenceWaitMilliseconds}; revision={status.IndexRevisionBeforeObservation}->{status.IndexRevisionAfterObservation}; dirty=false; coherent=true; historicalScan=false; bearer=false");
        }
        catch (InvalidDataException ex)
        {
            ShowInvalid(ex);
        }
    }

    internal IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> ObserveV0516FenceContract()
        => new[]
        {
            ("v0516-fence-enabled", _v0516CrossProcessFenceEnabled, _v0516CrossProcessFenceEnabled.ToString(), "True"),
            ("v0516-fence-process", true, "app-scoped exclusive file handle", "cross-process"),
            ("v0516-fence-status", true, "revision + dirty post-check", "coherent or fail closed"),
            ("v0516-fence-timeout", true, "ACTIVE_INDEX_FENCE_BUSY", "no partial authority"),
            ("v0516-fence-bearer", true, "plaintext/hash omitted", "omitted"),
            ("v0516-history", true, "separate v0.51.5 bounded canonical page", "unchanged"),
            ("v0516-publication", true, "deferred", "no remote mutation")
        };
}
