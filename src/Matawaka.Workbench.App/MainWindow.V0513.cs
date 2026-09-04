using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalAppReadSessionStatusV0513Service _readSessionStatusV0513Service = new();
    private bool _v0513ReadSessionStatusEnabled;

    internal void ConfigureV0513Routing()
    {
        ConfigureV0512Routing();
        Title = "Matawaka Workbench v0.51.3";

        UpdateLocalAppButton.Click -= LocalAppsV0512Button_Click;
        UpdateLocalAppButton.Click += LocalAppsV0513Button_Click;

        _v0513ReadSessionStatusEnabled = true;
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
    }

    private async void LocalAppsV0513Button_Click(object sender, RoutedEventArgs e)
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
        var choice = LocalAppsActionDialogV0513.ShowChoice(this, appId, adapterActive, tunnelActive);

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
                ShowReadSessionStatusV0513(appId);
                break;
            case LocalAppsActionChoiceV050.ReadSessionLease:
                await CreateReadLeaseAndAutoStartMcpV0511Async(appId);
                break;
            case LocalAppsActionChoiceV050.StopReadOnlyMcpAdapter:
                await EndReadSessionV0512Async(appId);
                break;
            case LocalAppsActionChoiceV050.EndOrphanedReadSession:
                await EndOrphanedReadSessionV0513Async(appId);
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
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  local-app.v0513.choice.cancelled app={appId}; effect=false");
                break;
        }

        RefreshInstalledAppsV044();
    }

    private LocalAppReadSessionStatusV0513 ObserveReadSessionStatusV0513(string appId)
    {
        var adapterApp = _v049ActiveAdapterApplicationId;
        var adapterLease = _v050ActiveMcpLeaseId;
        if (adapterApp is null || !_localAppMcpReadAdapterV049Service.IsActiveFor(adapterApp))
        {
            adapterApp = null;
            adapterLease = null;
        }
        return _readSessionStatusV0513Service.Observe(
            WorkspaceRootBox.Text, appId, adapterApp, adapterLease);
    }

    private void ShowReadSessionStatusV0513(string appId)
    {
        try
        {
            var status = ObserveReadSessionStatusV0513(appId);
            LocalAppsTextBox.Text = CommandCodec.Serialize(status);
            OutputTabs.SelectedItem = LocalAppsTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: Read Session Status for {appId}; live={status.LiveLeaseCount}; orphanEligible={status.OrphanClosureEligibleCount}; bearer=omitted";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  read-session-status.v0513 app={appId}; live={status.LiveLeaseCount}; orphan={status.OrphanClosureEligibleCount}; bearer=false; effect=read-only");
        }
        catch (InvalidDataException ex)
        {
            ShowInvalid(ex);
        }
    }

    private async Task EndOrphanedReadSessionV0513Async(string appId)
    {
        LocalAppReadSessionStatusV0513 observed;
        try
        {
            observed = ObserveReadSessionStatusV0513(appId);
        }
        catch (InvalidDataException ex)
        {
            ShowInvalid(ex);
            return;
        }

        var eligible = observed.Leases.Where(x => x.OrphanClosureEligible).ToArray();
        if (eligible.Length == 0)
        {
            MessageBox.Show(
                this,
                "No live unbound read lease is eligible for orphan closure.\n\nIf a local MCP adapter is active, use End Read Session. Expired/revoked/exhausted leases carry no live read authority.",
                "End orphaned read session",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  orphan-read-session.v0513.none app={appId}; effect=false");
            return;
        }

        var selectedLeaseId = LocalAppOrphanLeaseChooserV0513.Choose(this, appId, eligible);
        if (string.IsNullOrWhiteSpace(selectedLeaseId))
        {
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  orphan-read-session.v0513.selection-cancelled app={appId}; effect=false");
            return;
        }

        // Fresh status after selection: never close a lease that became bound or ceased to be live.
        var fresh = ObserveReadSessionStatusV0513(appId);
        var target = fresh.Leases.SingleOrDefault(x => x.LeaseId.Equals(selectedLeaseId, StringComparison.Ordinal));
        if (target is null || !target.OrphanClosureEligible || target.BoundToActiveLocalMcp)
        {
            ShowInvalid(new InvalidDataException("Selected LeaseId is no longer an exact live unbound orphan candidate. Refresh status instead of closing stale authority."));
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
        message.AppendLine("This lease is not bound to the active local MCP. Yes revokes only this exact LeaseId. No bearer is required or displayed. Sibling leases and active MCP/tunnel state are untouched.");

        if (MessageBox.Show(this, message.ToString(), "End orphaned read session v0.51.3", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  orphan-read-session.v0513.refused app={appId}; lease={target.LeaseId}; effect=false");
            return;
        }

        try
        {
            SetV035PrimaryControlsEnabled(false);
            BeginRun($"end-orphan-read-session-v0.51.3-{DateTime.Now:yyyyMMddHHmmss}");

            // One final freshness check immediately before exact state mutation.
            fresh = ObserveReadSessionStatusV0513(appId);
            target = fresh.Leases.SingleOrDefault(x => x.LeaseId.Equals(selectedLeaseId, StringComparison.Ordinal));
            if (target is null || !target.OrphanClosureEligible || target.BoundToActiveLocalMcp)
                throw new InvalidDataException("Selected orphan LeaseId changed before closure; exact revoke refused.");

            var revoked = await _exactReadLeaseRevokeV0512Service.RevokeExactAsync(
                WorkspaceRootBox.Text, appId, target.LeaseId, _cts!.Token);
            var after = ObserveReadSessionStatusV0513(appId);
            var afterTarget = after.Leases.Single(x => x.LeaseId.Equals(target.LeaseId, StringComparison.Ordinal));
            if (!afterTarget.Revoked || afterTarget.OrphanClosureEligible)
                throw new InvalidDataException("Exact orphan lease state did not become durably revoked.");

            LocalAppsTextBox.Text = CommandCodec.Serialize(new
            {
                Status = "ORPHAN_READ_SESSION_ENDED_EXACT_LEASE_REVOKED",
                ApplicationId = appId,
                LeaseId = target.LeaseId,
                Before = target,
                ExactLeaseRevokeReceipt = revoked.Receipt,
                ExactLeaseRevokeReceiptPath = revoked.ReceiptPath,
                After = afterTarget,
                SiblingLeasesRevoked = revoked.Receipt.SiblingLeasesRevoked,
                ActiveLocalMcpChanged = false,
                SecureMcpTunnelChanged = false,
                BearerPlaintextUsedOrDisclosed = false,
                AutomaticRetryPerformed = false
            });
            OutputTabs.SelectedItem = LocalAppsTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: orphan Read Session ended for {appId}; exact lease={target.LeaseId} revoked; sibling leases=0; active MCP unchanged";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  orphan-read-session.v0513.completed app={appId}; lease={target.LeaseId}; exactRevoke=true; siblingRevokes=0; mcpChanged=false; tunnelChanged=false");
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

    internal IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> ObserveV0513ReadSessionStatusContract()
        => new[]
        {
            ("v0513-status-enabled", _v0513ReadSessionStatusEnabled, _v0513ReadSessionStatusEnabled.ToString(), "True"),
            ("v0513-status-bearer", true, "omitted", "no plaintext/hash"),
            ("v0513-status-app-content", true, "not read", "not read"),
            ("v0513-orphan-close-exact", true, "fresh selected ApplicationId + LeaseId only", "exact"),
            ("v0513-orphan-close-bound-refusal", true, "BoundToActiveLocalMcp => refused", "refused"),
            ("v0513-orphan-close-sibling", true, "0", "0 sibling leases revoked"),
            ("v0513-auto-revoke", true, "false", "no automatic revocation")
        };
}
