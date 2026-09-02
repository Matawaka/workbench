using System.IO;
using System.Text;
using System.Windows;
using Matawaka.Workbench.Protocol;
using Matawaka.Workbench.Runtime;
using Microsoft.Win32;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalCheckpointV046Service _checkpointV046Service = new();
    private readonly FixedGitHubPublicationV046Service _fixedGitHubPublicationV046Service = new();
    private readonly LocalAppLaunchV046Service _localAppLaunchV046Service = new();
    private readonly LocalAppUpdateContextV046Service _localAppUpdateContextV046Service = new();
    private readonly LocalAppSourceBindingV046Service _localAppSourceBindingV046Service = new();
    private readonly LocalAppPrivateContextV046Service _localAppPrivateContextV046Service = new();
    private bool _v046LoadedBootstrapChecked;

    internal void ConfigureV046Routing()
    {
        ConfigureV045Routing();
        Title = "Matawaka Workbench v0.46";

        Loaded -= Window_LoadedV045;
        Loaded += Window_LoadedV046;
        PublishAcceptedButton.Click -= PublishAcceptedV045Button_Click;
        PublishAcceptedButton.Click += PublishAcceptedV046Button_Click;
        UpdateLocalAppButton.Click -= LocalAppsV0381Button_Click;
        UpdateLocalAppButton.Click += LocalAppsV046Button_Click;

        Activated -= WindowV045_Activated;
        Activated += WindowV046_Activated;
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
    }

    private void WindowV046_Activated(object? sender, EventArgs e)
    {
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
    }

    private async void LocalAppsV046Button_Click(object sender, RoutedEventArgs e)
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
        var choice = LocalAppsActionDialogV046.ShowChoice(this, appId);
        switch (choice)
        {
            case LocalAppsActionChoiceV046.UpdateFromPackage:
                await UpdateSelectedLocalAppAsync(selectedRoot);
                break;
            case LocalAppsActionChoiceV046.BuildUpdatePackage:
                await BuildLocalAppPackageV038Async(selectedRoot);
                break;
            case LocalAppsActionChoiceV046.LaunchApp:
                await LaunchSelectedLocalAppV046Async(appId, selectedRoot);
                break;
            case LocalAppsActionChoiceV046.ExportUpdateContext:
                await ExportUpdateContextV046Async(appId);
                break;
            case LocalAppsActionChoiceV046.BindDevelopmentSource:
                await BindDevelopmentSourceV046Async(appId);
                break;
            case LocalAppsActionChoiceV046.ExportPrivateDevelopmentContext:
                await ExportPrivateDevelopmentContextV046Async(appId);
                break;
            case LocalAppsActionChoiceV046.Cancel:
            default:
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  local-app.v046.choice.cancelled app={appId}; effect=false");
                break;
        }
        RefreshInstalledAppsV044();
    }

    private async Task LaunchSelectedLocalAppV046Async(string appId, string appRoot)
    {
        var dialog = new OpenFileDialog
        {
            Title = $"Select exact EXE to launch — {appId}",
            InitialDirectory = appRoot,
            Filter = "Windows executable (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var plan = _localAppLaunchV046Service.Preview(WorkspaceRootBox.Text, appId, dialog.FileName, CancellationToken.None);
            var preview = $"Запустить зарегистрированное локальное приложение?\n\nApplicationId: {plan.ApplicationId}\nVersion: {plan.InstalledVersion}\nEXE: {plan.ExecutableRelativePath}\nSHA-256: {plan.ExecutableSha256}\nBytes: {plan.ExecutableBytes}\n\nБудет запущен только этот exact EXE без аргументов. Workbench не утверждает, что поведение самого приложения после запуска sandboxed.";
            if (MessageBox.Show(this, preview, "Launch app", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            SetV035PrimaryControlsEnabled(false);
            BeginRun($"local-app-launch-v0.46-{DateTime.Now:yyyyMMddHHmmss}");
            var result = await _localAppLaunchV046Service.LaunchAsync(plan, WorkspaceRootBox.Text, _cts!.Token);
            LocalAppsTextBox.Text = CommandCodec.Serialize(new { Plan = plan, Launch = result.Receipt, LaunchReceiptPath = result.ArtifactPath });
            OutputTabs.SelectedItem = LocalAppsTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: launched {appId}; pid={result.Receipt.ProcessId}; exactExe=true";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  local-app.launch.completed app={appId}; pid={result.Receipt.ProcessId}; args=false");
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally { EndRun(); SetV035PrimaryControlsEnabled(true); OperatorSurfaceV045Contract.Apply(this); }
    }

    private async Task ExportUpdateContextV046Async(string appId)
    {
        try
        {
            SetV035PrimaryControlsEnabled(false);
            BeginRun($"local-app-update-context-v0.46-{DateTime.Now:yyyyMMddHHmmss}");
            var result = await _localAppUpdateContextV046Service.ExportAsync(WorkspaceRootBox.Text, appId, _cts!.Token);
            LocalAppsTextBox.Text = CommandCodec.Serialize(new
            {
                UpdateContext = result.Context,
                UpdateContextPath = result.ArtifactPath,
                ContainsFileContents = false,
                IntendedUse = "Give this small JSON to another chat so it can build sparse matawaka.local-app-update-package/v1 without copying unchanged/private files."
            });
            OutputTabs.SelectedItem = LocalAppsTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: exported content-free update context for {appId}";
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally { EndRun(); SetV035PrimaryControlsEnabled(true); OperatorSurfaceV045Contract.Apply(this); }
    }

    private async Task BindDevelopmentSourceV046Async(string appId)
    {
        var expected = Path.Combine(Path.GetFullPath(WorkspaceRootBox.Text.Trim()), LocalAppSourceBindingV046Service.SourcesDirectoryName, appId);
        if (!Directory.Exists(expected))
        {
            MessageBox.Show(this,
                $"Development source root ещё не существует:\n{expected}\n\nПопросите создающий приложение чат выдать source-seed ZIP, вручную распакуйте единственную папку {appId} сюда, затем повторите Bind development source. Workbench не импортирует и не копирует source bytes сам.",
                "Bind development source", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            var plan = _localAppSourceBindingV046Service.Preview(WorkspaceRootBox.Text, appId, CancellationToken.None);
            var preview = $"Связать development source с зарегистрированным приложением?\n\nApplicationId: {plan.ApplicationId}\nInstalled version: {plan.InstalledVersion}\nSource root: {plan.SourceRoot}\nFiles: {plan.SourceFileCount}; bytes: {plan.SourceBytes}\nInitial source tree SHA-256: {plan.SourceTreeSha256}\n\nБудет создан только .matawaka-source.json. Обычные source bytes и installed app не изменяются.";
            if (MessageBox.Show(this, preview, "Bind development source", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            SetV035PrimaryControlsEnabled(false);
            BeginRun($"local-app-source-bind-v0.46-{DateTime.Now:yyyyMMddHHmmss}");
            var result = await _localAppSourceBindingV046Service.BindAsync(plan, WorkspaceRootBox.Text, _cts!.Token);
            LocalAppsTextBox.Text = CommandCodec.Serialize(new { Preview = plan, Binding = result.Receipt, BindingReceiptPath = result.ArtifactPath });
            OutputTabs.SelectedItem = LocalAppsTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: bound development source for {appId}; sourceMutation=false";
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally { EndRun(); SetV035PrimaryControlsEnabled(true); OperatorSurfaceV045Contract.Apply(this); }
    }

    private async Task ExportPrivateDevelopmentContextV046Async(string appId)
    {
        try
        {
            var plan = _localAppPrivateContextV046Service.Preview(WorkspaceRootBox.Text, appId, CancellationToken.None);
            var preview = new StringBuilder();
            preview.AppendLine("Экспортировать PRIVATE development context?");
            preview.AppendLine();
            preview.AppendLine($"ApplicationId: {plan.ApplicationId}");
            preview.AppendLine($"Installed version: {plan.InstalledVersion}");
            preview.AppendLine($"Installed: {plan.InstalledFileCount} files; {plan.InstalledBytes} bytes");
            preview.AppendLine($"Source: {plan.SourceFileCount} files; {plan.SourceBytes} bytes");
            preview.AppendLine($"Total disclosure bytes before ZIP compression: {plan.TotalDisclosureBytes}");
            preview.AppendLine();
            preview.AppendLine("PRIVATE CONTENT: capsule intentionally copies installed contents, which may include bank statements, receipts, screenshots and other confidential evidence, plus development source. Workbench will create ONE LOCAL ZIP only. It will NOT upload it anywhere.");
            preview.AppendLine();
            preview.AppendLine("Yes authorizes only this local private export. Sharing/uploading the resulting ZIP remains a separate human decision.");
            if (MessageBox.Show(this, preview.ToString(), "Export PRIVATE development context", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            SetV035PrimaryControlsEnabled(false);
            BeginRun($"local-app-private-context-v0.46-{DateTime.Now:yyyyMMddHHmmss}");
            var result = await _localAppPrivateContextV046Service.ExportAsync(plan, WorkspaceRootBox.Text, _cts!.Token);
            LocalAppsTextBox.Text = CommandCodec.Serialize(new
            {
                Preview = plan,
                Export = result.Receipt,
                ExportReceiptPath = result.ArtifactPath,
                UploadPerformed = false,
                NextHumanDecision = "Attach CapsulePath to a chosen private chat only if disclosure is intended."
            });
            OutputTabs.SelectedItem = LocalAppsTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: PRIVATE local context exported for {appId}; upload=false";
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally { EndRun(); SetV035PrimaryControlsEnabled(true); OperatorSurfaceV045Contract.Apply(this); }
    }

    private async void Window_LoadedV046(object sender, RoutedEventArgs e)
    {
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
        if (_v046LoadedBootstrapChecked) return;
        _v046LoadedBootstrapChecked = true;
        TransitionBootstrapV040Claim? claim = null;
        var beganRun = false;
        try
        {
            claim = await _transitionBootstrapV040Service.TryClaimFirstBootAsync(WorkspaceRootBox.Text, LocalCheckpointV046Service.Version, LocalCheckpointV046Service.TargetTag, CancellationToken.None);
            if (claim is null)
            {
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v046 none; automaticValidation=false; automaticAccept=false");
                return;
            }
            SetV035PrimaryControlsEnabled(false);
            OperatorSurfaceV045Contract.Apply(this);
            BeginRun($"first-boot-bootstrap-v0.46-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;
            StatusText.Text = $"RUNNING: v0.46 operational-handoff validation; lease={claim.Lease.LeaseId}";
            var tested = await RunV046AcceptanceArtifactAsync(_cts!.Token);
            _lastAcceptanceReceipt = tested.Receipt;
            _lastAcceptanceArtifactPath = tested.ArtifactPath;
            _lastAcceptanceConsumed = false;
            if (!tested.Receipt.Passed)
            {
                await _transitionBootstrapV040Service.MarkFailedNoRetryAsync(claim.Lease, claim.LeasePath, "v0.46 validation returned Passed=false", CancellationToken.None);
                _currentTerminalState = CommandTerminalState.Failed;
                StatusText.Text = "FAILED: v0.46 validation did not pass; automatic local Accept refused; no retry authority";
                AcceptanceTextBox.Text = CommandCodec.Serialize(new { Bootstrap = claim.Lease, Acceptance = tested.Receipt, tested.ArtifactPath, AutomaticAcceptPerformed = false });
                OutputTabs.SelectedItem = AcceptanceTab;
                return;
            }
            var checkpointCandidate = await _checkpointV046Service.PreviewAsync(WorkspaceRootBox.Text, tested.ArtifactPath, tested.Receipt, _cts.Token);
            var checkpoint = await _checkpointV046Service.AcceptFromBootstrapAsync(checkpointCandidate, claim.Lease.LeaseId, _cts.Token);
            var checkpointPath = await LocalCheckpointV046Service.WriteReceiptAsync(WorkspaceRootBox.Text, checkpoint, _cts.Token);
            var completed = await _transitionBootstrapV040Service.FinalizeAcceptedAsync(claim, tested.ArtifactPath, checkpointPath, _cts.Token);
            _lastAcceptanceConsumed = true;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: v0.46 validation PASS + automatic local Accept; {checkpoint.Tag} -> {checkpoint.NewHead}";
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Bootstrap = completed,
                Acceptance = tested.Receipt,
                AcceptanceArtifactPath = tested.ArtifactPath,
                Checkpoint = checkpoint,
                CheckpointReceiptPath = checkpointPath,
                FourButtonSurfacePreserved = true,
                LaunchAppExplicitOnly = true,
                UpdateContextContentFree = true,
                DevelopmentSourceFixedRoot = true,
                PrivateContextLocalOnly = true,
                ReadToolPrimitiveImplemented = true,
                ExternalReadTransportImplemented = false,
                AutomaticPublishPerformed = false,
                AutomaticLifecyclePerformed = false,
                NextExplicitActions = new[] { "Real-host Local apps operational handoff check", "Publish accepted", "Lifecycle receipt" }
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

    private async Task<(WorkbenchAcceptanceReceipt Receipt, string ArtifactPath)> RunV046AcceptanceArtifactAsync(CancellationToken cancellationToken)
    {
        OperatorSurfaceV045Contract.Apply(this);
        var context = new RuntimeContext(CatalogRootBox.Text, true, false);
        var receipt = await new WorkbenchV046AcceptanceHarness(_acceptanceHarness, this).RunAsync(context, cancellationToken);
        var dir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"v0.46-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, CommandCodec.Serialize(receipt), new UTF8Encoding(false), cancellationToken);
        return (receipt, path);
    }

    private async void PublishAcceptedV046Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            OperatorSurfaceV045Contract.Apply(this);
            SaveSettings();
            var candidate = await _fixedGitHubPublicationV046Service.PreviewAsync(WorkspaceRootBox.Text, CancellationToken.None);
            var preview = $"Опубликовать принятый Workbench v0.46?\n\nRemote: {candidate.RemoteName}\nAccepted HEAD: {candidate.Head}\nParent: {candidate.Parent} / {FixedGitHubPublicationV046Service.ExpectedParentTag}\nTag: {candidate.AcceptedTag}\n\nYes только после real-host проверки. PRIVATE app/source/context bytes не входят в Workbench checkpoint и не публикуются этой кнопкой.";
            if (MessageBox.Show(this, preview, "Publish accepted v0.46", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            SetV035PrimaryControlsEnabled(false);
            OperatorSurfaceV045Contract.Apply(this);
            BeginRun($"publish-v0.46-{DateTime.Now:yyyyMMddHHmmss}");
            var receipt = await _fixedGitHubPublicationV046Service.PublishAsync(candidate, _cts!.Token);
            var path = await FixedGitHubPublicationV046Service.WriteReceiptAsync(WorkspaceRootBox.Text, receipt, _cts.Token);
            AcceptanceTextBox.Text = CommandCodec.Serialize(new { Publication = receipt, PublicationReceiptPath = path, PrivateAppBytesPublished = false, NextExplicitAction = "Lifecycle receipt" });
            OutputTabs.SelectedItem = AcceptanceTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: remote main/v0.46 tag == {receipt.LocalHead}";
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally { EndRun(); SetV035PrimaryControlsEnabled(true); OperatorSurfaceV045Contract.Apply(this); RefreshInstalledAppsV044(); }
    }
}
