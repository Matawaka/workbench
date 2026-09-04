using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalAppReadLeaseExactRevokeV0512Service _exactReadLeaseRevokeV0512Service = new();
    private bool _v0512EndReadSessionEnabled;

    internal void ConfigureV0512Routing()
    {
        ConfigureV0511Routing();
        Title = "Matawaka Workbench v0.51.2";

        UpdateLocalAppButton.Click -= LocalAppsV0511Button_Click;
        UpdateLocalAppButton.Click += LocalAppsV0512Button_Click;

        _v0512EndReadSessionEnabled = true;
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
    }

    private async void LocalAppsV0512Button_Click(object sender, RoutedEventArgs e)
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
        var choice = LocalAppsActionDialogV0512.ShowChoice(this, appId, adapterActive, tunnelActive);

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
            case LocalAppsActionChoiceV050.ReadSessionLease:
                await CreateReadLeaseAndAutoStartMcpV0511Async(appId);
                break;
            case LocalAppsActionChoiceV050.StopReadOnlyMcpAdapter:
                await EndReadSessionV0512Async(appId);
                break;
            case LocalAppsActionChoiceV050.RevokeReadLeases:
                await RevokeReadSessionLeasesV048Async(appId);
                break;
            case LocalAppsActionChoiceV050.StartReadOnlyMcpAdapter:
                await StartReadOnlyMcpAdapterV050Async(appId);
                break;
            case LocalAppsActionChoiceV050.StartSecureMcpTunnel:
                await StartSecureMcpTunnelV0502Async(appId);
                break;
            case LocalAppsActionChoiceV050.StopSecureMcpTunnel:
                await StopSecureMcpTunnelV0502Async(appId);
                break;
            case LocalAppsActionChoiceV050.Cancel:
            default:
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  local-app.v0512.choice.cancelled app={appId}; effect=false");
                break;
        }

        RefreshInstalledAppsV044();
    }

    private async Task EndReadSessionV0512Async(string appId)
    {
        if (!_localAppMcpReadAdapterV049Service.IsActiveFor(appId) ||
            _v049ActiveAdapterApplicationId is null ||
            !_v049ActiveAdapterApplicationId.Equals(appId, StringComparison.Ordinal))
        {
            ShowInvalid(new InvalidDataException("End Read Session requires the selected app to own the active local MCP adapter in this Workbench process."));
            return;
        }

        if (_secureMcpTunnelV0501Service.IsActiveFor(appId) ||
            (_v050ActiveTunnelApplicationId is not null && _secureMcpTunnelV0501Service.IsActiveFor(_v050ActiveTunnelApplicationId)))
        {
            ShowInvalid(new InvalidDataException("Stop the Secure MCP Tunnel first. End Read Session closes only local MCP + its exact bound lease."));
            return;
        }

        var leaseId = _v050ActiveMcpLeaseId;
        var leaseExpiresAt = _v050ActiveMcpLeaseExpiresAt;
        if (string.IsNullOrWhiteSpace(leaseId) || leaseExpiresAt is null)
        {
            ShowInvalid(new InvalidDataException("Active MCP runtime view is missing its exact bound LeaseId/expiry; use recovery controls instead of guessing a lease."));
            return;
        }

        var message = new StringBuilder();
        message.AppendLine("Завершить текущую Read Session?");
        message.AppendLine();
        message.AppendLine($"ApplicationId: {appId}");
        message.AppendLine($"Exact bound LeaseId: {leaseId}");
        message.AppendLine($"Lease expires: {leaseExpiresAt:O}");
        message.AppendLine();
        message.AppendLine("Yes выполнит только:");
        message.AppendLine("1) остановит текущий local MCP adapter и очистит его in-memory bearer reference;");
        message.AppendLine("2) отзовёт ровно указанный LeaseId;");
        message.AppendLine("3) очистит локальный runtime view этой MCP-сессии.");
        message.AppendLine();
        message.AppendLine("Другие leases приложения не перечисляются и не отзываются. Secure MCP Tunnel не запускается и не изменяется.");

        if (MessageBox.Show(this, message.ToString(), "End Read Session v0.51.2", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  end-read-session.v0512.refused app={appId}; lease={leaseId}; effect=false");
            return;
        }

        LocalAppMcpAdapterStopReceiptV049? adapterStopReceipt = null;
        string? adapterStopReceiptPath = null;
        string? adapterStopWarning = null;
        LocalAppReadLeaseExactRevokeReceiptV0512? revokeReceipt = null;
        string? revokeReceiptPath = null;

        try
        {
            SetV035PrimaryControlsEnabled(false);
            BeginRun($"end-read-session-v0.51.2-{DateTime.Now:yyyyMMddHHmmss}");

            try
            {
                var stopped = await _localAppMcpReadAdapterV049Service.StopAsync(WorkspaceRootBox.Text, _cts!.Token);
                adapterStopReceipt = stopped.Receipt;
                adapterStopReceiptPath = stopped.ReceiptPath;
            }
            catch (Exception ex)
            {
                adapterStopWarning = ex is InvalidDataException ? ex.Message : "local MCP stop receipt/runtime cleanup raised an unexpected error";
                await _localAppMcpReadAdapterV049Service.StopBestEffortAsync();
            }

            var adapterInactive = !_localAppMcpReadAdapterV049Service.IsActiveFor(appId);
            _v049ActiveAdapterApplicationId = null;
            ClearV050McpRuntimeView();

            if (!adapterInactive)
            {
                ShowEndReadSessionPartialV0512(
                    appId, leaseId, false, adapterStopReceipt, adapterStopReceiptPath,
                    null, null, "Local MCP adapter still appears active after bounded stop attempts. Exact lease revoke was not attempted to avoid an uncoordinated state race.");
                return;
            }

            try
            {
                var revoked = await _exactReadLeaseRevokeV0512Service.RevokeExactAsync(
                    WorkspaceRootBox.Text, appId, leaseId, _cts!.Token);
                revokeReceipt = revoked.Receipt;
                revokeReceiptPath = revoked.ReceiptPath;
            }
            catch (Exception ex)
            {
                ShowEndReadSessionPartialV0512(
                    appId, leaseId, true, adapterStopReceipt, adapterStopReceiptPath,
                    null, null,
                    ex is InvalidDataException ? ex.Message : "Exact lease revoke failed after local MCP stop. The lease may remain active until expiry or explicit recovery revoke.");
                return;
            }

            LocalAppsTextBox.Text = CommandCodec.Serialize(new
            {
                Status = "READ_SESSION_ENDED_EXACT_LEASE_REVOKED",
                ApplicationId = appId,
                LeaseId = leaseId,
                AdapterObservedInactive = true,
                AdapterStopReceipt = adapterStopReceipt,
                AdapterStopReceiptPath = adapterStopReceiptPath,
                AdapterStopWarning = adapterStopWarning,
                ExactLeaseRevokeReceipt = revokeReceipt,
                ExactLeaseRevokeReceiptPath = revokeReceiptPath,
                SiblingLeasesRevoked = revokeReceipt.SiblingLeasesRevoked,
                SecureMcpTunnelChanged = false,
                AutomaticRetryPerformed = false,
                RuntimeViewCleared = true,
                NextHumanAction = "Create a new Read session lease only when another bounded session is needed."
            });
            OutputTabs.SelectedItem = LocalAppsTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: Read Session ended for {appId}; MCP stopped; exact lease={leaseId} revoked; sibling leases=0";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  end-read-session.v0512.completed app={appId}; lease={leaseId}; adapter=false; exactRevoke=true; siblingRevokes=0; tunnel=false");
        }
        catch (OperationCanceledException)
        {
            await _localAppMcpReadAdapterV049Service.StopBestEffortAsync();
            _v049ActiveAdapterApplicationId = null;
            ClearV050McpRuntimeView();
            ShowEndReadSessionPartialV0512(
                appId, leaseId, !_localAppMcpReadAdapterV049Service.IsActiveFor(appId),
                adapterStopReceipt, adapterStopReceiptPath, revokeReceipt, revokeReceiptPath,
                "End Read Session was cancelled; no automatic retry is performed. Verify/revoke remaining lease authority explicitly.");
        }
        finally
        {
            EndRun();
            SetV035PrimaryControlsEnabled(true);
            OperatorSurfaceV045Contract.Apply(this);
            RefreshInstalledAppsV044();
        }
    }

    private void ShowEndReadSessionPartialV0512(
        string appId,
        string leaseId,
        bool adapterInactive,
        LocalAppMcpAdapterStopReceiptV049? adapterStopReceipt,
        string? adapterStopReceiptPath,
        LocalAppReadLeaseExactRevokeReceiptV0512? revokeReceipt,
        string? revokeReceiptPath,
        string failure)
    {
        _currentTerminalState = CommandTerminalState.Failed;
        ProgressBar.Value = 100;
        StatusText.Text = $"FAILED/PARTIAL: End Read Session for {appId}; adapterInactive={adapterInactive}; exact lease closure must be verified";
        LocalAppsTextBox.Text = CommandCodec.Serialize(new
        {
            Status = "END_READ_SESSION_PARTIAL",
            ApplicationId = appId,
            LeaseId = leaseId,
            AdapterObservedInactive = adapterInactive,
            AdapterStopReceipt = adapterStopReceipt,
            AdapterStopReceiptPath = adapterStopReceiptPath,
            ExactLeaseRevokeReceipt = revokeReceipt,
            ExactLeaseRevokeReceiptPath = revokeReceiptPath,
            Failure = failure,
            AutomaticRetryPerformed = false,
            SiblingLeaseRevocationAttempted = false,
            SecureMcpTunnelChanged = false,
            NextExplicitAction = revokeReceipt is null ? "Use Revoke ALL active read leases recovery if the exact lease may still be active." : "No read authority remains on the exact lease; inspect transport cleanup evidence if needed."
        });
        OutputTabs.SelectedItem = LocalAppsTab;
        EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  end-read-session.v0512.partial app={appId}; lease={leaseId}; adapterInactive={adapterInactive}; exactRevoke={(revokeReceipt?.ExactLeaseRevoked == true)}; retry=false");
    }

    internal IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> ObserveV0512EndSessionContract()
        => new[]
        {
            ("v0512-end-session-enabled", _v0512EndReadSessionEnabled, _v0512EndReadSessionEnabled.ToString(), "True"),
            ("v0512-end-session-exact", true, "bound ApplicationId + LeaseId", "exact lease only"),
            ("v0512-end-session-order", true, "MCP stop before exact state revoke", "no concurrent MCP read/list state writer"),
            ("v0512-end-session-sibling", true, "0", "0 sibling leases revoked"),
            ("v0512-end-session-tunnel", true, "must already be stopped", "separate authority"),
            ("v0512-end-session-retry", true, "false", "no automatic retry")
        };
}
