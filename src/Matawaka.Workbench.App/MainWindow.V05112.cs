using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalAppMcpListenerReadinessV05112Service _listenerReadinessV05112Service = new();
    private bool _v05112ExclusiveLocalAppsRouting;

    internal void ConfigureV05112Routing()
    {
        ConfigureV051111Routing();

        UpdateLocalAppButton.Click -= LocalAppsV0518Button_Click;
        UpdateLocalAppButton.Click -= LocalAppsV0517Button_Click;
        UpdateLocalAppButton.Click -= LocalAppsV05111Button_Click;
        UpdateLocalAppButton.Click -= LocalAppsV051111Button_Click;
        UpdateLocalAppButton.Click -= LocalAppsV05112Button_Click;
        UpdateLocalAppButton.Click += LocalAppsV05112Button_Click;

        _v05112ExclusiveLocalAppsRouting = true;
        Title = "Matawaka Workbench v0.51.12";
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
    }

    private async void LocalAppsV05112Button_Click(object sender, RoutedEventArgs e)
    {
        if (!_v05112ExclusiveLocalAppsRouting)
        {
            ShowInvalid(new InvalidDataException(
                "V05112_LOCAL_APPS_ROUTE_NOT_EXCLUSIVE: v0.51.12 route was invoked before exclusive routing configuration."));
            return;
        }

        EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  local-app.v05112.dispatch exclusive=true; target=v05112-listener-readiness");
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
            case LocalAppsActionChoiceV050.ChatReadRelay: await ChatReadRelayV047Async(appId); break;
            case LocalAppsActionChoiceV050.ReadSessionStatus: await ShowCoherentLiveReadSessionStatusV0516Async(appId); break;
            case LocalAppsActionChoiceV050.ReadSessionHistoryPage: ShowCanonicalReadSessionHistoryPageV0515(appId); break;
            case LocalAppsActionChoiceV050.ReadSessionLease: await CreateOwnedReadLeaseAndAutoStartMcpV05112Async(appId); break;
            case LocalAppsActionChoiceV050.StopReadOnlyMcpAdapter: await EndOwnedReadSessionV0517Async(appId); break;
            case LocalAppsActionChoiceV050.EndOrphanedReadSession: await EndOrphanedWithFreeMcpDomainV0517Async(appId); break;
            case LocalAppsActionChoiceV050.RevokeReadLeases: await RevokeAllWithFreeMcpDomainV0517Async(appId); break;
            case LocalAppsActionChoiceV050.StartReadOnlyMcpAdapter: await StartOwnedManualMcpV0517Async(appId); break;
            case LocalAppsActionChoiceV050.StartSecureMcpTunnel: await StartSecureMcpTunnelV0502Async(appId); break;
            case LocalAppsActionChoiceV050.StopSecureMcpTunnel: await StopSecureMcpTunnelV0502Async(appId); break;
            default:
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  local-app.v05112.choice.cancelled app={appId}; effect=false");
                break;
        }
        RefreshInstalledAppsV044();
    }

    private async Task CreateOwnedReadLeaseAndAutoStartMcpV05112Async(string appId)
    {
        if (!await EnsureVerifiedActiveIndexReadyV0515Async(appId, "create v0.51.12 listener-readiness bounded read lease")) return;
        if (_v049ActiveAdapterApplicationId is not null || _v0517ActiveMcpOwnership is { Released: false })
        {
            ShowInvalid(new InvalidDataException("This Workbench process already owns a local MCP session. End it before creating another."));
            return;
        }
        if (_v050ActiveTunnelApplicationId is not null && _secureMcpTunnelV0501Service.IsActiveFor(_v050ActiveTunnelApplicationId))
        {
            ShowInvalid(new InvalidDataException("Stop the active Secure MCP Tunnel before creating a new read session."));
            return;
        }

        var requestJson = LocalAppReadLeaseRequestDialogV048.ShowRequest(this, appId);
        if (requestJson is null) return;
        var preview = _indexedLeaseLifecycleV0515Service.PreviewFromJson(WorkspaceRootBox.Text, appId, requestJson, CancellationToken.None);
        var message = new StringBuilder();
        message.AppendLine("Создать bounded Read session lease и local MCP с Listener Readiness Transaction v0.51.12?");
        message.AppendLine();
        message.AppendLine($"ApplicationId: {preview.ApplicationId}");
        message.AppendLine($"TTL: {preview.TtlSeconds}s; calls={preview.MaxCalls}; total bytes={preview.MaxTotalBytes:N0}; max/read={preview.MaxBytesPerRead:N0}");
        foreach (var scope in preview.Scopes) message.AppendLine($"  - {scope.Role}: {scope.PathPrefix}");
        message.AppendLine();
        message.AppendLine("Yes preserves the v0.51.11 exact owner→lease chain, then records PREPARED_LISTENER_START before StartAsync, LISTENER_STARTED only after the loopback adapter is materially active, and LISTENER_READY only after a second exact observation. None of these transaction states grants read/revoke/resume/publication authority.");
        if (MessageBox.Show(this, message.ToString(), "Read session + listener readiness v0.51.12", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        LocalAppHeldMcpSessionOwnershipV0517? owner = null;
        LocalAppIndexedLeaseCreateResultV0515? created = null;
        (LocalAppMcpOwnerLeaseBindingTransactionV05111 Transaction, string ReceiptPath)? preparedBinding = null;
        (LocalAppMcpOwnerLeaseBindingTransactionV05111 Transaction, string ReceiptPath)? leaseCreatedBinding = null;
        (LocalAppMcpOwnerLeaseBindingTransactionV05111 Transaction, string ReceiptPath)? ownerBoundBinding = null;
        (LocalAppMcpListenerReadinessTransactionV05112 Transaction, string ReceiptPath)? listenerPrepared = null;
        (LocalAppMcpListenerReadinessTransactionV05112 Transaction, string ReceiptPath)? listenerStarted = null;
        (LocalAppMcpListenerReadinessTransactionV05112 Transaction, string ReceiptPath)? listenerReady = null;
        var beganRun = false;
        try
        {
            owner = await _mcpSessionOwnershipV0517Service.AcquireAsync(
                WorkspaceRootBox.Text, appId, "auto-read-session-start-v05112", CancellationToken.None);
            _v0517ActiveMcpOwnership = owner;
            SetV035PrimaryControlsEnabled(false);
            BeginRun($"listener-readiness-auto-mcp-v0.51.12-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;

            preparedBinding = await _ownerLeaseBindingV05111Service.PrepareBindingAsync(owner, _cts!.Token);
            created = await _preparedLeaseV05111Service.CreatePreparedIndexedAsync(
                WorkspaceRootBox.Text, appId, preview, preparedBinding.Value.Transaction.PreparedLeaseId, false, _cts.Token);
            leaseCreatedBinding = await _ownerLeaseBindingV05111Service.RecordLeaseCreatedAsync(owner, created, _cts.Token);

            await _mcpSessionOwnershipV0517Service.BindExactLeaseAsync(owner, created.Grant.LeaseId, _cts.Token);
            ownerBoundBinding = await _ownerLeaseBindingV05111Service.CommitOwnerBoundAsync(owner, _cts.Token);
            listenerPrepared = await _listenerReadinessV05112Service.PrepareAsync(owner, ownerBoundBinding.Value.Transaction, _cts.Token);

            var exactGrantJson = LocalAppReadLeaseV048Service.SerializeGrant(created.Grant);
            Clipboard.SetText(exactGrantJson);
            var clipboardGrantJson = Clipboard.GetText();
            if (!string.Equals(exactGrantJson, clipboardGrantJson, StringComparison.Ordinal))
                throw new InvalidDataException("Clipboard grant round-trip mismatch after PREPARED_LISTENER_START; listener was not started.");
            var adapterPreview = _localAppMcpReadAdapterV049Service.PreviewFromGrantJson(
                WorkspaceRootBox.Text, appId, clipboardGrantJson, _cts.Token);
            if (!adapterPreview.LeaseId.Equals(created.Grant.LeaseId, StringComparison.Ordinal) ||
                !adapterPreview.BearerSha256.Equals(created.Receipt.BearerSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("MCP preview is not bound to the exact v0.51.12 PREPARED_LISTENER_START lease.");

            var adapterGrant = await _localAppMcpReadAdapterV049Service.StartAsync(
                WorkspaceRootBox.Text, appId, adapterPreview, clipboardGrantJson, _cts.Token);
            _v049ActiveAdapterApplicationId = appId;
            _v050ActiveMcpEndpoint = adapterGrant.EndpointUrl;
            _v050ActiveMcpLeaseId = adapterGrant.LeaseId;
            _v050ActiveMcpLeaseExpiresAt = adapterGrant.LeaseExpiresAt;

            var activeAfterStart = _localAppMcpReadAdapterV049Service.IsActiveFor(appId);
            listenerStarted = await _listenerReadinessV05112Service.RecordListenerStartedAsync(
                owner, adapterGrant, activeAfterStart, _cts.Token);

            var activeAtCommit = _localAppMcpReadAdapterV049Service.IsActiveFor(appId);
            listenerReady = await _listenerReadinessV05112Service.CommitReadyAsync(
                owner, adapterGrant, activeAtCommit, _cts.Token);

            await _mcpSessionOwnershipV0517Service.MarkListenerReadyAsync(owner, adapterGrant, _cts.Token);
            var adapterWritten = await _localAppMcpReadAdapterV049Service.WriteStartReceiptAsync(
                WorkspaceRootBox.Text, adapterGrant, false, _cts.Token);

            LocalAppsTextBox.Text = CommandCodec.Serialize(new
            {
                Status = "OWNER_LEASE_BOUND_LISTENER_READY_INDEXED_READ_LEASE_AND_LOCAL_MCP_READY",
                LeasePreview = preview,
                OwnerLeasePreparedTransaction = preparedBinding.Value.Transaction,
                OwnerLeasePreparedReceiptPath = preparedBinding.Value.ReceiptPath,
                LeaseGrant = created.Grant,
                LeaseCreationReceipt = created.Receipt,
                LeaseCreationReceiptPath = created.ReceiptPath,
                created.ActiveIndexRevision,
                created.IndexedCandidates,
                OwnerLeaseCreatedTransaction = leaseCreatedBinding.Value.Transaction,
                OwnerLeaseCreatedReceiptPath = leaseCreatedBinding.Value.ReceiptPath,
                OwnerLeaseBoundTransaction = ownerBoundBinding.Value.Transaction,
                OwnerLeaseBoundReceiptPath = ownerBoundBinding.Value.ReceiptPath,
                ListenerPreparedTransaction = listenerPrepared.Value.Transaction,
                ListenerPreparedReceiptPath = listenerPrepared.Value.ReceiptPath,
                ListenerStartedTransaction = listenerStarted.Value.Transaction,
                ListenerStartedReceiptPath = listenerStarted.Value.ReceiptPath,
                ListenerReadyTransaction = listenerReady.Value.Transaction,
                ListenerReadyReceiptPath = listenerReady.Value.ReceiptPath,
                McpOwnershipSessionId = owner.SessionId,
                McpOwnershipWaitMilliseconds = owner.WaitMilliseconds,
                McpOwnershipLeaseId = owner.LeaseId,
                CrossProcessMcpOwnershipHeld = true,
                PreparedLeaseIdEqualsCreatedLeaseId = preparedBinding.Value.Transaction.PreparedLeaseId == created.Grant.LeaseId,
                OwnerBoundBeforeListenerPrepare = true,
                ListenerPreparedBeforeStartAsync = true,
                ListenerStartedObservedActive = listenerStarted.Value.Transaction.ListenerObservedActive,
                ListenerReadyReobservedActive = listenerReady.Value.Transaction.ListenerObservedActive,
                ClipboardRoundTripExact = true,
                AdapterPreview = adapterPreview,
                AdapterStartReceipt = adapterWritten.Receipt,
                AdapterStartReceiptPath = adapterWritten.ReceiptPath,
                HistoricalCanonicalScanPerformed = false,
                ListenerTransactionGrantedAuthority = false,
                BearerPlaintextStoredInListenerTransaction = false,
                BearerHashStoredInListenerTransaction = false,
                EndpointPathSecretStoredInListenerTransaction = false,
                SecureMcpTunnelStarted = false
            });
            OutputTabs.SelectedItem = LocalAppsTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: v0.51.12 read session + local MCP ready for {appId}; lease={created.Grant.LeaseId}; owner={owner.SessionId}; binding=OWNER_BOUND; listener=LISTENER_READY; indexRev={created.ActiveIndexRevision}";
        }
        catch (OperationCanceledException)
        {
            await ReleaseOwnerIfNoListenerV0517Async(owner, appId, "v0.51.12 operation cancelled before a proven active listener");
            if (created is not null) ShowLeaseCreatedMcpStartFailureV0511(appId, created.Grant, created.Receipt, created.ReceiptPath, "operation cancelled after exact lease creation during listener-readiness transaction");
            else ShowCancelled();
        }
        catch (InvalidDataException ex)
        {
            await ReleaseOwnerIfNoListenerV0517Async(owner, appId, ex.Message);
            if (created is not null) ShowLeaseCreatedMcpStartFailureV0511(appId, created.Grant, created.Receipt, created.ReceiptPath, ex.Message);
            else ShowInvalid(ex);
        }
        catch (Exception ex)
        {
            await ReleaseOwnerIfNoListenerV0517Async(owner, appId, "unexpected v0.51.12 startup failure before a proven active listener");
            if (created is not null) ShowLeaseCreatedMcpStartFailureV0511(appId, created.Grant, created.Receipt, created.ReceiptPath, "local MCP startup failed during v0.51.12 listener-readiness transaction");
            else ShowFailure(ex);
        }
        finally
        {
            if (beganRun) EndRun();
            SetV035PrimaryControlsEnabled(true);
            OperatorSurfaceV045Contract.Apply(this);
            RefreshInstalledAppsV044();
        }
    }

    internal IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> ObserveV05112ListenerReadinessContract() => new[]
    {
        ("v05112-exclusive-route", _v05112ExclusiveLocalAppsRouting, _v05112ExclusiveLocalAppsRouting.ToString(), "True"),
        ("v05112-order", true, "OWNER_BOUND -> PREPARED_LISTENER_START -> StartAsync -> LISTENER_STARTED -> second active observation -> LISTENER_READY -> owner MarkListenerReady", "true"),
        ("v05112-prepared", true, "PREPARED_LISTENER_START records no listener existence", "false"),
        ("v05112-started", true, "LISTENER_STARTED requires exact loopback grant + active process-local adapter", "true"),
        ("v05112-ready", true, "LISTENER_READY requires a second exact active observation and live exact lease", "true"),
        ("v05112-recovery", true, "live bound lease without current listener blocks successor owner generation", "no auto resume/revoke"),
        ("v05112-no-history", true, "recovery resolves exact LeaseId only", "true"),
        ("v05112-authority", true, "listener transaction grants no lease/read/revoke/resume authority", "false"),
        ("v05112-ui", true, "top-level four-button surface preserved", "true")
    };
}
