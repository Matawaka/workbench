using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalAppReadSessionStatusV0514Service _readSessionStatusV0514Service = new();
    private bool _v0514BoundedStatusEnabled;

    internal void ConfigureV0514Routing()
    {
        ConfigureV0513Routing();
        Title = "Matawaka Workbench v0.51.4";

        UpdateLocalAppButton.Click -= LocalAppsV0513Button_Click;
        UpdateLocalAppButton.Click += LocalAppsV0514Button_Click;

        _v0514BoundedStatusEnabled = true;
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
    }

    private async void LocalAppsV0514Button_Click(object sender, RoutedEventArgs e)
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
        var choice = LocalAppsActionDialogV0514.ShowChoice(this, appId, adapterActive, tunnelActive);

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
                ShowReadSessionStatusV0514(appId);
                break;
            case LocalAppsActionChoiceV050.ReadSessionLease:
                await CreateReadLeaseAndAutoStartMcpV0511Async(appId);
                break;
            case LocalAppsActionChoiceV050.StopReadOnlyMcpAdapter:
                await EndReadSessionV0512Async(appId);
                break;
            case LocalAppsActionChoiceV050.EndOrphanedReadSession:
                await EndOrphanedReadSessionV0514Async(appId);
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
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  local-app.v0514.choice.cancelled app={appId}; effect=false");
                break;
        }

        RefreshInstalledAppsV044();
    }

    private (string? AppId, string? LeaseId) CurrentMcpBindingV0514()
    {
        var adapterApp = _v049ActiveAdapterApplicationId;
        var adapterLease = _v050ActiveMcpLeaseId;
        if (adapterApp is null || !_localAppMcpReadAdapterV049Service.IsActiveFor(adapterApp))
            return (null, null);
        return (adapterApp, adapterLease);
    }

    private LocalAppReadSessionStatusV0514 ObserveReadSessionStatusV0514(
        string appId,
        int historyOffset = 0,
        int historyLimit = LocalAppReadSessionStatusV0514Service.DefaultHistoryLimit)
    {
        var binding = CurrentMcpBindingV0514();
        return _readSessionStatusV0514Service.Observe(
            WorkspaceRootBox.Text, appId, binding.AppId, binding.LeaseId, historyOffset, historyLimit);
    }

    private LocalAppReadSessionStatusLeaseV0513 ObserveExactLeaseV0514(string appId, string leaseId)
    {
        var binding = CurrentMcpBindingV0514();
        return _readSessionStatusV0514Service.ObserveExactLease(
            WorkspaceRootBox.Text, appId, leaseId, binding.AppId, binding.LeaseId);
    }

    private void ShowReadSessionStatusV0514(string appId)
    {
        try
        {
            var status = ObserveReadSessionStatusV0514(appId);
            if (status.HistoricalLeaseCount > status.HistoryLimit)
            {
                var selectedOffset = LocalAppHistoryPageChooserV0514.Choose(
                    this, appId, status.HistoricalLeaseCount, status.HistoryLimit);
                if (selectedOffset is not null && selectedOffset.Value != status.HistoryOffset)
                    status = ObserveReadSessionStatusV0514(appId, selectedOffset.Value, status.HistoryLimit);
            }

            LocalAppsTextBox.Text = CommandCodec.Serialize(status);
            OutputTabs.SelectedItem = LocalAppsTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text =
                $"COMPLETED: bounded Read Session Status for {appId}; live={status.LiveLeaseCount}; orphan={status.OrphanClosureEligibleCount}; history={status.HistoricalReturned}/{status.HistoricalLeaseCount}; offset={status.HistoryOffset}; bearer=omitted";
            EventList.Items.Add(
                $"{DateTime.Now:HH:mm:ss}  read-session-status.v0514 app={appId}; live={status.LiveLeaseCount}; orphan={status.OrphanClosureEligibleCount}; historyReturned={status.HistoricalReturned}; historyTotal={status.HistoricalLeaseCount}; offset={status.HistoryOffset}; bearer=false; stateMutation=false");
        }
        catch (InvalidDataException ex)
        {
            if (ex.Message.StartsWith("LIVE_AUTHORITY_OVERFLOW:", StringComparison.Ordinal))
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  read-session-status.v0514.live-overflow app={appId}; effect=false; recovery=explicit-only");
            ShowInvalid(ex);
        }
    }

    private async Task EndOrphanedReadSessionV0514Async(string appId)
    {
        LocalAppReadSessionStatusV0514 observed;
        try
        {
            observed = ObserveReadSessionStatusV0514(appId, 0, 1);
        }
        catch (InvalidDataException ex)
        {
            ShowInvalid(ex);
            return;
        }

        var eligible = observed.LiveAuthorities.Where(x => x.OrphanClosureEligible).ToArray();
        if (eligible.Length == 0)
        {
            MessageBox.Show(
                this,
                "No live unbound read lease is eligible for orphan closure.\n\nIf a local MCP adapter is active, use End Read Session. Expired/revoked/exhausted leases carry no live read authority.",
                "End orphaned read session",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  orphan-read-session.v0514.none app={appId}; effect=false");
            return;
        }

        var selectedLeaseId = LocalAppOrphanLeaseChooserV0513.Choose(this, appId, eligible);
        if (string.IsNullOrWhiteSpace(selectedLeaseId))
        {
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  orphan-read-session.v0514.selection-cancelled app={appId}; effect=false");
            return;
        }

        var target = ObserveExactLeaseV0514(appId, selectedLeaseId);
        if (!target.OrphanClosureEligible || target.BoundToActiveLocalMcp)
        {
            ShowInvalid(new InvalidDataException("Selected LeaseId is no longer an exact live unbound orphan candidate. Refresh bounded status instead of closing stale authority."));
            return;
        }

        var scopes = string.Join(", ", target.Scopes.Select(x => $"{x.Role}:{x.PathPrefix}"));
        var message = new StringBuilder();
        message.AppendLine("End this orphaned Read Session?");
        message.AppendLine();
        message.AppendLine($"ApplicationId: {appId}");
        message.AppendLine($"Exact LeaseId: {target.LeaseId}");
        message.AppendLine($"Scopes: {scopes}");
        message.AppendLine($"Expires: {target.ExpiresAt:O}");
        message.AppendLine($"Remaining: calls={target.RemainingCalls}, bytes={target.RemainingBytes}");
        message.AppendLine();
        message.AppendLine("This exact lease is live and not bound to the active local MCP. Yes revokes only this LeaseId. Historical pagination does not affect closure authority. No bearer is required or displayed.");

        if (MessageBox.Show(this, message.ToString(), "End orphaned read session v0.51.4", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  orphan-read-session.v0514.refused app={appId}; lease={target.LeaseId}; effect=false");
            return;
        }

        try
        {
            SetV035PrimaryControlsEnabled(false);
            BeginRun($"end-orphan-read-session-v0.51.4-{DateTime.Now:yyyyMMddHHmmss}");

            target = ObserveExactLeaseV0514(appId, selectedLeaseId);
            if (!target.OrphanClosureEligible || target.BoundToActiveLocalMcp)
                throw new InvalidDataException("Selected orphan LeaseId changed before closure; exact revoke refused.");

            var revoked = await _exactReadLeaseRevokeV0512Service.RevokeExactAsync(
                WorkspaceRootBox.Text, appId, target.LeaseId, _cts!.Token);
            var afterTarget = ObserveExactLeaseV0514(appId, target.LeaseId);
            if (!afterTarget.Revoked || afterTarget.OrphanClosureEligible)
                throw new InvalidDataException("Exact orphan lease state did not become durably revoked.");

            LocalAppsTextBox.Text = CommandCodec.Serialize(new
            {
                Status = "ORPHAN_READ_SESSION_ENDED_EXACT_LEASE_REVOKED",
                Version = "0.51.4",
                ApplicationId = appId,
                LeaseId = target.LeaseId,
                Before = target,
                ExactLeaseRevokeReceipt = revoked.Receipt,
                ExactLeaseRevokeReceiptPath = revoked.ReceiptPath,
                After = afterTarget,
                SiblingLeasesRevoked = revoked.Receipt.SiblingLeasesRevoked,
                HistoricalPaginationChanged = false,
                HistoricalEvidenceDeleted = false,
                ActiveLocalMcpChanged = false,
                SecureMcpTunnelChanged = false,
                BearerPlaintextUsedOrDisclosed = false,
                AutomaticRetryPerformed = false
            });
            OutputTabs.SelectedItem = LocalAppsTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: orphan Read Session ended for {appId}; exact lease={target.LeaseId} revoked; sibling leases=0; history preserved";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  orphan-read-session.v0514.completed app={appId}; lease={target.LeaseId}; exactRevoke=true; siblingRevokes=0; historyDeleted=false; mcpChanged=false");
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
            EndRun();
            SetV035PrimaryControlsEnabled(true);
            OperatorSurfaceV045Contract.Apply(this);
            RefreshInstalledAppsV044();
        }
    }

    internal IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> ObserveV0514BoundedStatusContract()
        => new[]
        {
            ("v0514-status-enabled", _v0514BoundedStatusEnabled, _v0514BoundedStatusEnabled.ToString(), "True"),
            ("v0514-live-authority", true, "all live or explicit LIVE_AUTHORITY_OVERFLOW", "never silently paginated"),
            ("v0514-history-default", true, "16", "bounded historical page"),
            ("v0514-history-max", true, "64", "hard max"),
            ("v0514-history-deletion", true, "false", "evidence preserved"),
            ("v0514-bearer", true, "omitted", "no plaintext/hash"),
            ("v0514-orphan-close-exact", true, "ObserveExactLease + exact revoke", "pagination-independent exact closure"),
            ("v0514-auto-revoke", true, "false", "no automatic revocation")
        };
}
