using System.IO;
using System.Text;
using System.Windows;
using Matawaka.Workbench.Protocol;
using Matawaka.Workbench.Runtime;
using Microsoft.Win32;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalCheckpointV050Service _checkpointV050Service = new();
    private readonly FixedGitHubPublicationV050Service _fixedGitHubPublicationV050Service = new();
    private readonly OpenAiSecureMcpTunnelV050Service _secureMcpTunnelV050Service = new();
    private bool _v050LoadedBootstrapChecked;
    private string? _v050ActiveMcpEndpoint;
    private string? _v050ActiveMcpLeaseId;
    private DateTimeOffset? _v050ActiveMcpLeaseExpiresAt;
    private string? _v050ActiveTunnelApplicationId;

    internal void ConfigureV050Routing()
    {
        ConfigureV0491Routing();
        Title = "Matawaka Workbench v0.50";
        Loaded -= Window_LoadedV0491;
        Loaded += Window_LoadedV050;
        PublishAcceptedButton.Click -= PublishAcceptedV0491Button_Click;
        PublishAcceptedButton.Click += PublishAcceptedV050Button_Click;
        UpdateLocalAppButton.Click -= LocalAppsV049Button_Click;
        UpdateLocalAppButton.Click += LocalAppsV050Button_Click;
        Closing -= WindowV049_Closing;
        Closing += WindowV050_Closing;
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
    }

    private void WindowV050_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            Task.Run(async () =>
            {
                await _secureMcpTunnelV050Service.StopBestEffortAsync();
                await _localAppMcpReadAdapterV049Service.StopBestEffortAsync();
            }).GetAwaiter().GetResult();
            _v050ActiveTunnelApplicationId = null;
            ClearV050McpRuntimeView();
        }
        catch
        {
            // Window close creates no retry semantics; bounded child/listener shutdown is best-effort only.
        }
    }

    private async void LocalAppsV050Button_Click(object sender, RoutedEventArgs e)
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
        if (_v050ActiveTunnelApplicationId is not null && !_secureMcpTunnelV050Service.IsActiveFor(_v050ActiveTunnelApplicationId))
            _v050ActiveTunnelApplicationId = null;
        var adapterActive = _localAppMcpReadAdapterV049Service.IsActiveFor(appId);
        var tunnelActive = _secureMcpTunnelV050Service.IsActiveFor(appId);
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
            case LocalAppsActionChoiceV050.StartSecureMcpTunnel: await StartSecureMcpTunnelV050Async(appId); break;
            case LocalAppsActionChoiceV050.StopSecureMcpTunnel: await StopSecureMcpTunnelV050Async(appId); break;
            case LocalAppsActionChoiceV050.Cancel:
            default: EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  local-app.v050.choice.cancelled app={appId}; effect=false"); break;
        }
        RefreshInstalledAppsV044();
    }

    private async Task StartReadOnlyMcpAdapterV050Async(string appId)
    {
        var grantJson = LocalAppMcpAdapterGrantDialogV049.ShowGrant(this, appId);
        if (grantJson is null)
        {
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  mcp-adapter.v050.start.cancelled app={appId}; listener=false");
            return;
        }
        var started = false;
        try
        {
            var preview = _localAppMcpReadAdapterV049Service.PreviewFromGrantJson(WorkspaceRootBox.Text, appId, grantJson, CancellationToken.None);
            var message = new StringBuilder();
            message.AppendLine("Запустить read-only MCP adapter на локальном loopback?");
            message.AppendLine();
            message.AppendLine($"ApplicationId: {preview.ApplicationId}");
            message.AppendLine($"LeaseId: {preview.LeaseId}");
            message.AppendLine($"Lease expires: {preview.ExpiresAt:O}");
            message.AppendLine($"Remaining calls: {preview.RemainingCalls}");
            message.AppendLine($"Remaining bytes: {preview.RemainingBytes:N0}");
            message.AppendLine("Scopes:");
            foreach (var scope in preview.Scopes) message.AppendLine($"  - {scope.Role}: {scope.PathPrefix}");
            message.AppendLine();
            message.AppendLine("Yes разрешает только временный MCP listener на 127.0.0.1. v0.50 не копирует secret endpoint в clipboard: он хранится только в памяти Workbench и может быть передан официальному OpenAI tunnel-client только после отдельного подтверждения Secure MCP Tunnel.");
            if (MessageBox.Show(this, message.ToString(), "Start read-only MCP adapter — explicit loopback authority", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            SetV035PrimaryControlsEnabled(false);
            BeginRun($"mcp-read-adapter-v0.50-{DateTime.Now:yyyyMMddHHmmss}");
            var grant = await _localAppMcpReadAdapterV049Service.StartAsync(WorkspaceRootBox.Text, appId, preview, grantJson, _cts!.Token);
            started = true;
            _v049ActiveAdapterApplicationId = appId;
            _v050ActiveMcpEndpoint = grant.EndpointUrl;
            _v050ActiveMcpLeaseId = grant.LeaseId;
            _v050ActiveMcpLeaseExpiresAt = grant.LeaseExpiresAt;
            var written = await _localAppMcpReadAdapterV049Service.WriteStartReceiptAsync(WorkspaceRootBox.Text, grant, false, _cts.Token);
            LocalAppsTextBox.Text = CommandCodec.Serialize(new
            {
                Preview = preview,
                StartReceipt = written.Receipt,
                StartReceiptPath = written.ReceiptPath,
                EndpointClipboardWritePerformed = false,
                EndpointSecretHeldInWorkbenchMemoryOnly = true,
                BearerPersistedByAdapter = false,
                SecureMcpTunnelStarted = false,
                NextHumanAction = "Start OpenAI Secure MCP Tunnel only if intentional, provisioned and account-available."
            });
            OutputTabs.SelectedItem = LocalAppsTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: lease-gated MCP adapter ready on loopback for {appId}; endpoint not copied; tunnel=false";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  mcp-adapter.v050.started app={appId}; lease={preview.LeaseId}; loopback=true; endpointClipboard=false; tunnel=false");
        }
        catch (OperationCanceledException) { if (started) { await _localAppMcpReadAdapterV049Service.StopBestEffortAsync(); ClearV050McpRuntimeView(); } ShowCancelled(); }
        catch (InvalidDataException ex) { if (started) { await _localAppMcpReadAdapterV049Service.StopBestEffortAsync(); ClearV050McpRuntimeView(); } ShowInvalid(ex); }
        catch (Exception ex) { if (started) { await _localAppMcpReadAdapterV049Service.StopBestEffortAsync(); ClearV050McpRuntimeView(); } ShowFailure(ex); }
        finally
        {
            grantJson = string.Empty;
            EndRun();
            SetV035PrimaryControlsEnabled(true);
            OperatorSurfaceV045Contract.Apply(this);
        }
    }

    private async Task StopReadOnlyMcpAdapterV050Async(string appId)
    {
        if (_secureMcpTunnelV050Service.IsActiveFor(appId))
        {
            ShowInvalid(new InvalidDataException("Stop the Secure MCP Tunnel before stopping its local MCP adapter."));
            return;
        }
        if (!_localAppMcpReadAdapterV049Service.IsActiveFor(appId))
        {
            MessageBox.Show(this, "Для выбранного приложения нет активного MCP adapter в этом Workbench process.", "Stop read-only MCP adapter", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(this, $"Остановить local read-only MCP adapter для {appId}?\n\nRead lease останется отдельным и может быть затем явно revoked.", "Stop read-only MCP adapter", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            SetV035PrimaryControlsEnabled(false);
            BeginRun($"stop-mcp-read-adapter-v0.50-{DateTime.Now:yyyyMMddHHmmss}");
            var result = await _localAppMcpReadAdapterV049Service.StopAsync(WorkspaceRootBox.Text, _cts!.Token);
            ClearV050McpRuntimeView();
            LocalAppsTextBox.Text = CommandCodec.Serialize(new { Stop = result.Receipt, StopReceiptPath = result.ReceiptPath });
            OutputTabs.SelectedItem = LocalAppsTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: read-only MCP adapter stopped for {appId}";
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally { EndRun(); SetV035PrimaryControlsEnabled(true); OperatorSurfaceV045Contract.Apply(this); }
    }

    private async Task StartSecureMcpTunnelV050Async(string appId)
    {
        if (!_localAppMcpReadAdapterV049Service.IsActiveFor(appId) || string.IsNullOrWhiteSpace(_v050ActiveMcpEndpoint) || _v050ActiveMcpLeaseId is null || _v050ActiveMcpLeaseExpiresAt is null)
        {
            ShowInvalid(new InvalidDataException("Start a fresh v0.48 read lease and the v0.49.1 lease-gated local MCP adapter in this Workbench process first."));
            return;
        }
        var input = OpenAiSecureMcpTunnelDialogV050.ShowInput(this, appId);
        if (input is null) return;
        var runtimeKey = input.RuntimeApiKey;
        try
        {
            var preview = await _secureMcpTunnelV050Service.PreviewAsync(
                WorkspaceRootBox.Text, appId, _v050ActiveMcpLeaseId, _v050ActiveMcpLeaseExpiresAt.Value,
                input.TunnelId, runtimeKey, true, CancellationToken.None);
            var message = new StringBuilder();
            message.AppendLine("Запустить официальный OpenAI Secure MCP Tunnel runtime?");
            message.AppendLine();
            message.AppendLine($"ApplicationId: {preview.ApplicationId}");
            message.AppendLine($"LeaseId: {preview.LeaseId}");
            message.AppendLine($"Lease expires: {preview.LeaseExpiresAt:O}");
            message.AppendLine($"TunnelId: {preview.TunnelId}");
            message.AppendLine($"tunnel-client: {preview.TunnelClientReportedVersion}");
            message.AppendLine($"binary SHA-256: {preview.TunnelClientExecutableSha256}");
            message.AppendLine();
            message.AppendLine("Yes разрешает отдельный исходящий HTTPS tunnel-client process, bounded текущим lease. Runtime API key и secret local MCP URL передаются child process только через environment и не пишутся в receipt. Workbench НЕ создаёт tunnel, НЕ использует Admin key и НЕ меняет ChatGPT settings.");
            if (MessageBox.Show(this, message.ToString(), "Start OpenAI Secure MCP Tunnel — explicit outbound authority", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            SetV035PrimaryControlsEnabled(false);
            BeginRun($"secure-mcp-tunnel-v0.50-{DateTime.Now:yyyyMMddHHmmss}");
            var grant = await _secureMcpTunnelV050Service.StartAsync(WorkspaceRootBox.Text, preview, runtimeKey, _v050ActiveMcpEndpoint, _cts!.Token);
            _v050ActiveTunnelApplicationId = appId;
            var written = await _secureMcpTunnelV050Service.WriteStartReceiptAsync(WorkspaceRootBox.Text, grant, _cts.Token);
            Clipboard.SetText(grant.TunnelId);
            LocalAppsTextBox.Text = CommandCodec.Serialize(new
            {
                Preview = preview,
                StartReceipt = written.Receipt,
                StartReceiptPath = written.ReceiptPath,
                TunnelIdCopiedToClipboard = true,
                RuntimeApiKeyPersisted = false,
                LocalMcpEndpointPersisted = false,
                ChatGptConnectionConfigured = false,
                NextHumanAction = "In ChatGPT developer/app settings choose Connection: Tunnel and select/paste this same TunnelId. This is separate from Workbench tunnel startup."
            });
            OutputTabs.SelectedItem = LocalAppsTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: OpenAI Secure MCP Tunnel runtime ready for {appId}; tunnel id copied; ChatGPT connection separate";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  secure-mcp-tunnel.v050.ready app={appId}; lease={preview.LeaseId}; tunnelId={preview.TunnelId}; outbound=true; publicListener=false; chatgptConfigured=false");
        }
        catch (OperationCanceledException) { await _secureMcpTunnelV050Service.StopBestEffortAsync(); _v050ActiveTunnelApplicationId = null; ShowCancelled(); }
        catch (InvalidDataException ex) { await _secureMcpTunnelV050Service.StopBestEffortAsync(); _v050ActiveTunnelApplicationId = null; ShowInvalid(ex); }
        catch (Exception ex) { await _secureMcpTunnelV050Service.StopBestEffortAsync(); _v050ActiveTunnelApplicationId = null; ShowFailure(ex); }
        finally
        {
            runtimeKey = string.Empty;
            input = null;
            EndRun();
            SetV035PrimaryControlsEnabled(true);
            OperatorSurfaceV045Contract.Apply(this);
        }
    }

    private async Task StopSecureMcpTunnelV050Async(string appId)
    {
        if (!_secureMcpTunnelV050Service.IsActiveFor(appId))
        {
            MessageBox.Show(this, "Для выбранного приложения нет активного Secure MCP Tunnel child process.", "Stop Secure MCP Tunnel", MessageBoxButton.OK, MessageBoxImage.Information);
            _v050ActiveTunnelApplicationId = null;
            return;
        }
        if (MessageBox.Show(this, $"Остановить outbound OpenAI Secure MCP Tunnel для {appId}?\n\nЭто остановит только exact child tunnel-client process. Local MCP adapter и read lease останутся отдельными; после теста останови adapter и revoke lease.", "Stop Secure MCP Tunnel", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            SetV035PrimaryControlsEnabled(false);
            BeginRun($"stop-secure-mcp-tunnel-v0.50-{DateTime.Now:yyyyMMddHHmmss}");
            var result = await _secureMcpTunnelV050Service.StopAsync(WorkspaceRootBox.Text, _cts!.Token);
            _v050ActiveTunnelApplicationId = null;
            LocalAppsTextBox.Text = CommandCodec.Serialize(new { Stop = result.Receipt, StopReceiptPath = result.ReceiptPath, NextExplicitActions = new[] { "Stop read-only MCP adapter", "Revoke active read leases" } });
            OutputTabs.SelectedItem = LocalAppsTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: OpenAI Secure MCP Tunnel child stopped for {appId}; adapter/lease unchanged";
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally { EndRun(); SetV035PrimaryControlsEnabled(true); OperatorSurfaceV045Contract.Apply(this); }
    }

    private void ClearV050McpRuntimeView()
    {
        _v049ActiveAdapterApplicationId = null;
        _v050ActiveMcpEndpoint = null;
        _v050ActiveMcpLeaseId = null;
        _v050ActiveMcpLeaseExpiresAt = null;
    }

    private async void Window_LoadedV050(object sender, RoutedEventArgs e)
    {
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
        if (_v050LoadedBootstrapChecked) return;
        _v050LoadedBootstrapChecked = true;
        TransitionBootstrapV040Claim? claim = null;
        var beganRun = false;
        try
        {
            claim = await _transitionBootstrapV040Service.TryClaimFirstBootAsync(WorkspaceRootBox.Text, LocalCheckpointV050Service.Version, LocalCheckpointV050Service.TargetTag, CancellationToken.None);
            if (claim is null)
            {
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v050 none; automaticValidation=false; automaticAccept=false");
                return;
            }
            SetV035PrimaryControlsEnabled(false);
            OperatorSurfaceV045Contract.Apply(this);
            BeginRun($"first-boot-bootstrap-v0.50-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;
            StatusText.Text = $"RUNNING: v0.50 Secure MCP Tunnel handoff validation; lease={claim.Lease.LeaseId}";
            var tested = await RunV050AcceptanceArtifactAsync(_cts!.Token);
            _lastAcceptanceReceipt = tested.Receipt;
            _lastAcceptanceArtifactPath = tested.ArtifactPath;
            _lastAcceptanceConsumed = false;
            if (!tested.Receipt.Passed)
            {
                await _transitionBootstrapV040Service.MarkFailedNoRetryAsync(claim.Lease, claim.LeasePath, "v0.50 validation returned Passed=false", CancellationToken.None);
                _currentTerminalState = CommandTerminalState.Failed;
                StatusText.Text = "FAILED: v0.50 validation did not pass; automatic local Accept refused; no retry authority";
                AcceptanceTextBox.Text = CommandCodec.Serialize(new { Bootstrap = claim.Lease, Acceptance = tested.Receipt, tested.ArtifactPath, AutomaticAcceptPerformed = false });
                OutputTabs.SelectedItem = AcceptanceTab;
                return;
            }
            var checkpointCandidate = await _checkpointV050Service.PreviewAsync(WorkspaceRootBox.Text, tested.ArtifactPath, tested.Receipt, _cts.Token);
            var checkpoint = await _checkpointV050Service.AcceptFromBootstrapAsync(checkpointCandidate, claim.Lease.LeaseId, _cts.Token);
            var checkpointPath = await LocalCheckpointV050Service.WriteReceiptAsync(WorkspaceRootBox.Text, checkpoint, _cts.Token);
            var completed = await _transitionBootstrapV040Service.FinalizeAcceptedAsync(claim, tested.ArtifactPath, checkpointPath, _cts.Token);
            _lastAcceptanceConsumed = true;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: v0.50 validation PASS + automatic local Accept; {checkpoint.Tag} -> {checkpoint.NewHead}";
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Bootstrap = completed,
                Acceptance = tested.Receipt,
                AcceptanceArtifactPath = tested.ArtifactPath,
                Checkpoint = checkpoint,
                CheckpointReceiptPath = checkpointPath,
                FourButtonSurfacePreserved = true,
                V0491LeaseGatedMcpPreserved = true,
                SecureMcpTunnelIsSeparateExplicitAuthority = true,
                RuntimeCredentialPersistence = false,
                AutomaticTunnelCreation = false,
                AutomaticChatGptConfiguration = false,
                AutomaticPublishPerformed = false,
                AutomaticLifecyclePerformed = false,
                NextExplicitActions = new[] { "Provision verified official tunnel-client runtime if absent", "Real-host lease -> local MCP -> Secure MCP Tunnel -> ChatGPT connector -> read -> stop/revoke", "Publish accepted", "Lifecycle receipt" }
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

    private async Task<(WorkbenchAcceptanceReceipt Receipt, string ArtifactPath)> RunV050AcceptanceArtifactAsync(CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(this);
        var context = new RuntimeContext(CatalogRootBox.Text, true, false);
        var receipt = await new WorkbenchV050AcceptanceHarness(_acceptanceHarness, this).RunAsync(context, cancellationToken);
        var dir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"v0.50-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, CommandCodec.Serialize(receipt), new UTF8Encoding(false), cancellationToken);
        return (receipt, path);
    }

    private async void PublishAcceptedV050Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            OperatorSurfaceV045Contract.Apply(this);
            SaveSettings();
            if (_v050ActiveTunnelApplicationId is not null && _secureMcpTunnelV050Service.IsActiveFor(_v050ActiveTunnelApplicationId)) throw new InvalidDataException("Stop the active Secure MCP Tunnel before publishing accepted Workbench source.");
            if (_v049ActiveAdapterApplicationId is not null) throw new InvalidDataException("Stop the active MCP adapter before publishing accepted Workbench source.");
            var candidate = await _fixedGitHubPublicationV050Service.PreviewAsync(WorkspaceRootBox.Text, CancellationToken.None);
            var preview = $"Опубликовать принятый Workbench v0.50?\n\nRemote: {candidate.RemoteName}\nAccepted HEAD: {candidate.Head}\nParent: {candidate.Parent} / {FixedGitHubPublicationV050Service.ExpectedParentTag}\nTarget tag: {candidate.AcceptedTag}\n\nYes только после успешной real-host цепочки Secure MCP Tunnel + ChatGPT read + Stop/adapter stop/revoke. External tunnel-client, runtime key, private app bytes and session state не публикуются.";
            if (MessageBox.Show(this, preview, "Publish accepted v0.50", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            SetV035PrimaryControlsEnabled(false);
            OperatorSurfaceV045Contract.Apply(this);
            BeginRun($"publish-v0.50-{DateTime.Now:yyyyMMddHHmmss}");
            var receipt = await _fixedGitHubPublicationV050Service.PublishAsync(candidate, _cts!.Token);
            var path = await FixedGitHubPublicationV050Service.WriteReceiptAsync(WorkspaceRootBox.Text, receipt, _cts.Token);
            AcceptanceTextBox.Text = CommandCodec.Serialize(new { Publication = receipt, PublicationReceiptPath = path, ExternalTunnelClientPublished = false, RuntimeCredentialPublished = false, PrivateAppBytesPublished = false, NextExplicitAction = "Lifecycle receipt" });
            OutputTabs.SelectedItem = AcceptanceTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: remote main/v0.50 tag == {receipt.LocalHead}; tunnel runtime/credentials remain local";
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally { EndRun(); SetV035PrimaryControlsEnabled(true); OperatorSurfaceV045Contract.Apply(this); RefreshInstalledAppsV044(); }
    }
}
