using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalAppReadLeaseIndexedLifecycleV0515Service _indexedLeaseLifecycleV0515Service = new();
    private bool _v0515VerifiedActiveIndexEnabled;

    internal void ConfigureV0515Routing()
    {
        ConfigureV0514Routing();
        Title = "Matawaka Workbench v0.51.5";

        UpdateLocalAppButton.Click -= LocalAppsV0514Button_Click;
        UpdateLocalAppButton.Click += LocalAppsV0515Button_Click;

        _v0515VerifiedActiveIndexEnabled = true;
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
    }

    private async void LocalAppsV0515Button_Click(object sender, RoutedEventArgs e)
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
                await ShowVerifiedLiveReadSessionStatusV0515Async(appId);
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
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  local-app.v0515.choice.cancelled app={appId}; effect=false");
                break;
        }

        RefreshInstalledAppsV044();
    }

    private async Task<bool> EnsureVerifiedActiveIndexReadyV0515Async(string appId, string purpose)
    {
        try
        {
            var readiness = await _indexedLeaseLifecycleV0515Service.GetIndexReadinessAsync(
                WorkspaceRootBox.Text, appId, CancellationToken.None);
            if (readiness.Ready) return true;

            var message = new StringBuilder();
            message.AppendLine("Verified Active Lease Index requires bounded reconciliation before this action.");
            message.AppendLine();
            message.AppendLine($"ApplicationId: {appId}");
            message.AppendLine($"Purpose: {purpose}");
            message.AppendLine($"Current status: {readiness.Status}");
            message.AppendLine();
            message.AppendLine($"Yes scans at most {LocalAppActiveLeaseIndexV0515Service.MaxReconciliationStateFiles} Workbench-owned lease-state JSON files, rebuilds only a derived LeaseId index, and writes a reconciliation receipt.");
            message.AppendLine("Canonical lease-state files are not modified/deleted. Bearer plaintext/hash and application contents are not disclosed. No network/tunnel is used.");

            if (MessageBox.Show(this, message.ToString(), "Reconcile Verified Active Lease Index v0.51.5", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  active-index.v0515.reconcile.refused app={appId}; purpose={purpose}; effect=false");
                return false;
            }

            var reconciled = await _indexedLeaseLifecycleV0515Service.ReconcileIndexAsync(
                WorkspaceRootBox.Text, appId, CancellationToken.None);
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  active-index.v0515.reconciled app={appId}; canonical={reconciled.Receipt.CanonicalStateRecords}; live={reconciled.Receipt.LiveCandidatesIndexed}; revision={reconciled.Index.IndexRevision}; bearer=false");
            return true;
        }
        catch (InvalidDataException ex)
        {
            ShowInvalid(ex);
            return false;
        }
    }

    private async Task ShowVerifiedLiveReadSessionStatusV0515Async(string appId)
    {
        if (!await EnsureVerifiedActiveIndexReadyV0515Async(appId, "fast live-authority status")) return;
        try
        {
            var binding = CurrentMcpBindingV0514();
            var status = await _indexedLeaseLifecycleV0515Service.ObserveLiveAuthorityAsync(
                WorkspaceRootBox.Text, appId, binding.AppId, binding.LeaseId, CancellationToken.None);
            LocalAppsTextBox.Text = CommandCodec.Serialize(status);
            OutputTabs.SelectedItem = LocalAppsTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: verified live Read Session Status for {appId}; live={status.LiveLeaseCount}; orphan={status.OrphanClosureEligibleCount}; indexed={status.IndexedCandidatesObserved}; historicalScan=false; bearer=omitted";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  live-read-status.v0515 app={appId}; live={status.LiveLeaseCount}; orphan={status.OrphanClosureEligibleCount}; historicalScan=false; indexRevision={status.IndexRevision}; bearer=false");
        }
        catch (InvalidDataException ex)
        {
            ShowInvalid(ex);
        }
    }

    private void ShowCanonicalReadSessionHistoryPageV0515(string appId)
    {
        try
        {
            var status = ObserveReadSessionStatusV0514(appId);
            if (status.HistoricalLeaseCount > status.HistoryLimit)
            {
                var selectedOffset = LocalAppHistoryPageChooserV0514.Choose(this, appId, status.HistoricalLeaseCount, status.HistoryLimit);
                if (selectedOffset is null) return;
                if (selectedOffset.Value != status.HistoryOffset)
                    status = ObserveReadSessionStatusV0514(appId, selectedOffset.Value, status.HistoryLimit);
            }

            LocalAppsTextBox.Text = CommandCodec.Serialize(new
            {
                Schema = "matawaka.local-app-read-session-history-page/v0.51.5",
                Version = "0.51.5",
                ObservedAt = DateTimeOffset.Now,
                ApplicationId = appId,
                status.TotalLeaseRecords,
                status.HistoricalLeaseCount,
                status.HistoryOffset,
                status.HistoryLimit,
                status.HistoricalReturned,
                status.NextHistoryOffset,
                status.HistoryTruncated,
                HistoricalLeases = status.HistoricalLeases,
                LiveAuthorityIncluded = false,
                LiveAuthoritySource = "Use Read Session Status — verified active index",
                CanonicalHistoricalScanPerformed = true,
                BearerPlaintextDisclosed = false,
                BearerHashDisclosed = false,
                CanonicalStateMutationPerformed = false,
                Note = "Explicit historical evidence page uses the inherited v0.51.4 canonical scan. Live authority is intentionally separated into the verified-index status surface."
            });
            OutputTabs.SelectedItem = LocalAppsTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: canonical Read Session History Page for {appId}; history={status.HistoricalReturned}/{status.HistoricalLeaseCount}; offset={status.HistoryOffset}; liveAuthorityIncluded=false; bearer=omitted";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  read-history.v0515 app={appId}; returned={status.HistoricalReturned}; total={status.HistoricalLeaseCount}; offset={status.HistoryOffset}; canonicalScan=true; mutation=false");
        }
        catch (InvalidDataException ex)
        {
            ShowInvalid(ex);
        }
    }

    private async Task CreateIndexedReadLeaseAndAutoStartMcpV0515Async(string appId)
    {
        if (!await EnsureVerifiedActiveIndexReadyV0515Async(appId, "create bounded read lease")) return;
        if (_v049ActiveAdapterApplicationId is not null)
        {
            ShowInvalid(new InvalidDataException($"Local MCP adapter is already active for {_v049ActiveAdapterApplicationId}. Stop it before creating another read session."));
            return;
        }
        if (_v050ActiveTunnelApplicationId is not null && _secureMcpTunnelV0501Service.IsActiveFor(_v050ActiveTunnelApplicationId))
        {
            ShowInvalid(new InvalidDataException("Stop the active Secure MCP Tunnel before creating a new read session."));
            return;
        }

        var requestJson = LocalAppReadLeaseRequestDialogV048.ShowRequest(this, appId);
        if (requestJson is null) return;
        LocalAppIndexedLeaseCreateResultV0515? created = null;

        try
        {
            var preview = _indexedLeaseLifecycleV0515Service.PreviewFromJson(WorkspaceRootBox.Text, appId, requestJson, CancellationToken.None);
            var message = new StringBuilder();
            message.AppendLine("Создать bounded Read session lease, синхронизировать Verified Active Index и сразу запустить local read-only MCP?");
            message.AppendLine();
            message.AppendLine($"ApplicationId: {preview.ApplicationId}");
            message.AppendLine($"TTL: {preview.TtlSeconds}s; calls={preview.MaxCalls}; total bytes={preview.MaxTotalBytes:N0}; max/read={preview.MaxBytesPerRead:N0}");
            message.AppendLine("Scopes:");
            foreach (var scope in preview.Scopes) message.AppendLine($"  - {scope.Role}: {scope.PathPrefix}");
            message.AppendLine();
            message.AppendLine("Yes writes the active-index dirty marker before canonical lease creation, commits the new LeaseId to the derived index only after canonical creation succeeds, copies the exact grant JSON, and starts loopback MCP. Secure MCP Tunnel is not started.");
            if (MessageBox.Show(this, message.ToString(), "Read session + verified active index v0.51.5", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            SetV035PrimaryControlsEnabled(false);
            BeginRun($"read-lease-indexed-auto-mcp-v0.51.5-{DateTime.Now:yyyyMMddHHmmss}");
            created = await _indexedLeaseLifecycleV0515Service.CreateIndexedAsync(
                WorkspaceRootBox.Text, appId, preview, false, _cts!.Token);

            var exactGrantJson = LocalAppReadLeaseV048Service.SerializeGrant(created.Grant);
            Clipboard.SetText(exactGrantJson);
            var clipboardGrantJson = Clipboard.GetText();
            if (!string.Equals(exactGrantJson, clipboardGrantJson, StringComparison.Ordinal))
                throw new InvalidDataException("Clipboard grant round-trip mismatch. Lease and index were committed, but automatic MCP startup is refused.");

            var adapterPreview = _localAppMcpReadAdapterV049Service.PreviewFromGrantJson(
                WorkspaceRootBox.Text, appId, clipboardGrantJson, _cts.Token);
            if (!adapterPreview.LeaseId.Equals(created.Grant.LeaseId, StringComparison.Ordinal) ||
                !adapterPreview.BearerSha256.Equals(created.Receipt.BearerSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("MCP preview is not bound to the exact indexed just-created lease.");

            var adapterGrant = await _localAppMcpReadAdapterV049Service.StartAsync(
                WorkspaceRootBox.Text, appId, adapterPreview, clipboardGrantJson, _cts.Token);
            _v049ActiveAdapterApplicationId = appId;
            _v050ActiveMcpEndpoint = adapterGrant.EndpointUrl;
            _v050ActiveMcpLeaseId = adapterGrant.LeaseId;
            _v050ActiveMcpLeaseExpiresAt = adapterGrant.LeaseExpiresAt;
            var adapterWritten = await _localAppMcpReadAdapterV049Service.WriteStartReceiptAsync(
                WorkspaceRootBox.Text, adapterGrant, false, _cts.Token);

            LocalAppsTextBox.Text = CommandCodec.Serialize(new
            {
                Status = "INDEXED_READ_LEASE_AND_LOCAL_MCP_READY",
                LeasePreview = preview,
                LeaseGrant = created.Grant,
                LeaseCreationReceipt = created.Receipt,
                LeaseCreationReceiptPath = created.ReceiptPath,
                ActiveIndexRevision = created.ActiveIndexRevision,
                IndexedCandidates = created.IndexedCandidates,
                ClipboardRoundTripExact = true,
                AdapterPreview = adapterPreview,
                AdapterStartReceipt = adapterWritten.Receipt,
                AdapterStartReceiptPath = adapterWritten.ReceiptPath,
                Tools = adapterGrant.Tools,
                HistoricalCanonicalScanPerformed = false,
                BearerPlaintextStoredInIndex = false,
                BearerHashStoredInIndex = false,
                SecureMcpTunnelStarted = false
            });
            OutputTabs.SelectedItem = LocalAppsTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: indexed read lease + local MCP ready for {appId}; lease={created.Grant.LeaseId}; indexRev={created.ActiveIndexRevision}; tools={string.Join(",", adapterGrant.Tools)}; tunnel=false";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  read-lease.v0515.ready app={appId}; lease={created.Grant.LeaseId}; indexRev={created.ActiveIndexRevision}; adapter=true; tunnel=false");
        }
        catch (OperationCanceledException)
        {
            if (created is not null)
                ShowLeaseCreatedMcpStartFailureV0511(appId, created.Grant, created.Receipt, created.ReceiptPath, "operation cancelled after indexed lease creation");
            else
                ShowCancelled();
        }
        catch (InvalidDataException ex)
        {
            if (created is not null)
                ShowLeaseCreatedMcpStartFailureV0511(appId, created.Grant, created.Receipt, created.ReceiptPath, ex.Message);
            else
                ShowInvalid(ex);
        }
        catch (Exception ex)
        {
            if (created is not null)
                ShowLeaseCreatedMcpStartFailureV0511(appId, created.Grant, created.Receipt, created.ReceiptPath, "local MCP startup failed after indexed lease creation");
            else
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

    private async Task EndIndexedReadSessionV0515Async(string appId)
    {
        if (!await EnsureVerifiedActiveIndexReadyV0515Async(appId, "end exact bound read session")) return;
        if (!_localAppMcpReadAdapterV049Service.IsActiveFor(appId) || _v049ActiveAdapterApplicationId != appId)
        {
            ShowInvalid(new InvalidDataException("End Read Session requires the selected app to own the active local MCP adapter."));
            return;
        }
        if (_secureMcpTunnelV0501Service.IsActiveFor(appId))
        {
            ShowInvalid(new InvalidDataException("Stop the Secure MCP Tunnel first."));
            return;
        }
        var leaseId = _v050ActiveMcpLeaseId;
        if (string.IsNullOrWhiteSpace(leaseId))
        {
            ShowInvalid(new InvalidDataException("Active MCP runtime view has no exact bound LeaseId."));
            return;
        }
        if (MessageBox.Show(this, $"End Read Session?\n\nApplicationId: {appId}\nExact LeaseId: {leaseId}\n\nYes stops MCP first, then exact-revokes canonical state and commits removal from Verified Active Index. Sibling leases/history are untouched.", "End indexed Read Session v0.51.5", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        LocalAppMcpAdapterStopReceiptV049? stopReceipt = null;
        string? stopPath = null;
        try
        {
            SetV035PrimaryControlsEnabled(false);
            BeginRun($"end-indexed-read-session-v0.51.5-{DateTime.Now:yyyyMMddHHmmss}");
            try
            {
                var stopped = await _localAppMcpReadAdapterV049Service.StopAsync(WorkspaceRootBox.Text, _cts!.Token);
                stopReceipt = stopped.Receipt;
                stopPath = stopped.ReceiptPath;
            }
            catch
            {
                await _localAppMcpReadAdapterV049Service.StopBestEffortAsync();
            }
            var inactive = !_localAppMcpReadAdapterV049Service.IsActiveFor(appId);
            _v049ActiveAdapterApplicationId = null;
            ClearV050McpRuntimeView();
            if (!inactive)
            {
                ShowEndReadSessionPartialV0512(appId, leaseId, false, stopReceipt, stopPath, null, null, "Local MCP still active; exact indexed closure refused.");
                return;
            }

            var revoked = await _indexedLeaseLifecycleV0515Service.RevokeExactIndexedAsync(
                WorkspaceRootBox.Text, appId, leaseId, _cts!.Token);
            LocalAppsTextBox.Text = CommandCodec.Serialize(new
            {
                Status = "READ_SESSION_ENDED_EXACT_LEASE_REVOKED_INDEX_COMMITTED",
                ApplicationId = appId,
                LeaseId = leaseId,
                AdapterStopReceipt = stopReceipt,
                AdapterStopReceiptPath = stopPath,
                ExactLeaseRevokeReceipt = revoked.ExactReceipt,
                ExactLeaseRevokeReceiptPath = revoked.ExactReceiptPath,
                revoked.ActiveIndexRevision,
                revoked.IndexedCandidates,
                SiblingLeasesRevoked = revoked.ExactReceipt.SiblingLeasesRevoked,
                HistoricalEvidenceDeleted = false,
                BearerPlaintextUsedOrDisclosed = false,
                SecureMcpTunnelChanged = false
            });
            OutputTabs.SelectedItem = LocalAppsTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: indexed Read Session ended for {appId}; exact lease={leaseId} revoked; indexRev={revoked.ActiveIndexRevision}; siblings=0";
        }
        catch (InvalidDataException ex)
        {
            ShowEndReadSessionPartialV0512(appId, leaseId, true, stopReceipt, stopPath, null, null, ex.Message + " Active index may require reconciliation.");
        }
        finally
        {
            EndRun();
            SetV035PrimaryControlsEnabled(true);
            OperatorSurfaceV045Contract.Apply(this);
            RefreshInstalledAppsV044();
        }
    }

    private async Task EndIndexedOrphanedReadSessionV0515Async(string appId)
    {
        if (!await EnsureVerifiedActiveIndexReadyV0515Async(appId, "exact orphan closure")) return;
        try
        {
            var binding = CurrentMcpBindingV0514();
            var observed = await _indexedLeaseLifecycleV0515Service.ObserveLiveAuthorityAsync(
                WorkspaceRootBox.Text, appId, binding.AppId, binding.LeaseId, CancellationToken.None);
            var eligible = observed.LiveAuthorities.Where(x => x.OrphanClosureEligible).ToArray();
            if (eligible.Length == 0)
            {
                MessageBox.Show(this, "No indexed live unbound lease is eligible for orphan closure.", "End orphaned read session", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var selected = LocalAppOrphanLeaseChooserV0513.Choose(this, appId, eligible);
            if (string.IsNullOrWhiteSpace(selected)) return;
            var target = await _indexedLeaseLifecycleV0515Service.ObserveIndexedExactLiveLeaseAsync(
                WorkspaceRootBox.Text, appId, selected, binding.AppId, binding.LeaseId, CancellationToken.None);
            if (!target.OrphanClosureEligible || target.BoundToActiveLocalMcp)
                throw new InvalidDataException("Selected indexed LeaseId is no longer an exact live unbound orphan candidate.");
            if (MessageBox.Show(this, $"End indexed orphan Read Session?\n\nApplicationId: {appId}\nExact LeaseId: {target.LeaseId}\nScopes: {string.Join(", ", target.Scopes.Select(x => $"{x.Role}:{x.PathPrefix}"))}\n\nOnly this canonical LeaseId is revoked and removed from the derived active index. Historical evidence and sibling leases are preserved.", "End indexed orphan v0.51.5", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            var revoked = await _indexedLeaseLifecycleV0515Service.RevokeExactIndexedAsync(
                WorkspaceRootBox.Text, appId, target.LeaseId, CancellationToken.None);
            LocalAppsTextBox.Text = CommandCodec.Serialize(new
            {
                Status = "ORPHAN_READ_SESSION_ENDED_EXACT_LEASE_REVOKED_INDEX_COMMITTED",
                ApplicationId = appId,
                LeaseId = target.LeaseId,
                Before = target,
                ExactLeaseRevokeReceipt = revoked.ExactReceipt,
                ExactLeaseRevokeReceiptPath = revoked.ExactReceiptPath,
                revoked.ActiveIndexRevision,
                revoked.IndexedCandidates,
                HistoricalEvidenceDeleted = false,
                SiblingLeasesRevoked = revoked.ExactReceipt.SiblingLeasesRevoked,
                ActiveLocalMcpChanged = false,
                BearerPlaintextUsedOrDisclosed = false
            });
            OutputTabs.SelectedItem = LocalAppsTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: indexed orphan session ended for {appId}; lease={target.LeaseId}; indexRev={revoked.ActiveIndexRevision}; siblings=0";
        }
        catch (InvalidDataException ex)
        {
            ShowInvalid(ex);
        }
    }

    private async Task RevokeAllAndReconcileV0515Async(string appId)
    {
        if (MessageBox.Show(this, $"Revoke ALL currently active read leases for {appId}?\n\nThis remains a recovery action. Canonical active leases will be revoked using the existing v0.48 recovery path, then the derived active index will be bounded-reconciled. Historical canonical state is preserved.", "Revoke ALL + reconcile v0.51.5", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        try
        {
            var result = await _indexedLeaseLifecycleV0515Service.RevokeAllAndReconcileAsync(
                WorkspaceRootBox.Text, appId, CancellationToken.None);
            LocalAppsTextBox.Text = CommandCodec.Serialize(result);
            OutputTabs.SelectedItem = LocalAppsTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: revoke-all recovery for {appId}; revoked={result.LegacyReceipt.RevokedLeases}; indexed={result.IndexedCandidates}; indexRev={result.ActiveIndexRevision}; history preserved";
        }
        catch (InvalidDataException ex)
        {
            ShowInvalid(ex);
        }
    }

    private async Task StartVerifiedManualMcpV0515Async(string appId)
    {
        if (!await EnsureVerifiedActiveIndexReadyV0515Async(appId, "manual MCP startup")) return;
        await StartReadOnlyMcpAdapterV050Async(appId);
        var activeLeaseId = _v050ActiveMcpLeaseId;
        if (!_localAppMcpReadAdapterV049Service.IsActiveFor(appId) || string.IsNullOrWhiteSpace(activeLeaseId)) return;
        try
        {
            _ = await _indexedLeaseLifecycleV0515Service.ObserveIndexedExactLiveLeaseAsync(
                WorkspaceRootBox.Text, appId, activeLeaseId, appId, activeLeaseId, CancellationToken.None);
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  manual-mcp.v0515.index-verified app={appId}; lease={activeLeaseId}");
        }
        catch (InvalidDataException ex)
        {
            await _localAppMcpReadAdapterV049Service.StopBestEffortAsync();
            _v049ActiveAdapterApplicationId = null;
            ClearV050McpRuntimeView();
            ShowInvalid(new InvalidDataException("Manual MCP startup was stopped because its exact LeaseId is not verified by the active index: " + ex.Message));
        }
    }

    internal IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> ObserveV0515VerifiedIndexContract()
        => new[]
        {
            ("v0515-index-enabled", _v0515VerifiedActiveIndexEnabled, _v0515VerifiedActiveIndexEnabled.ToString(), "True"),
            ("v0515-live-status", true, "verified index candidates + exact canonical read", "no historical enumeration"),
            ("v0515-history-separate", true, "explicit canonical page action", "separate from authority discovery"),
            ("v0515-first-use-reconcile", true, "explicit bounded prompt", "max 4096"),
            ("v0515-create-dirty", true, "dirty before canonical create; commit after", "fail closed"),
            ("v0515-close-dirty", true, "dirty before exact revoke; commit after", "fail closed"),
            ("v0515-bearer-index", true, "plaintext/hash omitted", "omitted"),
            ("v0515-publication", true, "deferred", "no remote mutation")
        };
}