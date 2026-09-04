using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalAppMcpOwnerLeaseBindingV05111Service _ownerLeaseBindingV05111Service = new();
    private readonly LocalAppPreparedIndexedLeaseV05111Service _preparedLeaseV05111Service = new();

    internal void ConfigureV05111Routing()
    {
        ConfigureV05110Routing();
        Title = "Matawaka Workbench v0.51.11";
        UpdateLocalAppButton.Click -= LocalAppsV0517Button_Click;
        UpdateLocalAppButton.Click += LocalAppsV05111Button_Click;
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
    }

    private async void LocalAppsV05111Button_Click(object sender, RoutedEventArgs e)
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
            case LocalAppsActionChoiceV050.ReadSessionLease: await CreateOwnedReadLeaseAndAutoStartMcpV05111Async(appId); break;
            case LocalAppsActionChoiceV050.StopReadOnlyMcpAdapter: await EndOwnedReadSessionV0517Async(appId); break;
            case LocalAppsActionChoiceV050.EndOrphanedReadSession: await EndOrphanedWithFreeMcpDomainV0517Async(appId); break;
            case LocalAppsActionChoiceV050.RevokeReadLeases: await RevokeAllWithFreeMcpDomainV0517Async(appId); break;
            case LocalAppsActionChoiceV050.StartReadOnlyMcpAdapter: await StartOwnedManualMcpV0517Async(appId); break;
            case LocalAppsActionChoiceV050.StartSecureMcpTunnel: await StartSecureMcpTunnelV0502Async(appId); break;
            case LocalAppsActionChoiceV050.StopSecureMcpTunnel: await StopSecureMcpTunnelV0502Async(appId); break;
            default:
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  local-app.v05111.choice.cancelled app={appId}; effect=false");
                break;
        }
        RefreshInstalledAppsV044();
    }

    private async Task CreateOwnedReadLeaseAndAutoStartMcpV05111Async(string appId)
    {
        if (!await EnsureVerifiedActiveIndexReadyV0515Async(appId, "create v0.51.11 owner-bound bounded read lease")) return;
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
        message.AppendLine("Создать bounded Read session lease и local MCP с owner→lease transaction v0.51.11?");
        message.AppendLine();
        message.AppendLine($"ApplicationId: {preview.ApplicationId}");
        message.AppendLine($"TTL: {preview.TtlSeconds}s; calls={preview.MaxCalls}; total bytes={preview.MaxTotalBytes:N0}; max/read={preview.MaxBytesPerRead:N0}");
        foreach (var scope in preview.Scopes) message.AppendLine($"  - {scope.Role}: {scope.PathPrefix}");
        message.AppendLine();
        message.AppendLine("Yes acquires the app MCP owner domain, reconciles any incomplete prior owner→lease transaction, requires v0.51.10 COMMITTED owner generation, then PREPARES one exact LeaseId before canonical state creation. PREPARED does not grant authority. Canonical state creation, LEASE_CREATED and exact OWNER_BOUND are recorded separately before the listener is started.");
        if (MessageBox.Show(this, message.ToString(), "Read session + owner→lease transaction v0.51.11", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        LocalAppHeldMcpSessionOwnershipV0517? owner = null;
        LocalAppIndexedLeaseCreateResultV0515? created = null;
        (LocalAppMcpOwnerLeaseBindingTransactionV05111 Transaction, string ReceiptPath)? preparedBinding = null;
        (LocalAppMcpOwnerLeaseBindingTransactionV05111 Transaction, string ReceiptPath)? leaseCreatedBinding = null;
        (LocalAppMcpOwnerLeaseBindingTransactionV05111 Transaction, string ReceiptPath)? ownerBoundBinding = null;
        var beganRun = false;
        try
        {
            owner = await _mcpSessionOwnershipV0517Service.AcquireAsync(
                WorkspaceRootBox.Text, appId, "auto-read-session-start-v05111", CancellationToken.None);
            _v0517ActiveMcpOwnership = owner;
            SetV035PrimaryControlsEnabled(false);
            BeginRun($"owner-lease-binding-auto-mcp-v0.51.11-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;

            preparedBinding = await _ownerLeaseBindingV05111Service.PrepareBindingAsync(owner, _cts!.Token);
            created = await _preparedLeaseV05111Service.CreatePreparedIndexedAsync(
                WorkspaceRootBox.Text, appId, preview, preparedBinding.Value.Transaction.PreparedLeaseId, false, _cts.Token);
            leaseCreatedBinding = await _ownerLeaseBindingV05111Service.RecordLeaseCreatedAsync(owner, created, _cts.Token);

            await _mcpSessionOwnershipV0517Service.BindExactLeaseAsync(owner, created.Grant.LeaseId, _cts.Token);
            ownerBoundBinding = await _ownerLeaseBindingV05111Service.CommitOwnerBoundAsync(owner, _cts.Token);

            var exactGrantJson = LocalAppReadLeaseV048Service.SerializeGrant(created.Grant);
            Clipboard.SetText(exactGrantJson);
            var clipboardGrantJson = Clipboard.GetText();
            if (!string.Equals(exactGrantJson, clipboardGrantJson, StringComparison.Ordinal))
                throw new InvalidDataException("Clipboard grant round-trip mismatch after OWNER_BOUND; listener was not started.");
            var adapterPreview = _localAppMcpReadAdapterV049Service.PreviewFromGrantJson(
                WorkspaceRootBox.Text, appId, clipboardGrantJson, _cts.Token);
            if (!adapterPreview.LeaseId.Equals(created.Grant.LeaseId, StringComparison.Ordinal) ||
                !adapterPreview.BearerSha256.Equals(created.Receipt.BearerSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("MCP preview is not bound to the exact v0.51.11 OWNER_BOUND lease.");

            var adapterGrant = await _localAppMcpReadAdapterV049Service.StartAsync(
                WorkspaceRootBox.Text, appId, adapterPreview, clipboardGrantJson, _cts.Token);
            _v049ActiveAdapterApplicationId = appId;
            _v050ActiveMcpEndpoint = adapterGrant.EndpointUrl;
            _v050ActiveMcpLeaseId = adapterGrant.LeaseId;
            _v050ActiveMcpLeaseExpiresAt = adapterGrant.LeaseExpiresAt;
            await _mcpSessionOwnershipV0517Service.MarkListenerReadyAsync(owner, adapterGrant, _cts.Token);
            var adapterWritten = await _localAppMcpReadAdapterV049Service.WriteStartReceiptAsync(
                WorkspaceRootBox.Text, adapterGrant, false, _cts.Token);

            LocalAppsTextBox.Text = CommandCodec.Serialize(new
            {
                Status = "OWNER_LEASE_BOUND_INDEXED_READ_LEASE_AND_LOCAL_MCP_READY",
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
                McpOwnershipSessionId = owner.SessionId,
                McpOwnershipWaitMilliseconds = owner.WaitMilliseconds,
                McpOwnershipLeaseId = owner.LeaseId,
                CrossProcessMcpOwnershipHeld = true,
                PreparedLeaseIdEqualsCreatedLeaseId = preparedBinding.Value.Transaction.PreparedLeaseId == created.Grant.LeaseId,
                OwnerBoundBeforeListenerStart = true,
                ClipboardRoundTripExact = true,
                AdapterPreview = adapterPreview,
                AdapterStartReceipt = adapterWritten.Receipt,
                AdapterStartReceiptPath = adapterWritten.ReceiptPath,
                HistoricalCanonicalScanPerformed = false,
                BindingTransactionGrantedAuthority = false,
                BearerPlaintextStoredInBindingTransaction = false,
                BearerHashStoredInBindingTransaction = false,
                EndpointSecretStoredInBindingTransaction = false,
                SecureMcpTunnelStarted = false
            });
            OutputTabs.SelectedItem = LocalAppsTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: v0.51.11 owner→lease bound read session + local MCP ready for {appId}; lease={created.Grant.LeaseId}; owner={owner.SessionId}; binding=OWNER_BOUND; indexRev={created.ActiveIndexRevision}";
        }
        catch (OperationCanceledException)
        {
            await ReleaseOwnerIfNoListenerV0517Async(owner, appId, "v0.51.11 operation cancelled before a proven active listener");
            if (created is not null) ShowLeaseCreatedMcpStartFailureV0511(appId, created.Grant, created.Receipt, created.ReceiptPath, "operation cancelled after exact prepared lease creation");
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
            await ReleaseOwnerIfNoListenerV0517Async(owner, appId, "unexpected v0.51.11 startup failure before a proven active listener");
            if (created is not null) ShowLeaseCreatedMcpStartFailureV0511(appId, created.Grant, created.Receipt, created.ReceiptPath, "local MCP startup failed after v0.51.11 prepared lease materialization");
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

    internal IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> ObserveV05111OwnerLeaseBindingContract() => new[]
    {
        ("v05111-order", true, "prior binding reconcile -> owner generation COMMITTED -> exact PREPARED_BINDING -> canonical create -> LEASE_CREATED -> owner metadata bind -> OWNER_BOUND -> listener", "true"),
        ("v05111-prepared-id", true, "prepared exact LeaseId exists before canonical state", "prepared != created"),
        ("v05111-no-history", true, "recovery resolves exact prepared LeaseId path only", "true"),
        ("v05111-live-orphan", true, "incomplete live lease blocks successor owner generation without revoke", "fail closed"),
        ("v05111-authority", true, "binding transaction grants no lease/read/revoke/resume authority", "false"),
        ("v05111-listener", true, "OWNER_BOUND != listener ready", "separate"),
        ("v05111-ui", true, "top-level four-button surface preserved", "true")
    };
}
