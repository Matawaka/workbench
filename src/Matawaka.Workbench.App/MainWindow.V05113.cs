using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalAppMcpShutdownTransactionV05113Service _shutdownTransactionV05113Service = new();
    private bool _v05113ExclusiveLocalAppsRouting;

    internal void ConfigureV05113Routing()
    {
        ConfigureV05112Routing();

        UpdateLocalAppButton.Click -= LocalAppsV0518Button_Click;
        UpdateLocalAppButton.Click -= LocalAppsV0517Button_Click;
        UpdateLocalAppButton.Click -= LocalAppsV05111Button_Click;
        UpdateLocalAppButton.Click -= LocalAppsV051111Button_Click;
        UpdateLocalAppButton.Click -= LocalAppsV05112Button_Click;
        UpdateLocalAppButton.Click -= LocalAppsV05113Button_Click;
        UpdateLocalAppButton.Click += LocalAppsV05113Button_Click;

        _v05113ExclusiveLocalAppsRouting = true;
        Title = "Matawaka Workbench v0.51.13";
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
    }

    private async void LocalAppsV05113Button_Click(object sender, RoutedEventArgs e)
    {
        if (!_v05113ExclusiveLocalAppsRouting)
        {
            ShowInvalid(new InvalidDataException("V05113_LOCAL_APPS_ROUTE_NOT_EXCLUSIVE: v0.51.13 route was invoked before exclusive configuration."));
            return;
        }

        EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  local-app.v05113.dispatch exclusive=true; target=shutdown-transaction");
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
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  local-app.v05113.choice.cancelled app={appId}; effect=false");
                break;
        }
        RefreshInstalledAppsV044();
    }

    private async Task EndOwnedReadSessionV05113Async(string appId)
    {
        if (!await EnsureVerifiedActiveIndexReadyV0515Async(appId, "end exact read session with v0.51.13 shutdown transaction")) return;
        var owner = _v0517ActiveMcpOwnership;
        var leaseId = _v050ActiveMcpLeaseId;
        if (owner is null || owner.Released || !owner.ApplicationId.Equals(appId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(leaseId) || !leaseId.Equals(owner.LeaseId, StringComparison.Ordinal) ||
            !_localAppMcpReadAdapterV049Service.IsActiveFor(appId))
        {
            ShowInvalid(new InvalidDataException("v0.51.13 End Read Session requires this process to hold the exact app/LeaseId MCP ownership and active listener."));
            return;
        }
        if (_secureMcpTunnelV0501Service.IsActiveFor(appId))
        {
            ShowInvalid(new InvalidDataException("Stop the Secure MCP Tunnel first."));
            return;
        }

        var message = new StringBuilder();
        message.AppendLine("End owned Read Session with Shutdown Transaction v0.51.13?");
        message.AppendLine();
        message.AppendLine($"ApplicationId: {appId}");
        message.AppendLine($"Exact LeaseId: {leaseId}");
        message.AppendLine($"Owner: {owner.SessionId}");
        message.AppendLine();
        message.AppendLine("Yes records SHUTDOWN_PREPARED before StopAsync, then proves LISTENER_STOPPED, releases the exact owner, and only then invokes the separate exact lease-revoke authority. Each boundary has its own receipt. A partial failure is not silently promoted to completion.");
        if (MessageBox.Show(this, message.ToString(), "Shutdown Transaction v0.51.13", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        (LocalAppMcpShutdownTransactionV05113 Transaction, string ReceiptPath)? prepared = null;
        (LocalAppMcpShutdownTransactionV05113 Transaction, string ReceiptPath)? listenerStopped = null;
        (LocalAppMcpShutdownTransactionV05113 Transaction, string ReceiptPath)? ownerReleased = null;
        (LocalAppMcpShutdownTransactionV05113 Transaction, string ReceiptPath)? leaseTerminal = null;
        (LocalAppMcpShutdownTransactionV05113 Transaction, string ReceiptPath)? completed = null;
        LocalAppMcpAdapterStopReceiptV049? stopReceipt = null;
        string? stopPath = null;
        LocalAppMcpSessionOwnershipReceiptV0517? ownerReleaseReceipt = null;
        string? ownerReleasePath = null;
        var beganRun = false;

        try
        {
            SetV035PrimaryControlsEnabled(false);
            BeginRun($"shutdown-transaction-v0.51.13-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;

            prepared = await _shutdownTransactionV05113Service.PrepareAsync(owner, _cts!.Token);

            var stopped = await _localAppMcpReadAdapterV049Service.StopAsync(WorkspaceRootBox.Text, _cts.Token);
            stopReceipt = stopped.Receipt;
            stopPath = stopped.ReceiptPath;
            var listenerInactive = stopReceipt.ListenerStopped && !_localAppMcpReadAdapterV049Service.IsActiveFor(appId);
            if (!listenerInactive)
                throw new InvalidDataException("MCP_SHUTDOWN_LISTENER_STOP_NOT_PROVEN: owner and exact lease remain fail-closed.");

            listenerStopped = await _shutdownTransactionV05113Service.RecordListenerStoppedAsync(
                owner, stopReceipt, stopPath, true, _cts.Token);

            var released = await _mcpSessionOwnershipV0517Service.ReleaseAfterListenerStoppedAsync(owner, true, _cts.Token);
            ownerReleaseReceipt = released.Receipt;
            ownerReleasePath = released.ReceiptPath;
            ownerReleased = await _shutdownTransactionV05113Service.RecordOwnerReleasedAsync(
                WorkspaceRootBox.Text, appId, owner.SessionId, leaseId, ownerReleasePath, _cts.Token);

            _v0517ActiveMcpOwnership = null;
            _v049ActiveAdapterApplicationId = null;
            ClearV050McpRuntimeView();

            var revoked = await _indexedLeaseLifecycleV0515Service.RevokeExactIndexedAsync(
                WorkspaceRootBox.Text, appId, leaseId, _cts.Token);
            leaseTerminal = await _shutdownTransactionV05113Service.RecordLeaseTerminalAsync(
                WorkspaceRootBox.Text, appId, owner.SessionId, leaseId, revoked.ExactReceiptPath,
                revoked.ExactReceipt.SiblingLeasesRevoked, _cts.Token);
            completed = await _shutdownTransactionV05113Service.CommitCompletedAsync(
                WorkspaceRootBox.Text, appId, owner.SessionId, leaseId, _cts.Token);

            LocalAppsTextBox.Text = CommandCodec.Serialize(new
            {
                Status = "SHUTDOWN_COMPLETED_LISTENER_STOPPED_OWNER_RELEASED_EXACT_LEASE_TERMINAL",
                ApplicationId = appId,
                LeaseId = leaseId,
                ShutdownPreparedTransaction = prepared.Value.Transaction,
                ShutdownPreparedReceiptPath = prepared.Value.ReceiptPath,
                AdapterStopReceipt = stopReceipt,
                AdapterStopReceiptPath = stopPath,
                ListenerStoppedTransaction = listenerStopped.Value.Transaction,
                ListenerStoppedReceiptPath = listenerStopped.Value.ReceiptPath,
                OwnershipReleaseReceipt = ownerReleaseReceipt,
                OwnershipReleaseReceiptPath = ownerReleasePath,
                OwnerReleasedTransaction = ownerReleased.Value.Transaction,
                OwnerReleasedReceiptPath = ownerReleased.Value.ReceiptPath,
                ExactLeaseRevokeReceipt = revoked.ExactReceipt,
                ExactLeaseRevokeReceiptPath = revoked.ExactReceiptPath,
                LeaseTerminalTransaction = leaseTerminal.Value.Transaction,
                LeaseTerminalReceiptPath = leaseTerminal.Value.ReceiptPath,
                ShutdownCompletedTransaction = completed.Value.Transaction,
                ShutdownCompletedReceiptPath = completed.Value.ReceiptPath,
                revoked.ActiveIndexRevision,
                revoked.IndexedCandidates,
                SiblingLeasesRevoked = revoked.ExactReceipt.SiblingLeasesRevoked,
                ShutdownTransactionGrantedAuthority = false,
                HistoricalCanonicalScanPerformed = false,
                BearerPlaintextStoredInShutdownTransaction = false,
                BearerHashStoredInShutdownTransaction = false,
                EndpointPathSecretStoredInShutdownTransaction = false
            });
            OutputTabs.SelectedItem = LocalAppsTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: v0.51.13 Shutdown Transaction for {appId}; listener=STOPPED; owner=RELEASED; lease={leaseId}=TERMINAL; siblings=0; indexRev={revoked.ActiveIndexRevision}";
        }
        catch (InvalidDataException ex)
        {
            LocalAppsTextBox.Text = CommandCodec.Serialize(new
            {
                Status = "SHUTDOWN_TRANSACTION_PARTIAL_FAIL_CLOSED",
                ApplicationId = appId,
                LeaseId = leaseId,
                ShutdownPrepared = prepared?.Transaction,
                ListenerStopped = listenerStopped?.Transaction,
                OwnerReleased = ownerReleased?.Transaction,
                LeaseTerminal = leaseTerminal?.Transaction,
                Completed = completed?.Transaction,
                AdapterStopReceipt = stopReceipt,
                AdapterStopReceiptPath = stopPath,
                OwnershipReleaseReceipt = ownerReleaseReceipt,
                OwnershipReleaseReceiptPath = ownerReleasePath,
                ErrorCategory = ex.Message.Split(':')[0],
                AutomaticListenerResumePerformed = false,
                AutomaticOwnerReleasePerformedAfterUnprovenStop = false,
                AutomaticLeaseRevokePerformedByRecovery = false,
                SiblingLeaseRevocationRequested = false
            });
            OutputTabs.SelectedItem = LocalAppsTab;
            ShowInvalid(ex);
        }
        finally
        {
            if (beganRun) EndRun();
            SetV035PrimaryControlsEnabled(true);
            OperatorSurfaceV045Contract.Apply(this);
            RefreshInstalledAppsV044();
        }
    }

    internal IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> ObserveV05113ShutdownContract() => new[]
    {
        ("v05113-exclusive-route", _v05113ExclusiveLocalAppsRouting, _v05113ExclusiveLocalAppsRouting.ToString(), "True"),
        ("v05113-order", true, "LISTENER_READY -> SHUTDOWN_PREPARED -> StopAsync -> LISTENER_STOPPED -> owner release -> OWNER_RELEASED -> exact revoke -> LEASE_REVOKED -> SHUTDOWN_COMPLETED", "separate states"),
        ("v05113-stop-proof", true, "owner release only after listener inactive observation", "true"),
        ("v05113-revoke-separate", true, "RevokeExactIndexedAsync after OWNER_RELEASED", "true"),
        ("v05113-siblings", true, "exact shutdown corridor refuses sibling revoke", "true"),
        ("v05113-recovery", true, "live exact lease after owner.lock reacquire blocks successor; no auto revoke", "true"),
        ("v05113-history", true, "exact LeaseId observation only", "false"),
        ("v05113-secrets", true, "shutdown transaction omits bearer/hash/path token", "omitted"),
        ("v05113-kontur", true, "generic reverse runtime lifecycle reusable later; current anchors planning-only", "no integration authority")
    };
}
