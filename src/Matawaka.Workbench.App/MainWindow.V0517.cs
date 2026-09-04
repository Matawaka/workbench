using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalAppMcpSessionOwnershipV0517Service _mcpSessionOwnershipV0517Service = new();
    private LocalAppHeldMcpSessionOwnershipV0517? _v0517ActiveMcpOwnership;
    private bool _v0517CrossProcessMcpOwnershipEnabled;

    internal void ConfigureV0517Routing()
    {
        ConfigureV0516Routing();
        Title = "Matawaka Workbench v0.51.7";
        UpdateLocalAppButton.Click -= LocalAppsV0516Button_Click;
        UpdateLocalAppButton.Click += LocalAppsV0517Button_Click;
        _v0517CrossProcessMcpOwnershipEnabled = true;
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
    }

    private async void LocalAppsV0517Button_Click(object sender, RoutedEventArgs e)
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
            case LocalAppsActionChoiceV050.ReadSessionLease: await CreateOwnedReadLeaseAndAutoStartMcpV0517Async(appId); break;
            case LocalAppsActionChoiceV050.StopReadOnlyMcpAdapter: await EndOwnedReadSessionV0517Async(appId); break;
            case LocalAppsActionChoiceV050.EndOrphanedReadSession: await EndOrphanedWithFreeMcpDomainV0517Async(appId); break;
            case LocalAppsActionChoiceV050.RevokeReadLeases: await RevokeAllWithFreeMcpDomainV0517Async(appId); break;
            case LocalAppsActionChoiceV050.StartReadOnlyMcpAdapter: await StartOwnedManualMcpV0517Async(appId); break;
            case LocalAppsActionChoiceV050.StartSecureMcpTunnel: await StartSecureMcpTunnelV0502Async(appId); break;
            case LocalAppsActionChoiceV050.StopSecureMcpTunnel: await StopSecureMcpTunnelV0502Async(appId); break;
            default:
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  local-app.v0517.choice.cancelled app={appId}; effect=false");
                break;
        }
        RefreshInstalledAppsV044();
    }

    private async Task CreateOwnedReadLeaseAndAutoStartMcpV0517Async(string appId)
    {
        if (!await EnsureVerifiedActiveIndexReadyV0515Async(appId, "create owned bounded read lease")) return;
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
        message.AppendLine("Создать bounded Read session lease и local MCP под cross-process ownership v0.51.7?");
        message.AppendLine();
        message.AppendLine($"ApplicationId: {preview.ApplicationId}");
        message.AppendLine($"TTL: {preview.TtlSeconds}s; calls={preview.MaxCalls}; total bytes={preview.MaxTotalBytes:N0}; max/read={preview.MaxBytesPerRead:N0}");
        foreach (var scope in preview.Scopes) message.AppendLine($"  - {scope.Role}: {scope.PathPrefix}");
        message.AppendLine();
        message.AppendLine("Yes first acquires app-scoped MCP runtime ownership. If another Workbench owns it, no lease is created. Only after ownership succeeds is the exact indexed lease created and the existing loopback adapter started. No bearer/hash or endpoint secret is written to owner metadata.");
        if (MessageBox.Show(this, message.ToString(), "Read session + MCP ownership v0.51.7", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        LocalAppHeldMcpSessionOwnershipV0517? owner = null;
        LocalAppIndexedLeaseCreateResultV0515? created = null;
        var beganRun = false;
        try
        {
            owner = await _mcpSessionOwnershipV0517Service.AcquireAsync(
                WorkspaceRootBox.Text, appId, "auto-read-session-start", CancellationToken.None);
            _v0517ActiveMcpOwnership = owner;
            SetV035PrimaryControlsEnabled(false);
            BeginRun($"owned-read-lease-auto-mcp-v0.51.7-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;

            created = await _indexedLeaseLifecycleV0515Service.CreateIndexedAsync(
                WorkspaceRootBox.Text, appId, preview, false, _cts!.Token);
            await _mcpSessionOwnershipV0517Service.BindExactLeaseAsync(owner, created.Grant.LeaseId, _cts.Token);

            var exactGrantJson = LocalAppReadLeaseV048Service.SerializeGrant(created.Grant);
            Clipboard.SetText(exactGrantJson);
            var clipboardGrantJson = Clipboard.GetText();
            if (!string.Equals(exactGrantJson, clipboardGrantJson, StringComparison.Ordinal))
                throw new InvalidDataException("Clipboard grant round-trip mismatch after lease creation; listener was not started.");
            var adapterPreview = _localAppMcpReadAdapterV049Service.PreviewFromGrantJson(
                WorkspaceRootBox.Text, appId, clipboardGrantJson, _cts.Token);
            if (!adapterPreview.LeaseId.Equals(created.Grant.LeaseId, StringComparison.Ordinal) ||
                !adapterPreview.BearerSha256.Equals(created.Receipt.BearerSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("MCP preview is not bound to the exact owner-bound just-created lease.");

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
                Status = "OWNED_INDEXED_READ_LEASE_AND_LOCAL_MCP_READY",
                LeasePreview = preview,
                LeaseGrant = created.Grant,
                LeaseCreationReceipt = created.Receipt,
                LeaseCreationReceiptPath = created.ReceiptPath,
                created.ActiveIndexRevision,
                created.IndexedCandidates,
                McpOwnershipSessionId = owner.SessionId,
                McpOwnershipWaitMilliseconds = owner.WaitMilliseconds,
                McpOwnershipLeaseId = owner.LeaseId,
                CrossProcessMcpOwnershipHeld = true,
                ClipboardRoundTripExact = true,
                AdapterPreview = adapterPreview,
                AdapterStartReceipt = adapterWritten.Receipt,
                AdapterStartReceiptPath = adapterWritten.ReceiptPath,
                HistoricalCanonicalScanPerformed = false,
                BearerPlaintextStoredInOwnerMetadata = false,
                BearerHashStoredInOwnerMetadata = false,
                EndpointSecretStoredInOwnerMetadata = false,
                SecureMcpTunnelStarted = false
            });
            OutputTabs.SelectedItem = LocalAppsTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: owned read lease + local MCP ready for {appId}; lease={created.Grant.LeaseId}; owner={owner.SessionId}; indexRev={created.ActiveIndexRevision}; tunnel=false";
        }
        catch (OperationCanceledException)
        {
            await ReleaseOwnerIfNoListenerV0517Async(owner, appId, "operation cancelled before a proven active listener");
            if (created is not null) ShowLeaseCreatedMcpStartFailureV0511(appId, created.Grant, created.Receipt, created.ReceiptPath, "operation cancelled after lease creation");
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
            await ReleaseOwnerIfNoListenerV0517Async(owner, appId, "unexpected startup failure before a proven active listener");
            if (created is not null) ShowLeaseCreatedMcpStartFailureV0511(appId, created.Grant, created.Receipt, created.ReceiptPath, "local MCP startup failed after owner-bound lease creation");
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

    private async Task ReleaseOwnerIfNoListenerV0517Async(LocalAppHeldMcpSessionOwnershipV0517? owner, string appId, string reason)
    {
        if (owner is null || owner.Released) return;
        if (_localAppMcpReadAdapterV049Service.IsActiveFor(appId)) return;
        try
        {
            _ = await _mcpSessionOwnershipV0517Service.ReleaseUnstartedAsync(owner, true, reason, CancellationToken.None);
            if (ReferenceEquals(_v0517ActiveMcpOwnership, owner)) _v0517ActiveMcpOwnership = null;
        }
        catch { }
    }

    private async Task EndOwnedReadSessionV0517Async(string appId)
    {
        if (!await EnsureVerifiedActiveIndexReadyV0515Async(appId, "end owned exact read session")) return;
        var owner = _v0517ActiveMcpOwnership;
        var leaseId = _v050ActiveMcpLeaseId;
        if (owner is null || owner.Released || !owner.ApplicationId.Equals(appId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(leaseId) || !leaseId.Equals(owner.LeaseId, StringComparison.Ordinal) ||
            !_localAppMcpReadAdapterV049Service.IsActiveFor(appId))
        {
            ShowInvalid(new InvalidDataException("End Read Session requires this process to hold the exact app/LeaseId MCP ownership and active listener."));
            return;
        }
        if (_secureMcpTunnelV0501Service.IsActiveFor(appId))
        {
            ShowInvalid(new InvalidDataException("Stop the Secure MCP Tunnel first."));
            return;
        }
        if (MessageBox.Show(this, $"End owned Read Session?\n\nApplicationId: {appId}\nExact LeaseId: {leaseId}\nOwner: {owner.SessionId}\n\nYes requires a proven listener stop, then releases MCP ownership, then exact-revokes this LeaseId. If listener stop is not proven, ownership and canonical authority are left fail-closed.", "End owned Read Session v0.51.7", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        LocalAppMcpAdapterStopReceiptV049? stopReceipt = null;
        string? stopPath = null;
        try
        {
            SetV035PrimaryControlsEnabled(false);
            BeginRun($"end-owned-read-session-v0.51.7-{DateTime.Now:yyyyMMddHHmmss}");
            var stopped = await _localAppMcpReadAdapterV049Service.StopAsync(WorkspaceRootBox.Text, _cts!.Token);
            stopReceipt = stopped.Receipt;
            stopPath = stopped.ReceiptPath;
            if (!stopReceipt.ListenerStopped || _localAppMcpReadAdapterV049Service.IsActiveFor(appId))
                throw new InvalidDataException("MCP_SESSION_RELEASE_REFUSED_LISTENER_STILL_ACTIVE: exact listener stop was not proven.");

            var ownerRelease = await _mcpSessionOwnershipV0517Service.ReleaseAfterListenerStoppedAsync(owner, true, _cts.Token);
            _v0517ActiveMcpOwnership = null;
            _v049ActiveAdapterApplicationId = null;
            ClearV050McpRuntimeView();

            var revoked = await _indexedLeaseLifecycleV0515Service.RevokeExactIndexedAsync(
                WorkspaceRootBox.Text, appId, leaseId, _cts.Token);
            LocalAppsTextBox.Text = CommandCodec.Serialize(new
            {
                Status = "OWNED_READ_SESSION_ENDED_LISTENER_STOPPED_OWNER_RELEASED_EXACT_LEASE_REVOKED",
                ApplicationId = appId,
                LeaseId = leaseId,
                AdapterStopReceipt = stopReceipt,
                AdapterStopReceiptPath = stopPath,
                OwnershipReleaseReceipt = ownerRelease.Receipt,
                OwnershipReleaseReceiptPath = ownerRelease.ReceiptPath,
                ExactLeaseRevokeReceipt = revoked.ExactReceipt,
                ExactLeaseRevokeReceiptPath = revoked.ExactReceiptPath,
                revoked.ActiveIndexRevision,
                revoked.IndexedCandidates,
                SiblingLeasesRevoked = revoked.ExactReceipt.SiblingLeasesRevoked,
                BearerPlaintextUsedOrDisclosed = false,
                EndpointSecretUsedOrDisclosed = false
            });
            OutputTabs.SelectedItem = LocalAppsTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: owned Read Session ended for {appId}; listener stopped; owner released; exact lease={leaseId} revoked; siblings=0";
        }
        catch (InvalidDataException ex)
        {
            ShowEndReadSessionPartialV0512(appId, leaseId, true, stopReceipt, stopPath, null, null,
                ex.Message + " MCP ownership is retained unless a proven release receipt exists; process exit releases only runtime ownership, not lease authority.");
        }
        finally
        {
            EndRun();
            SetV035PrimaryControlsEnabled(true);
            OperatorSurfaceV045Contract.Apply(this);
            RefreshInstalledAppsV044();
        }
    }

    private async Task EndOrphanedWithFreeMcpDomainV0517Async(string appId)
    {
        LocalAppHeldMcpSessionOwnershipV0517? guard = null;
        try
        {
            guard = await _mcpSessionOwnershipV0517Service.AcquireAsync(
                WorkspaceRootBox.Text, appId, "guard-exact-orphan-closure", CancellationToken.None, 500);
            await EndIndexedOrphanedReadSessionV0515Async(appId);
        }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        finally
        {
            if (guard is { Released: false })
                try { _ = await _mcpSessionOwnershipV0517Service.ReleaseUnstartedAsync(guard, true, "orphan-closure guard complete", CancellationToken.None); } catch { }
        }
    }

    private async Task RevokeAllWithFreeMcpDomainV0517Async(string appId)
    {
        LocalAppHeldMcpSessionOwnershipV0517? guard = null;
        try
        {
            guard = await _mcpSessionOwnershipV0517Service.AcquireAsync(
                WorkspaceRootBox.Text, appId, "guard-revoke-all-recovery", CancellationToken.None, 500);
            await RevokeAllAndReconcileV0515Async(appId);
        }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        finally
        {
            if (guard is { Released: false })
                try { _ = await _mcpSessionOwnershipV0517Service.ReleaseUnstartedAsync(guard, true, "revoke-all guard complete", CancellationToken.None); } catch { }
        }
    }

    private async Task StartOwnedManualMcpV0517Async(string appId)
    {
        if (!await EnsureVerifiedActiveIndexReadyV0515Async(appId, "manual owned MCP startup")) return;
        if (_v0517ActiveMcpOwnership is { Released: false })
        {
            ShowInvalid(new InvalidDataException("This Workbench process already holds MCP ownership."));
            return;
        }
        LocalAppHeldMcpSessionOwnershipV0517? owner = null;
        try
        {
            owner = await _mcpSessionOwnershipV0517Service.AcquireAsync(
                WorkspaceRootBox.Text, appId, "manual-mcp-start", CancellationToken.None);
            _v0517ActiveMcpOwnership = owner;
            await StartReadOnlyMcpAdapterV050Async(appId);
            var leaseId = _v050ActiveMcpLeaseId;
            if (!_localAppMcpReadAdapterV049Service.IsActiveFor(appId) || string.IsNullOrWhiteSpace(leaseId))
            {
                await ReleaseOwnerIfNoListenerV0517Async(owner, appId, "manual MCP start cancelled or produced no active listener");
                return;
            }
            _ = await _indexedLeaseLifecycleV0515Service.ObserveIndexedExactLiveLeaseAsync(
                WorkspaceRootBox.Text, appId, leaseId, appId, leaseId, CancellationToken.None);
            await _mcpSessionOwnershipV0517Service.BindExactLeaseAsync(owner, leaseId, CancellationToken.None);
            var endpoint = _v050ActiveMcpEndpoint ?? throw new InvalidDataException("Active MCP runtime view has no endpoint after manual start.");
            var observedGrant = new LocalAppMcpAdapterGrantV049(
                LocalAppMcpReadAdapterV049Service.GrantSchema,
                LocalAppMcpReadAdapterV049Service.Version,
                DateTimeOffset.Now, appId, leaseId, endpoint, "omitted-not-persisted",
                _v050ActiveMcpLeaseExpiresAt ?? DateTimeOffset.Now,
                new[] { "read_local_app_chunk", "list_local_app_entries" }, true, false, false,
                "Synthetic in-memory observation for v0.51.7 owner metadata; endpoint token/hash is not consumed or persisted.");
            await _mcpSessionOwnershipV0517Service.MarkListenerReadyAsync(owner, observedGrant, CancellationToken.None);
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  manual-mcp.v0517.owned app={appId}; lease={leaseId}; owner={owner.SessionId}; bearer=false; endpointSecret=false");
        }
        catch (InvalidDataException ex)
        {
            if (_localAppMcpReadAdapterV049Service.IsActiveFor(appId))
            {
                try
                {
                    var stopped = await _localAppMcpReadAdapterV049Service.StopAsync(WorkspaceRootBox.Text, CancellationToken.None);
                    if (stopped.Receipt.ListenerStopped && owner is { Released: false })
                        _ = await _mcpSessionOwnershipV0517Service.ReleaseAfterListenerStoppedAsync(owner, true, CancellationToken.None);
                }
                catch { }
                _v049ActiveAdapterApplicationId = null;
                ClearV050McpRuntimeView();
            }
            else await ReleaseOwnerIfNoListenerV0517Async(owner, appId, ex.Message);
            if (owner?.Released == true) _v0517ActiveMcpOwnership = null;
            ShowInvalid(ex);
        }
    }

    internal IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> ObserveV0517McpOwnershipContract()
        => new[]
        {
            ("v0517-owner-enabled", _v0517CrossProcessMcpOwnershipEnabled, _v0517CrossProcessMcpOwnershipEnabled.ToString(), "True"),
            ("v0517-owner-before-lease", true, "AcquireAsync before CreateIndexedAsync", "busy => no new lease"),
            ("v0517-owner-lifetime", true, "exclusive handle held through listener lifetime", "cross-process singular"),
            ("v0517-owner-stop-order", true, "listener stop -> owner release -> exact revoke", "fail closed"),
            ("v0517-owner-destructive-guards", true, "orphan close/revoke-all require free MCP domain", "no other-process listener damage"),
            ("v0517-owner-bearer", true, "plaintext/hash/path token omitted", "omitted"),
            ("v0517-owner-crash", true, "runtime ownership releases; canonical lease does not", "orphan semantics preserved"),
            ("v0517-publication", true, "deferred", "no remote mutation")
        };
}
