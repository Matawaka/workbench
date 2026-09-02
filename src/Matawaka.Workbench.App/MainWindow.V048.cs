using System.IO;
using System.Text;
using System.Windows;
using Matawaka.Workbench.Protocol;
using Matawaka.Workbench.Runtime;
using Microsoft.Win32;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalCheckpointV048Service _checkpointV048Service = new();
    private readonly FixedGitHubPublicationV048Service _fixedGitHubPublicationV048Service = new();
    private readonly LocalAppReadLeaseV048Service _localAppReadLeaseV048Service = new();
    private bool _v048LoadedBootstrapChecked;

    internal void ConfigureV048Routing()
    {
        ConfigureV047Routing();
        Title = "Matawaka Workbench v0.48";

        Loaded -= Window_LoadedV047;
        Loaded += Window_LoadedV048;
        PublishAcceptedButton.Click -= PublishAcceptedV047Button_Click;
        PublishAcceptedButton.Click += PublishAcceptedV048Button_Click;
        UpdateLocalAppButton.Click -= LocalAppsV047Button_Click;
        UpdateLocalAppButton.Click += LocalAppsV048Button_Click;

        Activated -= WindowV047_Activated;
        Activated += WindowV048_Activated;
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
    }

    private void WindowV048_Activated(object? sender, EventArgs e)
    {
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
    }

    private async void LocalAppsV048Button_Click(object sender, RoutedEventArgs e)
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
        var choice = LocalAppsActionDialogV048.ShowChoice(this, appId);
        switch (choice)
        {
            case LocalAppsActionChoiceV048.UpdateFromPackage:
                await UpdateSelectedLocalAppAsync(selectedRoot);
                break;
            case LocalAppsActionChoiceV048.BuildUpdatePackage:
                await BuildLocalAppPackageV038Async(selectedRoot);
                break;
            case LocalAppsActionChoiceV048.LaunchApp:
                await LaunchSelectedLocalAppV046Async(appId, selectedRoot);
                break;
            case LocalAppsActionChoiceV048.ExportUpdateContext:
                await ExportUpdateContextV046Async(appId);
                break;
            case LocalAppsActionChoiceV048.BindDevelopmentSource:
                await BindDevelopmentSourceV046Async(appId);
                break;
            case LocalAppsActionChoiceV048.ExportPrivateDevelopmentContext:
                await ExportPrivateDevelopmentContextV046Async(appId);
                break;
            case LocalAppsActionChoiceV048.ChatReadRelay:
                await ChatReadRelayV047Async(appId);
                break;
            case LocalAppsActionChoiceV048.ReadSessionLease:
                await CreateReadSessionLeaseV048Async(appId);
                break;
            case LocalAppsActionChoiceV048.RevokeReadLeases:
                await RevokeReadSessionLeasesV048Async(appId);
                break;
            case LocalAppsActionChoiceV048.Cancel:
            default:
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  local-app.v048.choice.cancelled app={appId}; effect=false");
                break;
        }
        RefreshInstalledAppsV044();
    }

    private async Task CreateReadSessionLeaseV048Async(string appId)
    {
        var requestJson = LocalAppReadLeaseRequestDialogV048.ShowRequest(this, appId);
        if (requestJson is null)
        {
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  read-lease.v048.request.cancelled app={appId}; lease=false");
            return;
        }

        try
        {
            var preview = _localAppReadLeaseV048Service.PreviewFromJson(WorkspaceRootBox.Text, appId, requestJson, CancellationToken.None);
            var message = new StringBuilder();
            message.AppendLine("Создать short-lived bounded read session lease?");
            message.AppendLine();
            message.AppendLine($"RequestId: {preview.RequestId}");
            message.AppendLine($"ApplicationId: {preview.ApplicationId}");
            message.AppendLine($"Expires after: {preview.TtlSeconds} seconds");
            message.AppendLine($"Max bytes/read: {preview.MaxBytesPerRead:N0}");
            message.AppendLine($"Max total bytes: {preview.MaxTotalBytes:N0}");
            message.AppendLine($"Max calls: {preview.MaxCalls}");
            message.AppendLine("Scopes:");
            foreach (var scope in preview.Scopes) message.AppendLine($"  - {scope.Role}: {scope.PathPrefix}");
            message.AppendLine();
            message.AppendLine("Preview содержимого не читает. Yes создаст только локальный lease state, 256-bit bearer и его hash-only persistence. Grant JSON с plaintext bearer будет показан и помещён в Windows clipboard. v0.48 НЕ запускает HTTP/MCP/tunnel и ничего не загружает в сеть.");
            if (MessageBox.Show(this, message.ToString(), "Read session lease — explicit authority", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  read-lease.v048.creation.refused app={appId}; lease=false");
                return;
            }

            SetV035PrimaryControlsEnabled(false);
            BeginRun($"read-lease-v0.48-{DateTime.Now:yyyyMMddHHmmss}");
            var created = await _localAppReadLeaseV048Service.CreateAsync(WorkspaceRootBox.Text, appId, preview, false, _cts!.Token);
            var grantJson = LocalAppReadLeaseV048Service.SerializeGrant(created.Grant);
            Clipboard.SetText(grantJson);
            LocalAppsTextBox.Text = CommandCodec.Serialize(new
            {
                Preview = preview,
                Grant = created.Grant,
                CreationReceipt = created.Receipt,
                CreationReceiptPath = created.ReceiptPath,
                ClipboardContainsExactGrantJson = true,
                BearerPersistence = "SHA-256 only; plaintext is present only in this returned grant/clipboard",
                NetworkTransportImplemented = false,
                NextHumanAction = "Keep the grant only for a chosen bounded adapter/session; revoke it when done."
            });
            OutputTabs.SelectedItem = LocalAppsTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: read lease {created.Grant.LeaseId}; expires={created.Grant.ExpiresAt:HH:mm:ss}; network=false";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  read-lease.v048.created app={appId}; lease={created.Grant.LeaseId}; scopes={created.Grant.Scopes.Count}; ttl={(int)(created.Grant.ExpiresAt-created.Grant.IssuedAt).TotalSeconds}; network=false");
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

    private async Task RevokeReadSessionLeasesV048Async(string appId)
    {
        try
        {
            var active = _localAppReadLeaseV048Service.ListActive(WorkspaceRootBox.Text, appId);
            if (active.Count == 0)
            {
                MessageBox.Show(this, "Для выбранного приложения нет активных bounded read leases.", "Revoke read leases", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var message = new StringBuilder();
            message.AppendLine($"Отозвать ВСЕ активные read leases для {appId}?");
            message.AppendLine();
            foreach (var lease in active)
                message.AppendLine($"- {lease.LeaseId} | expires {lease.ExpiresAt:HH:mm:ss} | calls {lease.RemainingCalls}/{lease.MaxCalls} | bytes {lease.RemainingBytes:N0}");
            message.AppendLine();
            message.AppendLine("Revocation изменит только локальное ignored lease state. Это не удаляет файлы приложения и не создаёт сеть/выполнение.");
            if (MessageBox.Show(this, message.ToString(), "Revoke active read leases", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            SetV035PrimaryControlsEnabled(false);
            BeginRun($"revoke-read-leases-v0.48-{DateTime.Now:yyyyMMddHHmmss}");
            var result = await _localAppReadLeaseV048Service.RevokeAllActiveAsync(WorkspaceRootBox.Text, appId, _cts!.Token);
            LocalAppsTextBox.Text = CommandCodec.Serialize(new { Revoke = result.Receipt, RevokeReceiptPath = result.ReceiptPath });
            OutputTabs.SelectedItem = LocalAppsTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: revoked {result.Receipt.RevokedLeases} read lease(s) for {appId}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  read-lease.v048.revoked app={appId}; count={result.Receipt.RevokedLeases}; network=false");
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

    private async void Window_LoadedV048(object sender, RoutedEventArgs e)
    {
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
        if (_v048LoadedBootstrapChecked) return;
        _v048LoadedBootstrapChecked = true;
        TransitionBootstrapV040Claim? claim = null;
        var beganRun = false;
        try
        {
            claim = await _transitionBootstrapV040Service.TryClaimFirstBootAsync(WorkspaceRootBox.Text, LocalCheckpointV048Service.Version, LocalCheckpointV048Service.TargetTag, CancellationToken.None);
            if (claim is null)
            {
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v048 none; automaticValidation=false; automaticAccept=false");
                return;
            }
            SetV035PrimaryControlsEnabled(false);
            OperatorSurfaceV045Contract.Apply(this);
            BeginRun($"first-boot-bootstrap-v0.48-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;
            StatusText.Text = $"RUNNING: v0.48 bounded-read-lease validation; lease={claim.Lease.LeaseId}";
            var tested = await RunV048AcceptanceArtifactAsync(_cts!.Token);
            _lastAcceptanceReceipt = tested.Receipt;
            _lastAcceptanceArtifactPath = tested.ArtifactPath;
            _lastAcceptanceConsumed = false;
            if (!tested.Receipt.Passed)
            {
                await _transitionBootstrapV040Service.MarkFailedNoRetryAsync(claim.Lease, claim.LeasePath, "v0.48 validation returned Passed=false", CancellationToken.None);
                _currentTerminalState = CommandTerminalState.Failed;
                StatusText.Text = "FAILED: v0.48 validation did not pass; automatic local Accept refused; no retry authority";
                AcceptanceTextBox.Text = CommandCodec.Serialize(new { Bootstrap = claim.Lease, Acceptance = tested.Receipt, tested.ArtifactPath, AutomaticAcceptPerformed = false });
                OutputTabs.SelectedItem = AcceptanceTab;
                return;
            }
            var checkpointCandidate = await _checkpointV048Service.PreviewAsync(WorkspaceRootBox.Text, tested.ArtifactPath, tested.Receipt, _cts.Token);
            var checkpoint = await _checkpointV048Service.AcceptFromBootstrapAsync(checkpointCandidate, claim.Lease.LeaseId, _cts.Token);
            var checkpointPath = await LocalCheckpointV048Service.WriteReceiptAsync(WorkspaceRootBox.Text, checkpoint, _cts.Token);
            var completed = await _transitionBootstrapV040Service.FinalizeAcceptedAsync(claim, tested.ArtifactPath, checkpointPath, _cts.Token);
            _lastAcceptanceConsumed = true;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: v0.48 validation PASS + automatic local Accept; {checkpoint.Tag} -> {checkpoint.NewHead}";
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Bootstrap = completed,
                Acceptance = tested.Receipt,
                AcceptanceArtifactPath = tested.ArtifactPath,
                Checkpoint = checkpoint,
                CheckpointReceiptPath = checkpointPath,
                FourButtonSurfacePreserved = true,
                V047ChatReadRelayPreserved = true,
                ReadLeaseHumanGated = true,
                BearerHashOnlyPersistence = true,
                LeaseTtlCallsBytesBounded = true,
                NetworkTransportImplemented = false,
                AutomaticPublishPerformed = false,
                AutomaticLifecyclePerformed = false,
                NextExplicitActions = new[] { "Real-host read lease create/revoke check", "Publish accepted", "Lifecycle receipt" }
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

    private async Task<(WorkbenchAcceptanceReceipt Receipt, string ArtifactPath)> RunV048AcceptanceArtifactAsync(CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(this);
        var context = new RuntimeContext(CatalogRootBox.Text, true, false);
        var receipt = await new WorkbenchV048AcceptanceHarness(_acceptanceHarness, this).RunAsync(context, cancellationToken);
        var dir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"v0.48-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, CommandCodec.Serialize(receipt), new UTF8Encoding(false), cancellationToken);
        return (receipt, path);
    }

    private async void PublishAcceptedV048Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            OperatorSurfaceV045Contract.Apply(this);
            SaveSettings();
            var candidate = await _fixedGitHubPublicationV048Service.PreviewAsync(WorkspaceRootBox.Text, CancellationToken.None);
            var preview = $"Опубликовать принятый Workbench v0.48?\n\nRemote: {candidate.RemoteName}\nAccepted HEAD: {candidate.Head}\nParent: {candidate.Parent} / {FixedGitHubPublicationV048Service.ExpectedParentTag}\nTag: {candidate.AcceptedTag}\n\nYes только после real-host проверки lease create/revoke. Lease state/bearers/private Apps/AppSources bytes не входят в Workbench checkpoint/publication.";
            if (MessageBox.Show(this, preview, "Publish accepted v0.48", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            SetV035PrimaryControlsEnabled(false);
            OperatorSurfaceV045Contract.Apply(this);
            BeginRun($"publish-v0.48-{DateTime.Now:yyyyMMddHHmmss}");
            var receipt = await _fixedGitHubPublicationV048Service.PublishAsync(candidate, _cts!.Token);
            var path = await FixedGitHubPublicationV048Service.WriteReceiptAsync(WorkspaceRootBox.Text, receipt, _cts.Token);
            AcceptanceTextBox.Text = CommandCodec.Serialize(new { Publication = receipt, PublicationReceiptPath = path, LeaseStatePublished = false, PrivateAppBytesPublished = false, NextExplicitAction = "Lifecycle receipt" });
            OutputTabs.SelectedItem = AcceptanceTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: remote main/v0.48 tag == {receipt.LocalHead}";
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally { EndRun(); SetV035PrimaryControlsEnabled(true); OperatorSurfaceV045Contract.Apply(this); RefreshInstalledAppsV044(); }
    }
}
