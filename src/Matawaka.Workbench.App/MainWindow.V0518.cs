using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalAppMcpOwnershipStatusV0518Service _mcpOwnershipStatusV0518Service = new();
    private readonly LocalAppMcpOwnershipRecoveryV0518Service _mcpOwnershipRecoveryV0518Service = new();
    private bool _v0518OwnershipStatusEnabled;

    internal void ConfigureV0518Routing()
    {
        ConfigureV0517Routing();
        Title = "Matawaka Workbench v0.51.8";
        UpdateLocalAppButton.Click -= LocalAppsV0517Button_Click;
        UpdateLocalAppButton.Click += LocalAppsV0518Button_Click;
        _v0518OwnershipStatusEnabled = true;
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
    }

    private async void LocalAppsV0518Button_Click(object sender, RoutedEventArgs e)
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
            case LocalAppsActionChoiceV050.ChatReadRelay: await ChatReadRelayV047Async(appId); break;
            case LocalAppsActionChoiceV050.ReadSessionStatus: await ShowCoherentLiveReadSessionStatusV0516Async(appId); break;
            case LocalAppsActionChoiceV050.ReadSessionHistoryPage: ShowCanonicalReadSessionHistoryPageV0515(appId); break;
            case LocalAppsActionChoiceV050.McpOwnershipStatus: ShowMcpOwnershipStatusV0518(appId); break;
            case LocalAppsActionChoiceV050.AcknowledgeStaleMcpOwnershipMetadata: await AcknowledgeStaleMcpOwnershipMetadataV0518Async(appId); break;
            case LocalAppsActionChoiceV050.ReadSessionLease: await CreateOwnedReadLeaseAndAutoStartMcpV0517Async(appId); break;
            case LocalAppsActionChoiceV050.StopReadOnlyMcpAdapter: await EndOwnedReadSessionV0517Async(appId); break;
            case LocalAppsActionChoiceV050.EndOrphanedReadSession: await EndOrphanedWithFreeMcpDomainV0517Async(appId); break;
            case LocalAppsActionChoiceV050.RevokeReadLeases: await RevokeAllWithFreeMcpDomainV0517Async(appId); break;
            case LocalAppsActionChoiceV050.StartReadOnlyMcpAdapter: await StartOwnedManualMcpV0517Async(appId); break;
            case LocalAppsActionChoiceV050.StartSecureMcpTunnel: await StartSecureMcpTunnelV0502Async(appId); break;
            case LocalAppsActionChoiceV050.StopSecureMcpTunnel: await StopSecureMcpTunnelV0502Async(appId); break;
            default:
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  local-app.v0518.choice.cancelled app={appId}; effect=false");
                break;
        }
        RefreshInstalledAppsV044();
    }

    private void ShowMcpOwnershipStatusV0518(string appId)
    {
        try
        {
            var status = _mcpOwnershipStatusV0518Service.Observe(WorkspaceRootBox.Text, appId);
            LocalAppsTextBox.Text = CommandCodec.Serialize(status);
            OutputTabs.SelectedItem = LocalAppsTab;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: MCP Ownership Status for {appId}; owner={status.Status}; lease={status.LeaseObservation.Classification}; historicalScan=false; bearer=omitted";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  mcp-ownership-status.v0518 app={appId}; owner={status.Status}; lease={status.LeaseObservation.Classification}; mutation=false; bearer=false");
        }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
    }

    private async Task AcknowledgeStaleMcpOwnershipMetadataV0518Async(string appId)
    {
        LocalAppMcpOwnershipStatusV0518 before;
        try { before = _mcpOwnershipStatusV0518Service.Observe(WorkspaceRootBox.Text, appId); }
        catch (InvalidDataException ex) { ShowInvalid(ex); return; }

        if (!before.Status.Equals("FREE_STALE_METADATA", StringComparison.Ordinal))
        {
            ShowInvalid(new InvalidDataException(
                $"MCP_STALE_METADATA_ACK_NOT_APPLICABLE: ownership status is {before.Status}; acknowledgement requires FREE_STALE_METADATA."));
            return;
        }

        var message = new StringBuilder();
        message.AppendLine("Acknowledge stale MCP owner metadata and rotate it into preserved evidence?");
        message.AppendLine();
        message.AppendLine($"ApplicationId: {appId}");
        message.AppendLine($"SessionId: {before.SessionId ?? "(untrusted/unknown)"}");
        message.AppendLine($"Metadata LeaseId: {before.MetadataLeaseId ?? "(none/untrusted)"}");
        message.AppendLine($"Lease classification: {before.LeaseObservation.Classification}");
        message.AppendLine();
        message.AppendLine("This does NOT revoke/renew/create a lease and does NOT resume/start MCP. The operation re-proves the owner domain is free under the existing owner.lock, preserves the exact old metadata bytes as evidence, and clears only the active stale metadata slot.");
        if (before.LeaseObservation.Classification == "LIVE_ORPHAN")
            message.AppendLine("A live orphan lease will remain live after acknowledgement; use the existing exact orphan-closure action separately if you intend to close it.");
        message.AppendLine();
        message.AppendLine("Continue?");

        if (MessageBox.Show(this, message.ToString(), "Acknowledge stale MCP owner metadata v0.51.8", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var beganRun = false;
        try
        {
            SetV035PrimaryControlsEnabled(false);
            BeginRun($"ack-stale-mcp-owner-v0.51.8-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;
            var rotated = await _mcpOwnershipRecoveryV0518Service.AcknowledgeAndRotateAsync(
                WorkspaceRootBox.Text, appId, _cts!.Token);
            var after = _mcpOwnershipStatusV0518Service.Observe(WorkspaceRootBox.Text, appId);

            LocalAppsTextBox.Text = CommandCodec.Serialize(new
            {
                Status = rotated.Receipt.Status,
                Before = before,
                RecoveryReceipt = rotated.Receipt,
                RecoveryReceiptPath = rotated.ReceiptPath,
                After = after,
                LiveOrphanClosurePerformed = false,
                CanonicalLeaseMutationPerformed = false,
                ActiveIndexMutationPerformed = false,
                McpResumePerformed = false,
                BearerPlaintextDisclosed = false,
                BearerHashDisclosed = false,
                EndpointSecretDisclosed = false
            });
            OutputTabs.SelectedItem = LocalAppsTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: stale MCP owner metadata acknowledged for {appId}; evidence preserved; activeSlotCleared=true; canonicalLeaseMutation=false; after={after.Status}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  mcp-owner-stale-ack.v0518 app={appId}; lease={before.LeaseObservation.Classification}; evidence=true; canonicalMutation=false; resume=false; bearer=false");
        }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (Exception ex) { ShowFailure(ex); }
        finally
        {
            if (beganRun) EndRun();
            SetV035PrimaryControlsEnabled(true);
            OperatorSurfaceV045Contract.Apply(this);
            RefreshInstalledAppsV044();
        }
    }

    internal IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> ObserveV0518OwnershipStatusContract()
        => new[]
        {
            ("v0518-status-enabled", _v0518OwnershipStatusEnabled, _v0518OwnershipStatusEnabled.ToString(), "True"),
            ("v0518-status-read-only", true, "MCP Ownership Status", "no canonical/index/metadata mutation"),
            ("v0518-status-states", true, "OWNED/FREE_NO_METADATA/FREE_STALE_METADATA", "explicit"),
            ("v0518-ack-explicit", true, "separate confirmation", "no automatic rotation"),
            ("v0518-ack-free-domain", true, "existing owner.lock exclusive guard", "required"),
            ("v0518-ack-evidence", true, "stale metadata archived exact + SHA receipt", "preserved"),
            ("v0518-ack-authority", true, "no revoke/renew/create/resume authority", "none"),
            ("v0518-secrets", true, "bearer/hash/path-token omitted", "omitted"),
            ("v0518-publication", true, "deferred", "no remote mutation")
        };
}
