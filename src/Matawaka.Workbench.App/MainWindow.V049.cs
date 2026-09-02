using System.IO;
using System.Text;
using System.Windows;
using Matawaka.Workbench.Protocol;
using Matawaka.Workbench.Runtime;
using Microsoft.Win32;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalCheckpointV049Service _checkpointV049Service = new();
    private readonly FixedGitHubPublicationV049Service _fixedGitHubPublicationV049Service = new();
    private readonly LocalAppMcpReadAdapterV049Service _localAppMcpReadAdapterV049Service = new();
    private bool _v049LoadedBootstrapChecked;

    internal void ConfigureV049Routing()
    {
        ConfigureV048Routing();
        Title = "Matawaka Workbench v0.49";

        Loaded -= Window_LoadedV048;
        Loaded += Window_LoadedV049;
        PublishAcceptedButton.Click -= PublishAcceptedV048Button_Click;
        PublishAcceptedButton.Click += PublishAcceptedV049Button_Click;
        UpdateLocalAppButton.Click -= LocalAppsV048Button_Click;
        UpdateLocalAppButton.Click += LocalAppsV049Button_Click;
        Activated -= WindowV048_Activated;
        Activated += WindowV049_Activated;
        Closing += WindowV049_Closing;

        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
    }

    private void WindowV049_Activated(object? sender, EventArgs e)
    {
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
    }

    private void WindowV049_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            Task.Run(() => _localAppMcpReadAdapterV049Service.StopBestEffortAsync()).GetAwaiter().GetResult();
        }
        catch
        {
            // Closing must not create retry/authority semantics. Best-effort adapter shutdown only.
        }
    }

    private async void LocalAppsV049Button_Click(object sender, RoutedEventArgs e)
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
                await _localApplicationManagedRoleGuardV0371Service.EnsureRegistrationRoleAllowedAsync(selectedRoot, WorkspaceRootBox.Text, CancellationToken.None);
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
        var choice = LocalAppsActionDialogV049.ShowChoice(this, appId, _localAppMcpReadAdapterV049Service.IsActiveFor(appId));
        switch (choice)
        {
            case LocalAppsActionChoiceV049.UpdateFromPackage:
                await UpdateSelectedLocalAppAsync(selectedRoot);
                break;
            case LocalAppsActionChoiceV049.BuildUpdatePackage:
                await BuildLocalAppPackageV038Async(selectedRoot);
                break;
            case LocalAppsActionChoiceV049.LaunchApp:
                await LaunchSelectedLocalAppV046Async(appId, selectedRoot);
                break;
            case LocalAppsActionChoiceV049.ExportUpdateContext:
                await ExportUpdateContextV046Async(appId);
                break;
            case LocalAppsActionChoiceV049.BindDevelopmentSource:
                await BindDevelopmentSourceV046Async(appId);
                break;
            case LocalAppsActionChoiceV049.ExportPrivateDevelopmentContext:
                await ExportPrivateDevelopmentContextV046Async(appId);
                break;
            case LocalAppsActionChoiceV049.ChatReadRelay:
                await ChatReadRelayV047Async(appId);
                break;
            case LocalAppsActionChoiceV049.ReadSessionLease:
                await CreateReadSessionLeaseV048Async(appId);
                break;
            case LocalAppsActionChoiceV049.RevokeReadLeases:
                await RevokeReadSessionLeasesV048Async(appId);
                break;
            case LocalAppsActionChoiceV049.StartReadOnlyMcpAdapter:
                await StartReadOnlyMcpAdapterV049Async(appId);
                break;
            case LocalAppsActionChoiceV049.StopReadOnlyMcpAdapter:
                await StopReadOnlyMcpAdapterV049Async(appId);
                break;
            case LocalAppsActionChoiceV049.Cancel:
            default:
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  local-app.v049.choice.cancelled app={appId}; effect=false");
                break;
        }
        RefreshInstalledAppsV044();
    }

    private async Task StartReadOnlyMcpAdapterV049Async(string appId)
    {
        var grantJson = LocalAppMcpAdapterGrantDialogV049.ShowGrant(this, appId);
        if (grantJson is null)
        {
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  mcp-adapter.v049.start.cancelled app={appId}; listener=false");
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
            message.AppendLine($"Max bytes/read: {preview.MaxBytesPerRead:N0}");
            message.AppendLine("Scopes:");
            foreach (var scope in preview.Scopes) message.AppendLine($"  - {scope.Role}: {scope.PathPrefix}");
            message.AppendLine();
            message.AppendLine("Yes разрешает только временный HTTP MCP listener на 127.0.0.1 с random port + random secret path. MCP tool не принимает ApplicationId, LeaseId или bearer и каждое чтение всё равно проходит через v0.48 lease. Workbench НЕ создаёт Secure MCP Tunnel и не публикует endpoint в интернет.");
            if (MessageBox.Show(this, message.ToString(), "Start read-only MCP adapter — explicit loopback authority", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  mcp-adapter.v049.start.refused app={appId}; listener=false");
                return;
            }

            SetV035PrimaryControlsEnabled(false);
            BeginRun($"mcp-read-adapter-v0.49-{DateTime.Now:yyyyMMddHHmmss}");
            var grant = await _localAppMcpReadAdapterV049Service.StartAsync(WorkspaceRootBox.Text, appId, preview, grantJson, _cts!.Token);
            started = true;
            Clipboard.SetText(grant.EndpointUrl);
            var written = await _localAppMcpReadAdapterV049Service.WriteStartReceiptAsync(WorkspaceRootBox.Text, grant, true, _cts.Token);
            LocalAppsTextBox.Text = CommandCodec.Serialize(new
            {
                Preview = preview,
                Adapter = grant,
                StartReceipt = written.Receipt,
                StartReceiptPath = written.ReceiptPath,
                EndpointClipboardContainsExactLocalUrl = true,
                BearerPersistedByAdapter = false,
                SecureMcpTunnelStarted = false,
                NextHumanAction = "First verify this local endpoint. Configure a supported Secure MCP Tunnel separately only when intentional and account-available."
            });
            OutputTabs.SelectedItem = LocalAppsTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: read-only MCP adapter on loopback for {appId}; endpoint copied; tunnel=false";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  mcp-adapter.v049.started app={appId}; lease={preview.LeaseId}; loopback=true; public=false; tunnel=false");
        }
        catch (OperationCanceledException) { if (started) await _localAppMcpReadAdapterV049Service.StopBestEffortAsync(); ShowCancelled(); }
        catch (InvalidDataException ex) { if (started) await _localAppMcpReadAdapterV049Service.StopBestEffortAsync(); ShowInvalid(ex); }
        catch (Exception ex) { if (started) await _localAppMcpReadAdapterV049Service.StopBestEffortAsync(); ShowFailure(ex); }
        finally
        {
            EndRun();
            SetV035PrimaryControlsEnabled(true);
            OperatorSurfaceV045Contract.Apply(this);
        }
    }

    private async Task StopReadOnlyMcpAdapterV049Async(string appId)
    {
        if (!_localAppMcpReadAdapterV049Service.IsActiveFor(appId))
        {
            MessageBox.Show(this, "Для выбранного приложения нет активного v0.49 MCP adapter в этом Workbench process.", "Stop read-only MCP adapter", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(this, $"Остановить local read-only MCP adapter для {appId}?\n\nЭто остановит только loopback listener и очистит Workbench-held bearer reference. Сам v0.48 lease не продлевается и не расширяется.", "Stop read-only MCP adapter", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            SetV035PrimaryControlsEnabled(false);
            BeginRun($"stop-mcp-read-adapter-v0.49-{DateTime.Now:yyyyMMddHHmmss}");
            var result = await _localAppMcpReadAdapterV049Service.StopAsync(WorkspaceRootBox.Text, _cts!.Token);
            LocalAppsTextBox.Text = CommandCodec.Serialize(new { Stop = result.Receipt, StopReceiptPath = result.ReceiptPath });
            OutputTabs.SelectedItem = LocalAppsTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: read-only MCP adapter stopped for {appId}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  mcp-adapter.v049.stopped app={appId}; listener=false; tunnel=false");
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally
        {
            EndRun();
            SetV035PrimaryControlsEnabled(true);
            OperatorSurfaceV045Contract.Apply(this);
        }
    }

    private async void Window_LoadedV049(object sender, RoutedEventArgs e)
    {
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
        if (_v049LoadedBootstrapChecked) return;
        _v049LoadedBootstrapChecked = true;
        TransitionBootstrapV040Claim? claim = null;
        var beganRun = false;
        try
        {
            claim = await _transitionBootstrapV040Service.TryClaimFirstBootAsync(WorkspaceRootBox.Text, LocalCheckpointV049Service.Version, LocalCheckpointV049Service.TargetTag, CancellationToken.None);
            if (claim is null)
            {
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v049 none; automaticValidation=false; automaticAccept=false");
                return;
            }
            SetV035PrimaryControlsEnabled(false);
            OperatorSurfaceV045Contract.Apply(this);
            BeginRun($"first-boot-bootstrap-v0.49-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;
            StatusText.Text = $"RUNNING: v0.49 lease-gated MCP validation; lease={claim.Lease.LeaseId}";
            var tested = await RunV049AcceptanceArtifactAsync(_cts!.Token);
            _lastAcceptanceReceipt = tested.Receipt;
            _lastAcceptanceArtifactPath = tested.ArtifactPath;
            _lastAcceptanceConsumed = false;
            if (!tested.Receipt.Passed)
            {
                await _transitionBootstrapV040Service.MarkFailedNoRetryAsync(claim.Lease, claim.LeasePath, "v0.49 validation returned Passed=false", CancellationToken.None);
                _currentTerminalState = CommandTerminalState.Failed;
                StatusText.Text = "FAILED: v0.49 validation did not pass; automatic local Accept refused; no retry authority";
                AcceptanceTextBox.Text = CommandCodec.Serialize(new { Bootstrap = claim.Lease, Acceptance = tested.Receipt, tested.ArtifactPath, AutomaticAcceptPerformed = false });
                OutputTabs.SelectedItem = AcceptanceTab;
                return;
            }
            var checkpointCandidate = await _checkpointV049Service.PreviewAsync(WorkspaceRootBox.Text, tested.ArtifactPath, tested.Receipt, _cts.Token);
            var checkpoint = await _checkpointV049Service.AcceptFromBootstrapAsync(checkpointCandidate, claim.Lease.LeaseId, _cts.Token);
            var checkpointPath = await LocalCheckpointV049Service.WriteReceiptAsync(WorkspaceRootBox.Text, checkpoint, _cts.Token);
            var completed = await _transitionBootstrapV040Service.FinalizeAcceptedAsync(claim, tested.ArtifactPath, checkpointPath, _cts.Token);
            _lastAcceptanceConsumed = true;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: v0.49 validation PASS + automatic local Accept; {checkpoint.Tag} -> {checkpoint.NewHead}";
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Bootstrap = completed,
                Acceptance = tested.Receipt,
                AcceptanceArtifactPath = tested.ArtifactPath,
                Checkpoint = checkpoint,
                CheckpointReceiptPath = checkpointPath,
                FourButtonSurfacePreserved = true,
                V048ReadLeasesPreserved = true,
                OfficialMcpSdkPinned = true,
                AdapterLeaseGated = true,
                AdapterLoopbackOnly = true,
                SecureMcpTunnelImplemented = false,
                AutomaticPublishPerformed = false,
                AutomaticLifecyclePerformed = false,
                NextExplicitActions = new[] { "Real-host local MCP adapter check", "Optional product/tunnel activation proof", "Publish accepted", "Lifecycle receipt" }
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

    private async Task<(WorkbenchAcceptanceReceipt Receipt, string ArtifactPath)> RunV049AcceptanceArtifactAsync(CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(this);
        var context = new RuntimeContext(CatalogRootBox.Text, true, false);
        var receipt = await new WorkbenchV049AcceptanceHarness(_acceptanceHarness, this).RunAsync(context, cancellationToken);
        var dir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"v0.49-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, CommandCodec.Serialize(receipt), new UTF8Encoding(false), cancellationToken);
        return (receipt, path);
    }

    private async void PublishAcceptedV049Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            OperatorSurfaceV045Contract.Apply(this);
            SaveSettings();
            if (_localAppMcpReadAdapterV049Service.IsActiveFor("life-situation-resolver"))
                throw new InvalidDataException("Stop the active v0.49 MCP adapter before publishing accepted Workbench source.");
            var candidate = await _fixedGitHubPublicationV049Service.PreviewAsync(WorkspaceRootBox.Text, CancellationToken.None);
            var preview = $"Опубликовать принятый Workbench v0.49?\n\nRemote: {candidate.RemoteName}\nAccepted HEAD: {candidate.Head}\nParent: {candidate.Parent} / {FixedGitHubPublicationV049Service.ExpectedParentTag}\nTag: {candidate.AcceptedTag}\n\nYes только после real-host проверки local loopback MCP adapter. Lease/bearer/private app data/endpoint tokens не входят в Workbench publication. Secure MCP Tunnel не запускается этой кнопкой.";
            if (MessageBox.Show(this, preview, "Publish accepted v0.49", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            SetV035PrimaryControlsEnabled(false);
            OperatorSurfaceV045Contract.Apply(this);
            BeginRun($"publish-v0.49-{DateTime.Now:yyyyMMddHHmmss}");
            var receipt = await _fixedGitHubPublicationV049Service.PublishAsync(candidate, _cts!.Token);
            var path = await FixedGitHubPublicationV049Service.WriteReceiptAsync(WorkspaceRootBox.Text, receipt, _cts.Token);
            AcceptanceTextBox.Text = CommandCodec.Serialize(new { Publication = receipt, PublicationReceiptPath = path, LeaseStatePublished = false, EndpointTokenPublished = false, PrivateAppBytesPublished = false, NextExplicitAction = "Lifecycle receipt" });
            OutputTabs.SelectedItem = AcceptanceTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: remote main/v0.49 tag == {receipt.LocalHead}";
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally { EndRun(); SetV035PrimaryControlsEnabled(true); OperatorSurfaceV045Contract.Apply(this); RefreshInstalledAppsV044(); }
    }
}
