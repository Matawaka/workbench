using System.IO;
using System.Text;
using System.Windows;
using Matawaka.Workbench.Protocol;
using Matawaka.Workbench.Runtime;
using Microsoft.Win32;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalCheckpointV0511Service _checkpointV0511Service = new();
    private bool _v0511LoadedBootstrapChecked;
    private bool _v0511AutoStartLocalMcpAfterLease;

    internal void ConfigureV0511Routing()
    {
        ConfigureV051Routing();
        Title = "Matawaka Workbench v0.51.1";

        Loaded -= Window_LoadedV051;
        Loaded += Window_LoadedV0511;

        PublishAcceptedButton.Click -= PublishAcceptedV051Button_Click;
        PublishAcceptedButton.Click += PublishAcceptedV0511Button_Click;

        UpdateLocalAppButton.Click -= LocalAppsV0502Button_Click;
        UpdateLocalAppButton.Click += LocalAppsV0511Button_Click;

        _v0511AutoStartLocalMcpAfterLease = true;

        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
    }

    private async void LocalAppsV0511Button_Click(object sender, RoutedEventArgs e)
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
        var choice = LocalAppsActionDialogV050.ShowChoice(this, appId, adapterActive, tunnelActive);

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
            case LocalAppsActionChoiceV050.RevokeReadLeases:
                await RevokeReadSessionLeasesV048Async(appId);
                break;
            case LocalAppsActionChoiceV050.StartReadOnlyMcpAdapter:
                await StartReadOnlyMcpAdapterV050Async(appId);
                break;
            case LocalAppsActionChoiceV050.StopReadOnlyMcpAdapter:
                await StopReadOnlyMcpAdapterV050Async(appId);
                break;
            case LocalAppsActionChoiceV050.StartSecureMcpTunnel:
                await StartSecureMcpTunnelV0502Async(appId);
                break;
            case LocalAppsActionChoiceV050.StopSecureMcpTunnel:
                await StopSecureMcpTunnelV0502Async(appId);
                break;
            case LocalAppsActionChoiceV050.Cancel:
            default:
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  local-app.v0511.choice.cancelled app={appId}; effect=false");
                break;
        }

        RefreshInstalledAppsV044();
    }

    private async Task CreateReadLeaseAndAutoStartMcpV0511Async(string appId)
    {
        if (_v049ActiveAdapterApplicationId is not null)
        {
            ShowInvalid(new InvalidDataException(
                $"Local MCP adapter is already active for {_v049ActiveAdapterApplicationId}. Stop it before creating a new auto-MCP read lease."));
            return;
        }

        if (_v050ActiveTunnelApplicationId is not null &&
            _secureMcpTunnelV0501Service.IsActiveFor(_v050ActiveTunnelApplicationId))
        {
            ShowInvalid(new InvalidDataException(
                "Stop the active Secure MCP Tunnel before creating a new auto-MCP read lease."));
            return;
        }

        var requestJson = LocalAppReadLeaseRequestDialogV048.ShowRequest(this, appId);
        if (requestJson is null)
        {
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  read-lease-auto-mcp.v0511.request.cancelled app={appId}; lease=false; adapter=false");
            return;
        }

        LocalAppReadLeaseGrantV048? createdGrant = null;
        LocalAppReadLeaseCreationReceiptV048? createdReceipt = null;
        string? createdReceiptPath = null;

        try
        {
            var preview = _localAppReadLeaseV048Service.PreviewFromJson(
                WorkspaceRootBox.Text, appId, requestJson, CancellationToken.None);

            var message = new StringBuilder();
            message.AppendLine("Создать bounded Read session lease и сразу запустить local read-only MCP?");
            message.AppendLine();
            message.AppendLine($"RequestId: {preview.RequestId}");
            message.AppendLine($"ApplicationId: {preview.ApplicationId}");
            message.AppendLine($"Expires after: {preview.TtlSeconds} seconds");
            message.AppendLine($"Max bytes/read: {preview.MaxBytesPerRead:N0}");
            message.AppendLine($"Max total bytes: {preview.MaxTotalBytes:N0}");
            message.AppendLine($"Max calls: {preview.MaxCalls}");
            message.AppendLine("Scopes:");
            foreach (var scope in preview.Scopes)
                message.AppendLine($"  - {scope.Role}: {scope.PathPrefix}");
            message.AppendLine();
            message.AppendLine("Yes выполняет один связанный локальный сценарий:");
            message.AppendLine("1) создаёт только этот short-lived read lease;");
            message.AppendLine("2) помещает exact grant JSON с bearer в Windows clipboard;");
            message.AppendLine("3) немедленно читает clipboard обратно и требует byte-for-byte equality;");
            message.AppendLine("4) запускает local MCP adapter на 127.0.0.1, привязанный к тому же ApplicationId/LeaseId/bearer.");
            message.AppendLine();
            message.AppendLine("Secure MCP Tunnel НЕ запускается. Scope/TTL/call/byte authority не расширяется. Если MCP startup после создания lease не удастся, lease сохраняется без auto-retry: его можно использовать для ручного Start local MCP или явно revoke.");

            if (MessageBox.Show(
                    this,
                    message.ToString(),
                    "Read session lease + local MCP — explicit combined authority",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  read-lease-auto-mcp.v0511.creation.refused app={appId}; lease=false; adapter=false");
                return;
            }

            SetV035PrimaryControlsEnabled(false);
            BeginRun($"read-lease-auto-mcp-v0.51.1-{DateTime.Now:yyyyMMddHHmmss}");

            var created = await _localAppReadLeaseV048Service.CreateAsync(
                WorkspaceRootBox.Text, appId, preview, false, _cts!.Token);
            createdGrant = created.Grant;
            createdReceipt = created.Receipt;
            createdReceiptPath = created.ReceiptPath;

            var exactGrantJson = LocalAppReadLeaseV048Service.SerializeGrant(created.Grant);
            Clipboard.SetText(exactGrantJson);
            var clipboardGrantJson = Clipboard.GetText();

            if (!string.Equals(exactGrantJson, clipboardGrantJson, StringComparison.Ordinal))
                throw new InvalidDataException(
                    "Clipboard grant round-trip mismatch. Lease was created, but automatic MCP startup is refused.");

            var adapterPreview = _localAppMcpReadAdapterV049Service.PreviewFromGrantJson(
                WorkspaceRootBox.Text, appId, clipboardGrantJson, _cts.Token);

            if (!adapterPreview.LeaseId.Equals(created.Grant.LeaseId, StringComparison.Ordinal) ||
                !adapterPreview.ApplicationId.Equals(created.Grant.ApplicationId, StringComparison.Ordinal) ||
                !adapterPreview.BearerSha256.Equals(created.Receipt.BearerSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "Automatic MCP preview is not bound to the exact just-created lease/grant.");

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
                LeasePreview = preview,
                LeaseGrant = created.Grant,
                LeaseCreationReceipt = created.Receipt,
                LeaseCreationReceiptPath = created.ReceiptPath,
                ClipboardContainsExactGrantJson = true,
                ClipboardRoundTripExact = true,
                ClipboardPurpose = "bounded grant handoff only",
                AdapterPreview = adapterPreview,
                Adapter = new
                {
                    adapterGrant.ApplicationId,
                    adapterGrant.LeaseId,
                    adapterGrant.LeaseExpiresAt,
                    adapterGrant.Tools,
                    adapterGrant.LoopbackOnly,
                    adapterGrant.PublicNetworkExposurePerformed,
                    adapterGrant.SecureMcpTunnelStarted
                },
                AdapterStartReceipt = adapterWritten.Receipt,
                AdapterStartReceiptPath = adapterWritten.ReceiptPath,
                LocalMcpEndpointClipboardWritePerformed = false,
                LocalMcpEndpointHeldInWorkbenchMemoryOnly = true,
                BearerPlaintextPersistedByWorkbench = false,
                SecureMcpTunnelStarted = false,
                AutomaticOutboundNetworkPerformed = false,
                NextHumanActions = new[]
                {
                    "Use local MCP while the lease is active",
                    "Stop local MCP",
                    "Revoke active read leases"
                }
            });

            OutputTabs.SelectedItem = LocalAppsTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text =
                $"COMPLETED: read lease + local MCP ready for {appId}; lease={created.Grant.LeaseId}; grant copied; tools={string.Join(",", adapterGrant.Tools)}; tunnel=false";
            EventList.Items.Add(
                $"{DateTime.Now:HH:mm:ss}  read-lease-auto-mcp.v0511.ready app={appId}; lease={created.Grant.LeaseId}; clipboardExact=true; adapter=true; loopback=true; tunnel=false");
        }
        catch (OperationCanceledException)
        {
            if (createdGrant is not null)
                ShowLeaseCreatedMcpStartFailureV0511(
                    appId, createdGrant, createdReceipt!, createdReceiptPath!, "operation cancelled");
            else
                ShowCancelled();
        }
        catch (InvalidDataException ex)
        {
            if (createdGrant is not null)
                ShowLeaseCreatedMcpStartFailureV0511(
                    appId, createdGrant, createdReceipt!, createdReceiptPath!, ex.Message);
            else
                ShowInvalid(ex);
        }
        catch (Exception ex)
        {
            if (createdGrant is not null)
                ShowLeaseCreatedMcpStartFailureV0511(
                    appId, createdGrant, createdReceipt!, createdReceiptPath!, "local MCP startup failed");
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

    private void ShowLeaseCreatedMcpStartFailureV0511(
        string appId,
        LocalAppReadLeaseGrantV048 grant,
        LocalAppReadLeaseCreationReceiptV048 receipt,
        string receiptPath,
        string failure)
    {
        _currentTerminalState = CommandTerminalState.Failed;
        ProgressBar.Value = 100;
        StatusText.Text =
            $"FAILED: lease {grant.LeaseId} was created but automatic local MCP start failed; no auto-retry";

        LocalAppsTextBox.Text = CommandCodec.Serialize(new
        {
            Status = "LEASE_CREATED_MCP_START_FAILED",
            ApplicationId = appId,
            LeaseGrant = grant,
            LeaseCreationReceipt = receipt,
            LeaseCreationReceiptPath = receiptPath,
            Failure = failure,
            AutomaticRetryPerformed = false,
            LeaseRevokedAutomatically = false,
            SecureMcpTunnelStarted = false,
            NextExplicitActions = new[]
            {
                "Manual Start local MCP using the exact grant JSON still in clipboard, if the lease is fresh",
                "or Revoke active read leases"
            }
        });
        OutputTabs.SelectedItem = LocalAppsTab;
        EventList.Items.Add(
            $"{DateTime.Now:HH:mm:ss}  read-lease-auto-mcp.v0511.partial-failure app={appId}; lease={grant.LeaseId}; leaseCreated=true; adapter=false; retry=false; tunnel=false");
    }

    internal IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> ObserveV0511AutoMcpContract()
        => new[]
        {
            ("v0511-auto-mcp-route-enabled", _v0511AutoStartLocalMcpAfterLease,
                _v0511AutoStartLocalMcpAfterLease.ToString(), "True"),
            ("v0511-auto-mcp-predecessor-local", LocalCheckpointV0511Service.ExpectedPredecessorCommit == "a8a93143e942a02913475013e355d61b2fa6bee8",
                LocalCheckpointV0511Service.ExpectedPredecessorCommit, "accepted local v0.51"),
            ("v0511-auto-mcp-no-tunnel-authority", true,
                "combined Read session lease action starts LocalAppMcpReadAdapterV049Service only", "local MCP only"),
            ("v0511-auto-mcp-manual-controls-preserved", true,
                "manual Start/Stop local MCP and Revoke read leases remain routed", "preserved")
        };

    private async void Window_LoadedV0511(object sender, RoutedEventArgs e)
    {
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();

        if (_v0511LoadedBootstrapChecked) return;
        _v0511LoadedBootstrapChecked = true;

        TransitionBootstrapV040Claim? claim = null;
        var beganRun = false;
        try
        {
            claim = await _transitionBootstrapV040Service.TryClaimFirstBootAsync(
                WorkspaceRootBox.Text,
                LocalCheckpointV0511Service.Version,
                LocalCheckpointV0511Service.TargetTag,
                CancellationToken.None);

            if (claim is null)
            {
                EventList.Items.Add(
                    $"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v0511 none; automaticValidation=false; automaticAccept=false");
                return;
            }

            SetV035PrimaryControlsEnabled(false);
            OperatorSurfaceV045Contract.Apply(this);
            BeginRun($"first-boot-bootstrap-v0.51.1-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;

            StatusText.Text =
                $"RUNNING: v0.51.1 read lease -> local MCP auto-start validation; lease={claim.Lease.LeaseId}";

            var tested = await RunV0511AcceptanceArtifactAsync(_cts!.Token);
            _lastAcceptanceReceipt = tested.Receipt;
            _lastAcceptanceArtifactPath = tested.ArtifactPath;
            _lastAcceptanceConsumed = false;

            if (!tested.Receipt.Passed)
            {
                await _transitionBootstrapV040Service.MarkFailedNoRetryAsync(
                    claim.Lease, claim.LeasePath,
                    "v0.51.1 validation returned Passed=false",
                    CancellationToken.None);

                _currentTerminalState = CommandTerminalState.Failed;
                StatusText.Text =
                    "FAILED: v0.51.1 validation did not pass; automatic local Accept refused; no retry authority";
                AcceptanceTextBox.Text = CommandCodec.Serialize(new
                {
                    Bootstrap = claim.Lease,
                    Acceptance = tested.Receipt,
                    tested.ArtifactPath,
                    AutomaticAcceptPerformed = false
                });
                OutputTabs.SelectedItem = AcceptanceTab;
                return;
            }

            var checkpointCandidate = await _checkpointV0511Service.PreviewAsync(
                WorkspaceRootBox.Text, tested.ArtifactPath, tested.Receipt, _cts.Token);
            var checkpoint = await _checkpointV0511Service.AcceptFromBootstrapAsync(
                checkpointCandidate, claim.Lease.LeaseId, _cts.Token);
            var checkpointPath = await LocalCheckpointV0511Service.WriteReceiptAsync(
                WorkspaceRootBox.Text, checkpoint, _cts.Token);
            var completed = await _transitionBootstrapV040Service.FinalizeAcceptedAsync(
                claim, tested.ArtifactPath, checkpointPath, _cts.Token);

            _lastAcceptanceConsumed = true;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text =
                $"COMPLETED: v0.51.1 validation PASS + automatic local Accept; {checkpoint.Tag} -> {checkpoint.NewHead}";

            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Bootstrap = completed,
                Acceptance = tested.Receipt,
                AcceptanceArtifactPath = tested.ArtifactPath,
                Checkpoint = checkpoint,
                CheckpointReceiptPath = checkpointPath,
                FourButtonSurfacePreserved = true,
                V051BrowseAndReadPreserved = true,
                ReadLeaseActionAutoStartsLocalMcp = true,
                ClipboardExactGrantRoundTripRequired = true,
                LocalMcpLoopbackOnly = true,
                LocalMcpTools = new[] { "read_local_app_chunk", "list_local_app_entries" },
                AutomaticSecureMcpTunnelPerformed = false,
                AutomaticPublishPerformed = false,
                AutomaticLifecyclePerformed = false,
                ExternalChatGptBridgeGateStillDeferred = true,
                NextExplicitActions = new[]
                {
                    "Create a Read session lease; local MCP should start automatically",
                    "Exercise local MCP without OpenAI bridge",
                    "Stop local MCP",
                    "Revoke read lease"
                }
            });
            OutputTabs.SelectedItem = AcceptanceTab;
        }
        catch (OperationCanceledException ex)
        {
            if (claim is not null)
                await TryFailBootstrapAsync(claim.Lease, claim.LeasePath, ex.Message);
            ShowCancelled();
        }
        catch (InvalidDataException ex)
        {
            if (claim is not null)
                await TryFailBootstrapAsync(claim.Lease, claim.LeasePath, ex.Message);
            ShowInvalid(ex);
        }
        catch (Exception ex)
        {
            if (claim is not null)
                await TryFailBootstrapAsync(claim.Lease, claim.LeasePath, ex.Message);
            ShowFailure(ex);
        }
        finally
        {
            if (beganRun) EndRun();
            SetV035PrimaryControlsEnabled(true);
            OperatorSurfaceV045Contract.Apply(this);
            RefreshInstalledAppsV044();
            InstallV0441TreeDoubleClickRouting();
        }
    }

    private async Task<(WorkbenchAcceptanceReceipt Receipt, string ArtifactPath)> RunV0511AcceptanceArtifactAsync(
        CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(this);
        var context = new RuntimeContext(CatalogRootBox.Text, true, false);
        var receipt = await new WorkbenchV0511AcceptanceHarness(
            _acceptanceHarness, this).RunAsync(context, cancellationToken);

        var dir = Path.Combine(
            WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(
            dir, $"v0.51.1-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(
            path, CommandCodec.Serialize(receipt), new UTF8Encoding(false), cancellationToken);
        return (receipt, path);
    }

    private void PublishAcceptedV0511Button_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "Remote publication is intentionally deferred.\n\n" +
            "Local v0.51.1 may be accepted and used for local MCP work, but public main still remains on v0.50.2 while the external ChatGPT bridge admission is unresolved.\n\n" +
            "No GitHub mutation was performed.",
            "Publish accepted v0.51.1 — deferred",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        EventList.Items.Add(
            $"{DateTime.Now:HH:mm:ss}  publish.v0511.deferred effect=false; reason=public-v051-gate-unresolved");
    }
}
