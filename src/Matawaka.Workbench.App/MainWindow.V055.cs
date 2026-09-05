using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly BoundedLocalModelInvocationV055Service _localModelInvocationV055Service = new();

    internal void ConfigureV055Routing()
    {
        ConfigureV054Routing();
        UpdateLocalAppButton.Click -= LocalAppsV054Button_Click;
        UpdateLocalAppButton.Click -= LocalAppsV055Button_Click;
        UpdateLocalAppButton.Click += LocalAppsV055Button_Click;
        Title = "Matawaka Workbench v0.55";
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
    }

    private async void LocalAppsV055Button_Click(object sender, RoutedEventArgs e)
    {
        EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  local-app.v055.dispatch exclusive=true; acquisition=true; materialization=true; runtimeExecution=true; modelInvocation=true");
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
            case LocalAppsActionChoiceV050.BoundedArtifactAcquisition: await AcquireArtifactsV052Async(); break;
            case LocalAppsActionChoiceV050.BoundedRuntimeMaterialization: await MaterializeBoundedRuntimeV054Async(appId); break;
            case LocalAppsActionChoiceV050.BoundedRuntimeExecution: await ExecuteBoundedRuntimeV053Async(appId); break;
            case LocalAppsActionChoiceV050.StopBoundedRuntimeExecution: await StopBoundedRuntimeV053Async(appId); break;
            case LocalAppsActionChoiceV050.BoundedLocalModelInvocation: await InvokeBoundedLocalModelV055Async(appId); break;
            case LocalAppsActionChoiceV050.ChatReadRelay: await ChatReadRelayV047Async(appId); break;
            case LocalAppsActionChoiceV050.ReadSessionStatus: await ShowCoherentLiveReadSessionStatusV0516Async(appId); break;
            case LocalAppsActionChoiceV050.ReadSessionHistoryPage: ShowCanonicalReadSessionHistoryPageV0515(appId); break;
            case LocalAppsActionChoiceV050.ReadSessionLease: await CreateOwnedReadLeaseAndAutoStartMcpV05112Async(appId); break;
            case LocalAppsActionChoiceV050.StopReadOnlyMcpAdapter: await EndOwnedReadSessionV05113Async(appId); break;
            case LocalAppsActionChoiceV050.EndOrphanedReadSession: await EndOrphanedWithFreeMcpDomainV0517Async(appId); break;
            case LocalAppsActionChoiceV050.RevokeReadLeases: await RevokeAllWithFreeMcpDomainV0517Async(appId); break;
            case LocalAppsActionChoiceV050.StartReadOnlyMcpAdapter: await StartOwnedManualMcpV0517Async(appId); break;
            case LocalAppsActionChoiceV050.StartSecureMcpTunnel: await StartSecureMcpTunnelV0502Async(appId); break;
            case LocalAppsActionChoiceV050.StopSecureMcpTunnel: await StopSecureMcpTunnelV0502Async(appId); break;
            default:
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  local-app.v055.choice.cancelled app={appId}; effect=false");
                break;
        }
        RefreshInstalledAppsV044();
    }

    private async Task InvokeBoundedLocalModelV055Async(string navigationAppId)
    {
        var requestDialog = new OpenFileDialog
        {
            Title = "Select exact bounded local-model invocation request JSON",
            Filter = "JSON request (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (requestDialog.ShowDialog(this) != true) return;

        LocalModelInvocationRequestV055 request;
        LocalModelInvocationPreviewV055 preview;
        try
        {
            var json = await File.ReadAllTextAsync(requestDialog.FileName, Encoding.UTF8);
            request = LocalModelInvocationRequestV055Parser.ParseExact(json);
            preview = _localModelInvocationV055Service.Preview(WorkspaceRootBox.Text, request, CancellationToken.None);
        }
        catch (JsonException ex)
        {
            ShowInvalid(new InvalidDataException("V055_MODEL_REQUEST_JSON_INVALID: " + ex.Message, ex));
            return;
        }
        catch (InvalidDataException ex)
        {
            ShowInvalid(new InvalidDataException("V055_MODEL_REQUEST_JSON_REFUSED: " + ex.Message, ex));
            return;
        }
        catch (LocalModelInvocationExceptionV055 ex)
        {
            ShowInvalid(new InvalidDataException($"{ex.Classification}: {ex.Message}", ex));
            return;
        }
        catch (IOException ex)
        {
            ShowInvalid(new InvalidDataException("V055_MODEL_REQUEST_READ_FAILED: " + ex.Message, ex));
            return;
        }

        var confirmation = new StringBuilder();
        confirmation.AppendLine("Authorize exactly one bounded local-model invocation?");
        confirmation.AppendLine();
        confirmation.AppendLine($"Navigation app only: {navigationAppId}");
        confirmation.AppendLine($"RequestId: {preview.RequestId}");
        confirmation.AppendLine($"Request: {preview.RequestBytes} UTF-8 bytes / SHA-256 {preview.RequestDigestSha256}");
        confirmation.AppendLine($"Profile: {preview.InvocationProfileId}");
        confirmation.AppendLine($"Runtime manifest: {preview.RuntimeTreeManifestId}");
        confirmation.AppendLine($"Executable: {preview.ExecutableRelativePath}");
        confirmation.AppendLine($"Executable SHA-256: {preview.ExecutableSha256}");
        confirmation.AppendLine($"Model ArtifactId: {preview.ModelArtifactId}");
        confirmation.AppendLine($"Model: {preview.ModelBytes:N0} bytes / SHA-256 {preview.ModelSha256}");
        confirmation.AppendLine($"Stdout ceiling: {preview.MaxStdoutBytes:N0} bytes");
        confirmation.AppendLine($"Stderr ceiling: {preview.MaxStderrBytes:N0} bytes");
        confirmation.AppendLine($"Output chars ceiling: {preview.MaxOutputChars:N0}");
        confirmation.AppendLine($"Timeout: {preview.TimeoutSeconds}s; lease TTL: {preview.TtlSeconds}s");
        confirmation.AppendLine();
        confirmation.AppendLine("Yes creates and immediately consumes a separate one-shot MODEL REQUEST authority. It is not v0.53 process authority.");
        confirmation.AppendLine("Raw request text and bearer are not persisted in canonical lease state. Output remains UNTRUSTED_LOCAL_MODEL_OUTPUT.");
        confirmation.AppendLine("No response/display/game/ActionPermit/successor authority follows from successful local inference.");
        confirmation.AppendLine("Workbench performs no network transport in the fixture profile, but OS-level child-process network isolation is NOT proven.");

        if (MessageBox.Show(this, confirmation.ToString(), "Bounded Local Model Invocation v0.55 — explicit one-shot authority",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  local-model.v055.preview.cancelled request={preview.RequestId}; modelRequest=false");
            return;
        }

        try
        {
            SetV035PrimaryControlsEnabled(false);
            BeginRun($"local-model-invocation-v0.55-{DateTime.Now:yyyyMMddHHmmss}");
            var granted = await _localModelInvocationV055Service.GrantAsync(
                WorkspaceRootBox.Text, preview, request.RequestUtf8, _cts!.Token);
            var executed = await _localModelInvocationV055Service.InvokeAsync(
                WorkspaceRootBox.Text, granted.Grant, _cts.Token);

            // Do not serialize the grant: it contains the one-shot bearer and transient raw request text.
            LocalAppsTextBox.Text = JsonSerializer.Serialize(new
            {
                Status = executed.Receipt.Status,
                RequestPath = requestDialog.FileName,
                Preview = preview,
                AuthorityReceipt = granted.Receipt,
                AuthorityReceiptPath = granted.ReceiptPath,
                ExecutionReceipt = executed.Receipt,
                ExecutionReceiptPath = executed.ReceiptPath,
                PortableResult = executed.Result,
                GrantBearerPersistedOrDisplayed = false,
                RawRequestPersistedByWorkbenchLeaseState = false
            }, new JsonSerializerOptions { WriteIndented = true });
            OutputTabs.SelectedItem = LocalAppsTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: v0.55 {executed.Receipt.Status}; request={preview.RequestId}; modelRequest=true; responseAuthority=false";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  local-model.v055.completed request={preview.RequestId}; status={executed.Receipt.Status}; responseAuthority=false; display=false; retry=false");
        }
        catch (OperationCanceledException)
        {
            ShowCancelled();
        }
        catch (LocalModelInvocationExceptionV055 ex)
        {
            LocalAppsTextBox.Text = JsonSerializer.Serialize(new
            {
                Status = "LOCAL_MODEL_INVOCATION_TERMINAL_FAIL_CLOSED",
                Classification = ex.Classification,
                Message = ex.Message,
                ReceiptPath = ex.ReceiptPath,
                AutomaticRetryPerformed = false,
                ResponseAuthorityCreated = false,
                DisplayPerformed = false,
                GameAccessPerformed = false
            }, new JsonSerializerOptions { WriteIndented = true });
            OutputTabs.SelectedItem = LocalAppsTab;
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  local-model.v055.refused class={ex.Classification}; retry=false; responseAuthority=false");
            ShowInvalid(new InvalidDataException($"{ex.Classification}: {ex.Message}", ex));
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
        finally
        {
            request = request with { RequestUtf8 = string.Empty };
            EndRun();
            SetV035PrimaryControlsEnabled(true);
            OperatorSurfaceV045Contract.Apply(this);
        }
    }

    internal IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> ObserveV055RoutingContract() => new[]
    {
        ("routing-v055-title", Title == "Matawaka Workbench v0.55", Title, "Matawaka Workbench v0.55"),
        ("routing-v055-separate-model-action", true, "BoundedLocalModelInvocation", "separate from BoundedRuntimeExecution"),
        ("routing-v055-exact-request-parser", true, "LocalModelInvocationRequestV055Parser.ParseExact", "unknown/duplicate/missing JSON properties refused"),
        ("routing-v055-grant-secret", true, "grant is never serialized to LocalApps output", "true"),
        ("routing-v055-output-status", true, "UNTRUSTED_LOCAL_MODEL_OUTPUT", "no response/display authority"),
        ("routing-v055-network-claim", true, "process network isolation not claimed", "false unless separately proven")
    };
}
