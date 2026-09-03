using System.IO;
using System.Text;
using System.Windows;
using Matawaka.Workbench.Protocol;
using Matawaka.Workbench.Runtime;
using Microsoft.Win32;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalCheckpointV0501Service _checkpointV0501Service = new();
    private readonly FixedGitHubPublicationV0501Service _fixedGitHubPublicationV0501Service = new();
    private readonly OpenAiSecureMcpTunnelV0501Service _secureMcpTunnelV0501Service = new();
    private bool _v0501LoadedBootstrapChecked;

    internal void ConfigureV0501Routing()
    {
        ConfigureV050Routing();
        Title = "Matawaka Workbench v0.50.1";
        Loaded -= Window_LoadedV050;
        Loaded += Window_LoadedV0501;
        PublishAcceptedButton.Click -= PublishAcceptedV050Button_Click;
        PublishAcceptedButton.Click += PublishAcceptedV0501Button_Click;
        UpdateLocalAppButton.Click -= LocalAppsV050Button_Click;
        UpdateLocalAppButton.Click += LocalAppsV0501Button_Click;
        Closing -= WindowV050_Closing;
        Closing += WindowV0501_Closing;
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
    }

    private void WindowV0501_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            Task.Run(async () =>
            {
                await _secureMcpTunnelV0501Service.StopBestEffortAsync();
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

    private async void LocalAppsV0501Button_Click(object sender, RoutedEventArgs e)
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
            case LocalAppsActionChoiceV050.StartSecureMcpTunnel: await StartSecureMcpTunnelV0501Async(appId); break;
            case LocalAppsActionChoiceV050.StopSecureMcpTunnel: await StopSecureMcpTunnelV0501Async(appId); break;
            case LocalAppsActionChoiceV050.Cancel:
            default: EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  local-app.v0501.choice.cancelled app={appId}; effect=false"); break;
        }
        RefreshInstalledAppsV044();
    }

    private async Task StartSecureMcpTunnelV0501Async(string appId)
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
            var preview = await _secureMcpTunnelV0501Service.PreviewAsync(
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
            message.AppendLine("v0.50.1 наблюдает /healthz и /readyz до 90 секунд, но никогда не дольше текущего read lease. Неуспешный /readyz остаётся отказом; его bounded/redacted причина сохраняется только в локальном failure receipt. Runtime API key и secret local MCP URL не сохраняются.");
            message.AppendLine();
            message.AppendLine("Yes разрешает отдельный исходящий HTTPS tunnel-client process. Workbench НЕ создаёт tunnel, НЕ использует Admin key и НЕ меняет ChatGPT settings.");
            if (MessageBox.Show(this, message.ToString(), "Start OpenAI Secure MCP Tunnel v0.50.1 — explicit outbound authority", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            SetV035PrimaryControlsEnabled(false);
            BeginRun($"secure-mcp-tunnel-v0.50.1-{DateTime.Now:yyyyMMddHHmmss}");
            var grant = await _secureMcpTunnelV0501Service.StartAsync(WorkspaceRootBox.Text, preview, runtimeKey, _v050ActiveMcpEndpoint, _cts!.Token);
            _v050ActiveTunnelApplicationId = appId;
            var written = await _secureMcpTunnelV0501Service.WriteStartReceiptAsync(WorkspaceRootBox.Text, grant, _cts.Token);
            Clipboard.SetText(grant.TunnelId);
            LocalAppsTextBox.Text = CommandCodec.Serialize(new
            {
                Preview = preview,
                StartReceipt = written.Receipt,
                StartReceiptPath = written.ReceiptPath,
                ReadinessWindowSeconds = 90,
                TunnelIdCopiedToClipboard = true,
                RuntimeApiKeyPersisted = false,
                LocalMcpEndpointPersisted = false,
                ChatGptConnectionConfigured = false,
                NextHumanAction = "In ChatGPT developer/app settings choose Connection: Tunnel and select/paste this same TunnelId. This remains separate from Workbench tunnel startup."
            });
            OutputTabs.SelectedItem = LocalAppsTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: OpenAI Secure MCP Tunnel /readyz ready for {appId}; tunnel id copied; ChatGPT connection separate";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  secure-mcp-tunnel.v0501.ready app={appId}; lease={preview.LeaseId}; tunnelId={preview.TunnelId}; readyz=true; outbound=true; publicListener=false; chatgptConfigured=false");
        }
        catch (OperationCanceledException) { await _secureMcpTunnelV0501Service.StopBestEffortAsync(); _v050ActiveTunnelApplicationId = null; ShowCancelled(); }
        catch (InvalidDataException ex) { await _secureMcpTunnelV0501Service.StopBestEffortAsync(); _v050ActiveTunnelApplicationId = null; ShowInvalid(ex); }
        catch (Exception ex) { await _secureMcpTunnelV0501Service.StopBestEffortAsync(); _v050ActiveTunnelApplicationId = null; ShowFailure(ex); }
        finally
        {
            runtimeKey = string.Empty;
            input = null;
            EndRun();
            SetV035PrimaryControlsEnabled(true);
            OperatorSurfaceV045Contract.Apply(this);
        }
    }

    private async Task StopSecureMcpTunnelV0501Async(string appId)
    {
        if (!_secureMcpTunnelV0501Service.IsActiveFor(appId))
        {
            MessageBox.Show(this, "Для выбранного приложения нет активного Secure MCP Tunnel child process.", "Stop Secure MCP Tunnel", MessageBoxButton.OK, MessageBoxImage.Information);
            _v050ActiveTunnelApplicationId = null;
            return;
        }
        if (MessageBox.Show(this, $"Остановить outbound OpenAI Secure MCP Tunnel для {appId}?\n\nЭто остановит только exact child tunnel-client process. Local MCP adapter и read lease останутся отдельными; после теста останови adapter и revoke lease.", "Stop Secure MCP Tunnel", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            SetV035PrimaryControlsEnabled(false);
            BeginRun($"stop-secure-mcp-tunnel-v0.50.1-{DateTime.Now:yyyyMMddHHmmss}");
            var result = await _secureMcpTunnelV0501Service.StopAsync(WorkspaceRootBox.Text, _cts!.Token);
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

    private async void Window_LoadedV0501(object sender, RoutedEventArgs e)
    {
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
        if (_v0501LoadedBootstrapChecked) return;
        _v0501LoadedBootstrapChecked = true;
        TransitionBootstrapV040Claim? claim = null;
        var beganRun = false;
        try
        {
            claim = await _transitionBootstrapV040Service.TryClaimFirstBootAsync(WorkspaceRootBox.Text, LocalCheckpointV0501Service.Version, LocalCheckpointV0501Service.TargetTag, CancellationToken.None);
            if (claim is null)
            {
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v0501 none; automaticValidation=false; automaticAccept=false");
                return;
            }
            SetV035PrimaryControlsEnabled(false);
            OperatorSurfaceV045Contract.Apply(this);
            BeginRun($"first-boot-bootstrap-v0.50.1-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;
            StatusText.Text = $"RUNNING: v0.50.1 Secure MCP Tunnel readiness diagnostics validation; lease={claim.Lease.LeaseId}";
            var tested = await RunV0501AcceptanceArtifactAsync(_cts!.Token);
            _lastAcceptanceReceipt = tested.Receipt;
            _lastAcceptanceArtifactPath = tested.ArtifactPath;
            _lastAcceptanceConsumed = false;
            if (!tested.Receipt.Passed)
            {
                await _transitionBootstrapV040Service.MarkFailedNoRetryAsync(claim.Lease, claim.LeasePath, "v0.50.1 validation returned Passed=false", CancellationToken.None);
                _currentTerminalState = CommandTerminalState.Failed;
                StatusText.Text = "FAILED: v0.50.1 validation did not pass; automatic local Accept refused; no retry authority";
                AcceptanceTextBox.Text = CommandCodec.Serialize(new { Bootstrap = claim.Lease, Acceptance = tested.Receipt, tested.ArtifactPath, AutomaticAcceptPerformed = false });
                OutputTabs.SelectedItem = AcceptanceTab;
                return;
            }
            var checkpointCandidate = await _checkpointV0501Service.PreviewAsync(WorkspaceRootBox.Text, tested.ArtifactPath, tested.Receipt, _cts.Token);
            var checkpoint = await _checkpointV0501Service.AcceptFromBootstrapAsync(checkpointCandidate, claim.Lease.LeaseId, _cts.Token);
            var checkpointPath = await LocalCheckpointV0501Service.WriteReceiptAsync(WorkspaceRootBox.Text, checkpoint, _cts.Token);
            var completed = await _transitionBootstrapV040Service.FinalizeAcceptedAsync(claim, tested.ArtifactPath, checkpointPath, _cts.Token);
            _lastAcceptanceConsumed = true;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: v0.50.1 validation PASS + automatic local Accept; {checkpoint.Tag} -> {checkpoint.NewHead}";
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Bootstrap = completed,
                Acceptance = tested.Receipt,
                AcceptanceArtifactPath = tested.ArtifactPath,
                Checkpoint = checkpoint,
                CheckpointReceiptPath = checkpointPath,
                FourButtonSurfacePreserved = true,
                FailedV050LocalPredecessorPreserved = true,
                FailedV050RemoteTagMustRemainAbsent = true,
                ReadinessDiagnosticsBoundedAndRedacted = true,
                ReadinessWaitNeverBeyondLease = true,
                AutomaticTunnelCreation = false,
                AutomaticChatGptConfiguration = false,
                AutomaticPublishPerformed = false,
                AutomaticLifecyclePerformed = false,
                NextExplicitActions = new[] { "Fresh read lease", "Start local MCP", "Start Secure MCP Tunnel and inspect exact readiness result", "If ready: ChatGPT read round-trip -> Tunnel Stop -> MCP Stop -> Lease Revoke", "Publish accepted", "Lifecycle receipt" }
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

    private async Task<(WorkbenchAcceptanceReceipt Receipt, string ArtifactPath)> RunV0501AcceptanceArtifactAsync(CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(this);
        var context = new RuntimeContext(CatalogRootBox.Text, true, false);
        var receipt = await new WorkbenchV0501AcceptanceHarness(_acceptanceHarness, this).RunAsync(context, cancellationToken);
        var dir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"v0.50.1-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, CommandCodec.Serialize(receipt), new UTF8Encoding(false), cancellationToken);
        return (receipt, path);
    }

    private async void PublishAcceptedV0501Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            OperatorSurfaceV045Contract.Apply(this);
            SaveSettings();
            if (_v050ActiveTunnelApplicationId is not null && _secureMcpTunnelV0501Service.IsActiveFor(_v050ActiveTunnelApplicationId))
                throw new InvalidDataException("Stop the active Secure MCP Tunnel before publishing accepted Workbench source.");
            if (_v049ActiveAdapterApplicationId is not null)
                throw new InvalidDataException("Stop the active MCP adapter before publishing accepted Workbench source.");
            var candidate = await _fixedGitHubPublicationV0501Service.PreviewAsync(WorkspaceRootBox.Text, CancellationToken.None);
            var preview = $"Опубликовать принятый Workbench v0.50.1?\n\nRemote: {candidate.RemoteName}\nAccepted HEAD: {candidate.Head}\nLocal failed-v0.50 parent: {candidate.Parent} / {FixedGitHubPublicationV0501Service.ExpectedParentTag}\nRemote base must remain: {FixedGitHubPublicationV0501Service.ExpectedRemoteBase} / {FixedGitHubPublicationV0501Service.ExpectedRemoteBaseTag}\nTarget tag: {candidate.AcceptedTag}\n\nYes только после успешной real-host цепочки /readyz + ChatGPT read + Tunnel Stop + MCP Stop + Lease Revoke. Failed workbench-v0.50-accepted tag не публикуется.";
            if (MessageBox.Show(this, preview, "Publish accepted v0.50.1", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            SetV035PrimaryControlsEnabled(false);
            OperatorSurfaceV045Contract.Apply(this);
            BeginRun($"publish-v0.50.1-{DateTime.Now:yyyyMMddHHmmss}");
            var receipt = await _fixedGitHubPublicationV0501Service.PublishAsync(candidate, _cts!.Token);
            var path = await FixedGitHubPublicationV0501Service.WriteReceiptAsync(WorkspaceRootBox.Text, receipt, _cts.Token);
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Publication = receipt,
                PublicationReceiptPath = path,
                FailedV050TagPublished = false,
                ExternalTunnelClientPublished = false,
                RuntimeCredentialPublished = false,
                PrivateAppBytesPublished = false,
                NextExplicitAction = "Lifecycle receipt"
            });
            OutputTabs.SelectedItem = AcceptanceTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: remote main/v0.50.1 tag == {receipt.LocalHead}; failed v0.50 tag remains absent";
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally { EndRun(); SetV035PrimaryControlsEnabled(true); OperatorSurfaceV045Contract.Apply(this); RefreshInstalledAppsV044(); }
    }
}
