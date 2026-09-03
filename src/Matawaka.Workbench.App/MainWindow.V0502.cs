using System.IO;
using System.Text;
using System.Windows;
using Matawaka.Workbench.Protocol;
using Matawaka.Workbench.Runtime;
using Microsoft.Win32;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly PlainMcpOAuthDiscoveryCompatV0502Service _plainMcpOauthCompatV0502Service = new();
    private readonly LocalCheckpointV0502Service _checkpointV0502Service = new();
    private readonly FixedGitHubPublicationV0502Service _fixedGitHubPublicationV0502Service = new();
    private bool _v0502LoadedBootstrapChecked;

    internal void ConfigureV0502Routing()
    {
        ConfigureV0501Routing();
        Title = "Matawaka Workbench v0.50.2";
        Loaded -= Window_LoadedV0501;
        Loaded += Window_LoadedV0502;
        PublishAcceptedButton.Click -= PublishAcceptedV0501Button_Click;
        PublishAcceptedButton.Click += PublishAcceptedV0502Button_Click;
        UpdateLocalAppButton.Click -= LocalAppsV0501Button_Click;
        UpdateLocalAppButton.Click += LocalAppsV0502Button_Click;
        Closing -= WindowV0501_Closing;
        Closing += WindowV0502_Closing;
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
    }

    private void WindowV0502_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            Task.Run(async () =>
            {
                await _secureMcpTunnelV0501Service.StopBestEffortAsync();
                await _plainMcpOauthCompatV0502Service.StopBestEffortAsync();
                await _secureMcpTunnelV050Service.StopBestEffortAsync();
                await _localAppMcpReadAdapterV049Service.StopBestEffortAsync();
            }).GetAwaiter().GetResult();
            _v050ActiveTunnelApplicationId = null;
            ClearV050McpRuntimeView();
        }
        catch
        {
            // Window close creates no retry semantics; bounded local child/listener shutdown is best-effort only.
        }
    }

    private async void LocalAppsV0502Button_Click(object sender, RoutedEventArgs e)
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
            try { await _localApplicationManagedRoleGuardV0371Service.EnsureRegistrationRoleAllowedAsync(selectedRoot, WorkspaceRootBox.Text, CancellationToken.None); }
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
        var choice = LocalAppsActionDialogV050.ShowChoice(this, appId, adapterActive, tunnelActive);
        switch (choice)
        {
            case LocalAppsActionChoiceV050.UpdateFromPackage: await UpdateSelectedLocalAppAsync(selectedRoot); break;
            case LocalAppsActionChoiceV050.BuildUpdatePackage: await BuildLocalAppPackageV038Async(selectedRoot); break;
            case LocalAppsActionChoiceV050.LaunchApp: await LaunchSelectedLocalAppV046Async(appId, selectedRoot); break;
            case LocalAppsActionChoiceV050.ExportUpdateContext: await ExportUpdateContextV046Async(appId); break;
            case LocalAppsActionChoiceV050.BindDevelopmentSource: await BindDevelopmentSourceV046Async(appId); break;
            case LocalAppsActionChoiceV050.ExportPrivateDevelopmentContext: await ExportPrivateDevelopmentContextV046Async(appId); break;
            case LocalAppsActionChoiceV050.ChatReadRelay: await ChatReadRelayV047Async(appId); break;
            case LocalAppsActionChoiceV050.ReadSessionLease: await CreateReadSessionLeaseV048Async(appId); break;
            case LocalAppsActionChoiceV050.RevokeReadLeases: await RevokeReadSessionLeasesV048Async(appId); break;
            case LocalAppsActionChoiceV050.StartReadOnlyMcpAdapter: await StartReadOnlyMcpAdapterV050Async(appId); break;
            case LocalAppsActionChoiceV050.StopReadOnlyMcpAdapter: await StopReadOnlyMcpAdapterV050Async(appId); break;
            case LocalAppsActionChoiceV050.StartSecureMcpTunnel: await StartSecureMcpTunnelV0502Async(appId); break;
            case LocalAppsActionChoiceV050.StopSecureMcpTunnel: await StopSecureMcpTunnelV0502Async(appId); break;
            case LocalAppsActionChoiceV050.Cancel:
            default: EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  local-app.v0502.choice.cancelled app={appId}; effect=false"); break;
        }
        RefreshInstalledAppsV044();
    }

    private async Task StartSecureMcpTunnelV0502Async(string appId)
    {
        if (!_localAppMcpReadAdapterV049Service.IsActiveFor(appId) || string.IsNullOrWhiteSpace(_v050ActiveMcpEndpoint) || _v050ActiveMcpLeaseId is null || _v050ActiveMcpLeaseExpiresAt is null)
        {
            ShowInvalid(new InvalidDataException("Start a fresh v0.48 read lease and the lease-gated local MCP adapter in this Workbench process first."));
            return;
        }
        var input = OpenAiSecureMcpTunnelDialogV050.ShowInput(this, appId);
        if (input is null) return;
        var runtimeKey = input.RuntimeApiKey;
        PlainMcpOAuthDiscoveryCompatGrantV0502? compat = null;
        try
        {
            var preview = await _secureMcpTunnelV0501Service.PreviewAsync(
                WorkspaceRootBox.Text, appId, _v050ActiveMcpLeaseId, _v050ActiveMcpLeaseExpiresAt.Value,
                input.TunnelId, runtimeKey, true, CancellationToken.None);
            var message = new StringBuilder();
            message.AppendLine("Запустить OpenAI Secure MCP Tunnel через v0.50.2 plain-MCP compatibility facade?");
            message.AppendLine();
            message.AppendLine($"ApplicationId: {preview.ApplicationId}");
            message.AppendLine($"LeaseId: {preview.LeaseId}");
            message.AppendLine($"Lease expires: {preview.LeaseExpiresAt:O}");
            message.AppendLine($"TunnelId: {preview.TunnelId}");
            message.AppendLine($"tunnel-client: {preview.TunnelClientReportedVersion}");
            message.AppendLine();
            message.AppendLine("v0.50.2 НЕ добавляет OAuth. Он запускает только дополнительный loopback facade: RFC9728 protected-resource metadata candidates отвечают 404, а POST MCP traffic проксируется в уже активный lease-gated MCP endpoint. Authorization/Cookie не проксируются. Filesystem/read authority не создаётся.");
            message.AppendLine();
            message.AppendLine("Tunnel readiness по-прежнему требует /readyz=2xx и наблюдается bounded/redacted v0.50.1 diagnostics.");
            if (MessageBox.Show(this, message.ToString(), "Start OpenAI Secure MCP Tunnel v0.50.2", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            SetV035PrimaryControlsEnabled(false);
            BeginRun($"secure-mcp-tunnel-v0.50.2-{DateTime.Now:yyyyMMddHHmmss}");
            compat = await _plainMcpOauthCompatV0502Service.StartAsync(appId, preview.LeaseId, preview.LeaseExpiresAt, _v050ActiveMcpEndpoint, _cts!.Token);
            var compatWritten = await _plainMcpOauthCompatV0502Service.WriteStartReceiptAsync(WorkspaceRootBox.Text, compat, _cts.Token);
            var grant = await _secureMcpTunnelV0501Service.StartAsync(WorkspaceRootBox.Text, preview, runtimeKey, compat.EndpointUrl, _cts.Token);
            _v050ActiveTunnelApplicationId = appId;
            var written = await _secureMcpTunnelV0501Service.WriteStartReceiptAsync(WorkspaceRootBox.Text, grant, _cts.Token);
            Clipboard.SetText(grant.TunnelId);
            LocalAppsTextBox.Text = CommandCodec.Serialize(new
            {
                Preview = preview,
                PlainMcpCompatibility = compatWritten.Receipt,
                PlainMcpCompatibilityReceiptPath = compatWritten.ReceiptPath,
                TunnelStartReceipt = written.Receipt,
                TunnelStartReceiptPath = written.ReceiptPath,
                OAuthProtectedResourceMetadataAdvertised = false,
                ProtectedResourceMetadataMode = "404 on exact root + path-specific candidates",
                AuthorizationForwardedToLocalMcp = false,
                RuntimeApiKeyPersisted = false,
                LocalMcpEndpointPersisted = false,
                ChatGptConnectionConfigured = false,
                NextHumanAction = "In ChatGPT developer/app settings choose Connection: Tunnel and use this same TunnelId, then call read_local_app_chunk."
            });
            OutputTabs.SelectedItem = LocalAppsTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: OpenAI Secure MCP Tunnel /readyz ready via no-auth facade for {appId}; tunnel id copied";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  secure-mcp-tunnel.v0502.ready app={appId}; lease={preview.LeaseId}; prmd=404; readyz=true; oauthAuthority=false; publicListener=false");
        }
        catch (OperationCanceledException)
        {
            await _secureMcpTunnelV0501Service.StopBestEffortAsync();
            await _plainMcpOauthCompatV0502Service.StopBestEffortAsync();
            _v050ActiveTunnelApplicationId = null;
            ShowCancelled();
        }
        catch (InvalidDataException ex)
        {
            await _secureMcpTunnelV0501Service.StopBestEffortAsync();
            await _plainMcpOauthCompatV0502Service.StopBestEffortAsync();
            _v050ActiveTunnelApplicationId = null;
            ShowInvalid(ex);
        }
        catch (Exception ex)
        {
            await _secureMcpTunnelV0501Service.StopBestEffortAsync();
            await _plainMcpOauthCompatV0502Service.StopBestEffortAsync();
            _v050ActiveTunnelApplicationId = null;
            ShowFailure(ex);
        }
        finally
        {
            runtimeKey = string.Empty;
            input = null;
            EndRun();
            SetV035PrimaryControlsEnabled(true);
            OperatorSurfaceV045Contract.Apply(this);
        }
    }

    private async Task StopSecureMcpTunnelV0502Async(string appId)
    {
        if (!_secureMcpTunnelV0501Service.IsActiveFor(appId))
        {
            await _plainMcpOauthCompatV0502Service.StopBestEffortAsync();
            MessageBox.Show(this, "Для выбранного приложения нет активного Secure MCP Tunnel child process.", "Stop Secure MCP Tunnel", MessageBoxButton.OK, MessageBoxImage.Information);
            _v050ActiveTunnelApplicationId = null;
            return;
        }
        if (MessageBox.Show(this, $"Остановить outbound OpenAI Secure MCP Tunnel для {appId}?\n\nTunnel child и v0.50.2 compatibility facade будут остановлены. Local MCP adapter и read lease останутся отдельными.", "Stop Secure MCP Tunnel", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            SetV035PrimaryControlsEnabled(false);
            BeginRun($"stop-secure-mcp-tunnel-v0.50.2-{DateTime.Now:yyyyMMddHHmmss}");
            var result = await _secureMcpTunnelV0501Service.StopAsync(WorkspaceRootBox.Text, _cts!.Token);
            await _plainMcpOauthCompatV0502Service.StopAsync(_cts.Token);
            _v050ActiveTunnelApplicationId = null;
            LocalAppsTextBox.Text = CommandCodec.Serialize(new { Stop = result.Receipt, StopReceiptPath = result.ReceiptPath, PlainMcpCompatibilityFacadeStopped = true, NextExplicitActions = new[] { "Stop read-only MCP adapter", "Revoke active read leases" } });
            OutputTabs.SelectedItem = LocalAppsTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: Secure MCP Tunnel + no-auth facade stopped for {appId}; adapter/lease unchanged";
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally { EndRun(); SetV035PrimaryControlsEnabled(true); OperatorSurfaceV045Contract.Apply(this); }
    }

    private async void Window_LoadedV0502(object sender, RoutedEventArgs e)
    {
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
        if (_v0502LoadedBootstrapChecked) return;
        _v0502LoadedBootstrapChecked = true;
        TransitionBootstrapV040Claim? claim = null;
        var beganRun = false;
        try
        {
            claim = await _transitionBootstrapV040Service.TryClaimFirstBootAsync(WorkspaceRootBox.Text, LocalCheckpointV0502Service.Version, LocalCheckpointV0502Service.TargetTag, CancellationToken.None);
            if (claim is null)
            {
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v0502 none; automaticValidation=false; automaticAccept=false");
                return;
            }
            SetV035PrimaryControlsEnabled(false);
            OperatorSurfaceV045Contract.Apply(this);
            BeginRun($"first-boot-bootstrap-v0.50.2-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;
            StatusText.Text = $"RUNNING: v0.50.2 plain-MCP OAuth discovery compatibility validation; lease={claim.Lease.LeaseId}";
            var tested = await RunV0502AcceptanceArtifactAsync(_cts!.Token);
            _lastAcceptanceReceipt = tested.Receipt;
            _lastAcceptanceArtifactPath = tested.ArtifactPath;
            _lastAcceptanceConsumed = false;
            if (!tested.Receipt.Passed)
            {
                await _transitionBootstrapV040Service.MarkFailedNoRetryAsync(claim.Lease, claim.LeasePath, "v0.50.2 validation returned Passed=false", CancellationToken.None);
                _currentTerminalState = CommandTerminalState.Failed;
                StatusText.Text = "FAILED: v0.50.2 validation did not pass; automatic local Accept refused; no retry authority";
                AcceptanceTextBox.Text = CommandCodec.Serialize(new { Bootstrap = claim.Lease, Acceptance = tested.Receipt, tested.ArtifactPath, AutomaticAcceptPerformed = false });
                OutputTabs.SelectedItem = AcceptanceTab;
                return;
            }
            var checkpointCandidate = await _checkpointV0502Service.PreviewAsync(WorkspaceRootBox.Text, tested.ArtifactPath, tested.Receipt, _cts.Token);
            var checkpoint = await _checkpointV0502Service.AcceptFromBootstrapAsync(checkpointCandidate, claim.Lease.LeaseId, _cts.Token);
            var checkpointPath = await LocalCheckpointV0502Service.WriteReceiptAsync(WorkspaceRootBox.Text, checkpoint, _cts.Token);
            var completed = await _transitionBootstrapV040Service.FinalizeAcceptedAsync(claim, tested.ArtifactPath, checkpointPath, _cts.Token);
            _lastAcceptanceConsumed = true;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: v0.50.2 validation PASS + automatic local Accept; {checkpoint.Tag} -> {checkpoint.NewHead}";
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Bootstrap = completed,
                Acceptance = tested.Receipt,
                AcceptanceArtifactPath = tested.ArtifactPath,
                Checkpoint = checkpoint,
                CheckpointReceiptPath = checkpointPath,
                FourButtonSurfacePreserved = true,
                FailedV050AndV0501LocalPredecessorsPreserved = true,
                PlainMcpPrmdMode = "404 / no OAuth advertisement",
                ReadAuthorityStillV048LeaseBound = true,
                V0501ReadinessDiagnosticsPreserved = true,
                AutomaticPublishPerformed = false,
                AutomaticLifecyclePerformed = false,
                NextExplicitActions = new[] { "Fresh read lease", "Start local MCP", "Start Secure MCP Tunnel", "ChatGPT read round-trip", "Tunnel Stop -> MCP Stop -> Lease Revoke", "Publish accepted", "Lifecycle receipt" }
            });
            OutputTabs.SelectedItem = AcceptanceTab;
        }
        catch (OperationCanceledException ex) { if (claim is not null) await TryFailBootstrapAsync(claim.Lease, claim.LeasePath, ex.Message); ShowCancelled(); }
        catch (InvalidDataException ex) { if (claim is not null) await TryFailBootstrapAsync(claim.Lease, claim.LeasePath, ex.Message); ShowInvalid(ex); }
        catch (Exception ex) { if (claim is not null) await TryFailBootstrapAsync(claim.Lease, claim.LeasePath, ex.Message); ShowFailure(ex); }
        finally
        {
            if (beganRun) EndRun();
            SetV035PrimaryControlsEnabled(true);
            OperatorSurfaceV045Contract.Apply(this);
            RefreshInstalledAppsV044();
            InstallV0441TreeDoubleClickRouting();
        }
    }

    private async Task<(WorkbenchAcceptanceReceipt Receipt, string ArtifactPath)> RunV0502AcceptanceArtifactAsync(CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(this);
        var context = new RuntimeContext(CatalogRootBox.Text, true, false);
        var receipt = await new WorkbenchV0502AcceptanceHarness(_acceptanceHarness, this).RunAsync(context, cancellationToken);
        var dir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"v0.50.2-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, CommandCodec.Serialize(receipt), new UTF8Encoding(false), cancellationToken);
        return (receipt, path);
    }

    private async void PublishAcceptedV0502Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            OperatorSurfaceV045Contract.Apply(this);
            SaveSettings();
            if (_v050ActiveTunnelApplicationId is not null && _secureMcpTunnelV0501Service.IsActiveFor(_v050ActiveTunnelApplicationId)) throw new InvalidDataException("Stop the active Secure MCP Tunnel before publishing accepted Workbench source.");
            if (_plainMcpOauthCompatV0502Service.IsActiveFor("life-situation-resolver")) throw new InvalidDataException("Stop the active v0.50.2 compatibility facade before publication.");
            if (_v049ActiveAdapterApplicationId is not null) throw new InvalidDataException("Stop the active MCP adapter before publishing accepted Workbench source.");
            var candidate = await _fixedGitHubPublicationV0502Service.PreviewAsync(WorkspaceRootBox.Text, CancellationToken.None);
            var preview = $"Опубликовать принятый Workbench v0.50.2?\n\nRemote: {candidate.RemoteName}\nAccepted HEAD: {candidate.Head}\nLocal parent: {candidate.Parent} / {FixedGitHubPublicationV0502Service.ExpectedParentTag}\nRemote base: {FixedGitHubPublicationV0502Service.ExpectedRemoteBase} / {FixedGitHubPublicationV0502Service.ExpectedRemoteBaseTag}\nTarget tag: {candidate.AcceptedTag}\n\nYes только после успешной real-host цепочки tunnel /readyz + ChatGPT read + Stop/adapter stop/revoke. Failed v0.50/v0.50.1 tags не публикуются.";
            if (MessageBox.Show(this, preview, "Publish accepted v0.50.2", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            SetV035PrimaryControlsEnabled(false);
            OperatorSurfaceV045Contract.Apply(this);
            BeginRun($"publish-v0.50.2-{DateTime.Now:yyyyMMddHHmmss}");
            var receipt = await _fixedGitHubPublicationV0502Service.PublishAsync(candidate, _cts!.Token);
            var path = await FixedGitHubPublicationV0502Service.WriteReceiptAsync(WorkspaceRootBox.Text, receipt, _cts.Token);
            AcceptanceTextBox.Text = CommandCodec.Serialize(new { Publication = receipt, PublicationReceiptPath = path, FailedV050Published = false, FailedV0501Published = false, PrivateAppBytesPublished = false, NextExplicitAction = "Lifecycle receipt" });
            OutputTabs.SelectedItem = AcceptanceTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: remote main/v0.50.2 tag == {receipt.LocalHead}; failed local tags suppressed";
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally { EndRun(); SetV035PrimaryControlsEnabled(true); OperatorSurfaceV045Contract.Apply(this); RefreshInstalledAppsV044(); }
    }
}
