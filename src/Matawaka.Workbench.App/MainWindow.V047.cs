using System.IO;
using System.Text;
using System.Windows;
using Matawaka.Workbench.Protocol;
using Matawaka.Workbench.Runtime;
using Microsoft.Win32;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalCheckpointV047Service _checkpointV047Service = new();
    private readonly FixedGitHubPublicationV047Service _fixedGitHubPublicationV047Service = new();
    private readonly LocalAppChatReadRelayV047Service _localAppChatReadRelayV047Service = new();
    private bool _v047LoadedBootstrapChecked;

    internal void ConfigureV047Routing()
    {
        ConfigureV046Routing();
        Title = "Matawaka Workbench v0.47";

        Loaded -= Window_LoadedV046;
        Loaded += Window_LoadedV047;
        PublishAcceptedButton.Click -= PublishAcceptedV046Button_Click;
        PublishAcceptedButton.Click += PublishAcceptedV047Button_Click;
        UpdateLocalAppButton.Click -= LocalAppsV046Button_Click;
        UpdateLocalAppButton.Click += LocalAppsV047Button_Click;

        Activated -= WindowV046_Activated;
        Activated += WindowV047_Activated;
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
    }

    private void WindowV047_Activated(object? sender, EventArgs e)
    {
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
    }

    private async void LocalAppsV047Button_Click(object sender, RoutedEventArgs e)
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
        var choice = LocalAppsActionDialogV047.ShowChoice(this, appId);
        switch (choice)
        {
            case LocalAppsActionChoiceV047.UpdateFromPackage:
                await UpdateSelectedLocalAppAsync(selectedRoot);
                break;
            case LocalAppsActionChoiceV047.BuildUpdatePackage:
                await BuildLocalAppPackageV038Async(selectedRoot);
                break;
            case LocalAppsActionChoiceV047.LaunchApp:
                await LaunchSelectedLocalAppV046Async(appId, selectedRoot);
                break;
            case LocalAppsActionChoiceV047.ExportUpdateContext:
                await ExportUpdateContextV046Async(appId);
                break;
            case LocalAppsActionChoiceV047.BindDevelopmentSource:
                await BindDevelopmentSourceV046Async(appId);
                break;
            case LocalAppsActionChoiceV047.ExportPrivateDevelopmentContext:
                await ExportPrivateDevelopmentContextV046Async(appId);
                break;
            case LocalAppsActionChoiceV047.ChatReadRelay:
                await ChatReadRelayV047Async(appId);
                break;
            case LocalAppsActionChoiceV047.Cancel:
            default:
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  local-app.v047.choice.cancelled app={appId}; effect=false");
                break;
        }
        RefreshInstalledAppsV044();
    }

    private async Task ChatReadRelayV047Async(string appId)
    {
        var requestJson = LocalAppChatReadRequestDialogV047.ShowRequest(this, appId);
        if (requestJson is null)
        {
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  chat-read.v047.request.cancelled app={appId}; read=false; clipboard=false");
            return;
        }

        try
        {
            var preview = _localAppChatReadRelayV047Service.PreviewFromJson(WorkspaceRootBox.Text, appId, requestJson, CancellationToken.None);
            var message = new StringBuilder();
            message.AppendLine("Разрешить bounded read и раскрытие exact результата в локальный clipboard?");
            message.AppendLine();
            message.AppendLine($"RequestId: {preview.RequestId}");
            message.AppendLine($"ApplicationId: {preview.ApplicationId}");
            message.AppendLine($"Role: {preview.Role}");
            message.AppendLine($"Path: {preview.RelativePath}");
            message.AppendLine($"Whole file SHA-256: {preview.FileSha256}");
            message.AppendLine($"Whole file bytes: {preview.FileBytes}");
            message.AppendLine($"Requested offset/max: {preview.Offset} / {preview.MaxBytes}");
            message.AppendLine($"Planned disclosure bytes: {preview.PlannedReadBytes}");
            message.AppendLine($"Expected hash verified: {preview.ExpectedHashVerified}");
            message.AppendLine();
            message.AppendLine("До этого подтверждения содержимое файла не читалось и в clipboard ничего не записывалось. После Yes Workbench повторно проверит SHA/size/range, прочитает только этот bounded chunk и положит response JSON в Windows clipboard. Автоматической загрузки или сети нет.");
            if (MessageBox.Show(this, message.ToString(), "Chat read relay — explicit disclosure", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  chat-read.v047.disclosure.refused app={appId}; request={preview.RequestId}; read=false; clipboard=false");
                return;
            }

            SetV035PrimaryControlsEnabled(false);
            BeginRun($"chat-read-relay-v0.47-{DateTime.Now:yyyyMMddHHmmss}");
            StatusText.Text = $"RUNNING: bounded chat read {appId}/{preview.Role}/{preview.RelativePath}";
            var read = _localAppChatReadRelayV047Service.PrepareConfirmedRead(WorkspaceRootBox.Text, appId, preview, _cts!.Token);
            var response = _localAppChatReadRelayV047Service.BuildClipboardResponse(preview, read);
            var responseJson = LocalAppChatReadRelayV047Service.SerializeResponse(response);
            Clipboard.SetText(responseJson);
            var written = await _localAppChatReadRelayV047Service.WriteReceiptAsync(WorkspaceRootBox.Text, preview, response, _cts.Token);

            LocalAppsTextBox.Text = CommandCodec.Serialize(new
            {
                Preview = preview,
                Response = response,
                RelayReceipt = written.Receipt,
                RelayReceiptPath = written.ArtifactPath,
                ClipboardContainsExactResponseJson = true,
                NextHumanAction = "Paste the clipboard JSON into the chosen chat."
            });
            OutputTabs.SelectedItem = LocalAppsTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: chat read relay ready in clipboard; {preview.RelativePath}; bytes={response.ReturnedBytes}; upload=false";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  chat-read.v047.completed app={appId}; request={preview.RequestId}; role={preview.Role}; path={preview.RelativePath}; bytes={response.ReturnedBytes}; clipboard=true; network=false");
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

    private async void Window_LoadedV047(object sender, RoutedEventArgs e)
    {
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
        if (_v047LoadedBootstrapChecked) return;
        _v047LoadedBootstrapChecked = true;
        TransitionBootstrapV040Claim? claim = null;
        var beganRun = false;
        try
        {
            claim = await _transitionBootstrapV040Service.TryClaimFirstBootAsync(WorkspaceRootBox.Text, LocalCheckpointV047Service.Version, LocalCheckpointV047Service.TargetTag, CancellationToken.None);
            if (claim is null)
            {
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v047 none; automaticValidation=false; automaticAccept=false");
                return;
            }
            SetV035PrimaryControlsEnabled(false);
            OperatorSurfaceV045Contract.Apply(this);
            BeginRun($"first-boot-bootstrap-v0.47-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;
            StatusText.Text = $"RUNNING: v0.47 bounded-chat-read validation; lease={claim.Lease.LeaseId}";
            var tested = await RunV047AcceptanceArtifactAsync(_cts!.Token);
            _lastAcceptanceReceipt = tested.Receipt;
            _lastAcceptanceArtifactPath = tested.ArtifactPath;
            _lastAcceptanceConsumed = false;
            if (!tested.Receipt.Passed)
            {
                await _transitionBootstrapV040Service.MarkFailedNoRetryAsync(claim.Lease, claim.LeasePath, "v0.47 validation returned Passed=false", CancellationToken.None);
                _currentTerminalState = CommandTerminalState.Failed;
                StatusText.Text = "FAILED: v0.47 validation did not pass; automatic local Accept refused; no retry authority";
                AcceptanceTextBox.Text = CommandCodec.Serialize(new { Bootstrap = claim.Lease, Acceptance = tested.Receipt, tested.ArtifactPath, AutomaticAcceptPerformed = false });
                OutputTabs.SelectedItem = AcceptanceTab;
                return;
            }
            var checkpointCandidate = await _checkpointV047Service.PreviewAsync(WorkspaceRootBox.Text, tested.ArtifactPath, tested.Receipt, _cts.Token);
            var checkpoint = await _checkpointV047Service.AcceptFromBootstrapAsync(checkpointCandidate, claim.Lease.LeaseId, _cts.Token);
            var checkpointPath = await LocalCheckpointV047Service.WriteReceiptAsync(WorkspaceRootBox.Text, checkpoint, _cts.Token);
            var completed = await _transitionBootstrapV040Service.FinalizeAcceptedAsync(claim, tested.ArtifactPath, checkpointPath, _cts.Token);
            _lastAcceptanceConsumed = true;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: v0.47 validation PASS + automatic local Accept; {checkpoint.Tag} -> {checkpoint.NewHead}";
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Bootstrap = completed,
                Acceptance = tested.Receipt,
                AcceptanceArtifactPath = tested.ArtifactPath,
                Checkpoint = checkpoint,
                CheckpointReceiptPath = checkpointPath,
                FourButtonSurfacePreserved = true,
                V046OperationalHandoffPreserved = true,
                ChatReadRelayHumanGated = true,
                ClipboardDisclosureExplicitOnly = true,
                NetworkTransportImplemented = false,
                AutomaticPublishPerformed = false,
                AutomaticLifecyclePerformed = false,
                NextExplicitActions = new[] { "Real-host chat read relay check", "Publish accepted", "Lifecycle receipt" }
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

    private async Task<(WorkbenchAcceptanceReceipt Receipt, string ArtifactPath)> RunV047AcceptanceArtifactAsync(CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(this);
        var context = new RuntimeContext(CatalogRootBox.Text, true, false);
        var receipt = await new WorkbenchV047AcceptanceHarness(_acceptanceHarness, this).RunAsync(context, cancellationToken);
        var dir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"v0.47-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, CommandCodec.Serialize(receipt), new UTF8Encoding(false), cancellationToken);
        return (receipt, path);
    }

    private async void PublishAcceptedV047Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            OperatorSurfaceV045Contract.Apply(this);
            SaveSettings();
            var candidate = await _fixedGitHubPublicationV047Service.PreviewAsync(WorkspaceRootBox.Text, CancellationToken.None);
            var preview = $"Опубликовать принятый Workbench v0.47?\n\nRemote: {candidate.RemoteName}\nAccepted HEAD: {candidate.Head}\nParent: {candidate.Parent} / {FixedGitHubPublicationV047Service.ExpectedParentTag}\nTag: {candidate.AcceptedTag}\n\nYes только после real-host проверки Chat read relay: exact selected app/path/SHA preview, explicit disclosure confirmation, bounded response in clipboard, no automatic upload/network. PRIVATE app/source bytes не публикуются.";
            if (MessageBox.Show(this, preview, "Publish accepted v0.47", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            SetV035PrimaryControlsEnabled(false);
            OperatorSurfaceV045Contract.Apply(this);
            BeginRun($"publish-v0.47-{DateTime.Now:yyyyMMddHHmmss}");
            var receipt = await _fixedGitHubPublicationV047Service.PublishAsync(candidate, _cts!.Token);
            var path = await FixedGitHubPublicationV047Service.WriteReceiptAsync(WorkspaceRootBox.Text, receipt, _cts.Token);
            AcceptanceTextBox.Text = CommandCodec.Serialize(new { Publication = receipt, PublicationReceiptPath = path, PrivateAppBytesPublished = false, ClipboardDataPublished = false, NextExplicitAction = "Lifecycle receipt" });
            OutputTabs.SelectedItem = AcceptanceTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: remote main/v0.47 tag == {receipt.LocalHead}";
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally { EndRun(); SetV035PrimaryControlsEnabled(true); OperatorSurfaceV045Contract.Apply(this); RefreshInstalledAppsV044(); }
    }
}
