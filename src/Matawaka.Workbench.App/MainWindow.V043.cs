using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Matawaka.Workbench.Protocol;
using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalCheckpointV043Service _checkpointV043Service = new();
    private readonly FixedGitHubPublicationV043Service _fixedGitHubPublicationV043Service = new();
    private bool _v043LoadedBootstrapChecked;

    internal void ConfigureV043Routing()
    {
        ConfigureV042Routing();
        Title = "Matawaka Workbench v0.43";

        Loaded -= Window_LoadedV042;
        Loaded += Window_LoadedV043;
        PublishAcceptedButton.Click -= PublishAcceptedV042Button_Click;
        PublishAcceptedButton.Click += PublishAcceptedV043Button_Click;

        Activated -= WindowV042_Activated;
        Activated += WindowV043_Activated;
        UpdateLocalAppButton.Click += UpdateLocalAppV043Refresh_Click;

        DisableLegacyManualControlsV042();
        RefreshInstalledAppsV043();
    }

    private void WindowV043_Activated(object? sender, EventArgs e)
        => RefreshInstalledAppsV043();

    private void UpdateLocalAppV043Refresh_Click(object sender, RoutedEventArgs e)
        => Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ContextIdle,
            new Action(RefreshInstalledAppsV043));

    private void RefreshInstalledAppsV043()
    {
        try
        {
            var apps = InstalledAppsV042Service.Read(WorkspaceRootBox.Text);
            InstalledAppsList.ItemsSource = apps;
            InstalledAppsSummaryText.Text = $"Apps ({apps.Count})";
        }
        catch (Exception ex)
        {
            InstalledAppsList.ItemsSource = Array.Empty<InstalledAppV042>();
            InstalledAppsSummaryText.Text = "Apps ⚠";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  apps.v043.observation.warning    {ex.Message}");
        }
    }

    private void InstalledAppV043Button_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: InstalledAppV042 app }) return;
        try
        {
            OpenOrRefreshAppTreeTabV043(app);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"WARNING: app tree unavailable for {app.ApplicationId}: {ex.Message}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  app-tree.v043.warning         app={app.ApplicationId}; {ex.Message}");
        }
    }

    private void OpenOrRefreshAppTreeTabV043(InstalledAppV042 app)
    {
        var observation = WorkbenchAppTreeV043Service.Read(WorkspaceRootBox.Text, app.ApplicationId);
        var existing = OutputTabs.Items
            .OfType<TabItem>()
            .FirstOrDefault(tab => tab.Tag is AppTreeTabTagV043 tag && tag.ApplicationId.Equals(app.ApplicationId, StringComparison.Ordinal));

        var content = BuildAppTreeContentV043(observation);
        if (existing is null)
        {
            existing = new TabItem
            {
                Header = observation.TabHeader,
                Tag = new AppTreeTabTagV043(app.ApplicationId),
                Content = content
            };
            OutputTabs.Items.Add(existing);
        }
        else
        {
            existing.Header = observation.TabHeader;
            existing.Content = content;
        }

        OutputTabs.SelectedItem = existing;
        StatusText.Text = $"COMPLETED: opened read-only tree for {observation.ApplicationId}; {observation.DirectoryCount} folders / {observation.FileCount} files";
        EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  app-tree.v043.opened          app={observation.ApplicationId}; dirs={observation.DirectoryCount}; files={observation.FileCount}; skippedReparse={observation.SkippedReparsePoints}; authority=false");
    }

    private FrameworkElement BuildAppTreeContentV043(AppTreeObservationV043 observation)
    {
        var grid = new Grid { Margin = new Thickness(6) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var summary = new TextBlock
        {
            Text = observation.Summary,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(2, 0, 2, 6)
        };
        grid.Children.Add(summary);

        var tree = new TreeView
        {
            ItemsSource = new[] { observation.Root },
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        tree.Loaded += (_, _) =>
        {
            if (tree.ItemContainerGenerator.ContainerFromIndex(0) is TreeViewItem root)
                root.IsExpanded = true;
        };
        Grid.SetRow(tree, 1);
        grid.Children.Add(tree);
        return grid;
    }

    private async void Window_LoadedV043(object sender, RoutedEventArgs e)
    {
        RefreshInstalledAppsV043();
        if (_v043LoadedBootstrapChecked) return;
        _v043LoadedBootstrapChecked = true;
        TransitionBootstrapV040Claim? claim = null;
        var beganRun = false;
        try
        {
            claim = await _transitionBootstrapV040Service.TryClaimFirstBootAsync(
                WorkspaceRootBox.Text,
                LocalCheckpointV043Service.Version,
                LocalCheckpointV043Service.TargetTag,
                CancellationToken.None);
            if (claim is null)
            {
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v043 none; automaticValidation=false; automaticAccept=false");
                return;
            }

            SetV035PrimaryControlsEnabled(false);
            DisableLegacyManualControlsV042();
            BeginRun($"first-boot-bootstrap-v0.43-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;
            StatusText.Text = $"RUNNING: v0.43 first-boot validation; lease={claim.Lease.LeaseId}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v043 consuming lease={claim.Lease.LeaseId}; pid={Environment.ProcessId}; retry=false");

            var tested = await RunV043AcceptanceArtifactAsync(_cts!.Token);
            _lastAcceptanceReceipt = tested.Receipt;
            _lastAcceptanceArtifactPath = tested.ArtifactPath;
            _lastAcceptanceConsumed = false;

            if (!tested.Receipt.Passed)
            {
                await _transitionBootstrapV040Service.MarkFailedNoRetryAsync(
                    claim.Lease, claim.LeasePath, "v0.43 first-boot validation returned Passed=false", CancellationToken.None);
                _currentTerminalState = CommandTerminalState.Failed;
                StatusText.Text = "FAILED: v0.43 first-boot validation did not pass; automatic local Accept refused; no retry authority";
                AcceptanceTextBox.Text = CommandCodec.Serialize(new
                {
                    Bootstrap = claim.Lease,
                    BootstrapLeasePath = claim.LeasePath,
                    Acceptance = tested.Receipt,
                    AcceptanceArtifactPath = tested.ArtifactPath,
                    AutomaticAcceptPerformed = false,
                    AutomaticRetryAuthorized = false
                });
                OutputTabs.SelectedItem = AcceptanceTab;
                return;
            }

            var checkpointCandidate = await _checkpointV043Service.PreviewAsync(
                WorkspaceRootBox.Text, tested.ArtifactPath, tested.Receipt, _cts.Token);
            var checkpoint = await _checkpointV043Service.AcceptFromBootstrapAsync(
                checkpointCandidate, claim.Lease.LeaseId, _cts.Token);
            var checkpointPath = await LocalCheckpointV043Service.WriteReceiptAsync(
                WorkspaceRootBox.Text, checkpoint, _cts.Token);
            var completed = await _transitionBootstrapV040Service.FinalizeAcceptedAsync(
                claim, tested.ArtifactPath, checkpointPath, _cts.Token);

            _lastAcceptanceConsumed = true;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: v0.43 first-boot validation PASS + automatic local Accept; {checkpoint.Tag} -> {checkpoint.NewHead}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v043 completed lease={completed.LeaseId}; validated=true; accepted=true; publish=false; lifecycle=false");
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Bootstrap = completed,
                BootstrapLeasePath = claim.LeasePath,
                Acceptance = tested.Receipt,
                AcceptanceArtifactPath = tested.ArtifactPath,
                Checkpoint = checkpoint,
                CheckpointReceiptPath = checkpointPath,
                AutomaticValidationPerformed = true,
                AutomaticAcceptPerformed = true,
                ClickableInstalledApps = true,
                SeparateAppTreeTabs = true,
                AppTreeObservationOnly = true,
                AppFileContentsRead = false,
                VisibleTopLevelMaintenanceButtons = 5,
                AutomaticPublishPerformed = false,
                AutomaticLifecyclePerformed = false,
                NextExplicitActions = new[] { "Real-host app tree click check", "Publish accepted", "Lifecycle receipt" }
            });
            OutputTabs.SelectedItem = AcceptanceTab;
            RefreshInstalledAppsV043();
        }
        catch (OperationCanceledException ex)
        {
            if (claim is not null) await TryFailBootstrapAsync(claim.Lease, claim.LeasePath, ex.Message);
            ShowCancelled();
        }
        catch (InvalidDataException ex)
        {
            if (claim is not null) await TryFailBootstrapAsync(claim.Lease, claim.LeasePath, ex.Message);
            ShowInvalid(ex);
        }
        catch (Exception ex)
        {
            if (claim is not null) await TryFailBootstrapAsync(claim.Lease, claim.LeasePath, ex.Message);
            ShowFailure(ex);
        }
        finally
        {
            if (beganRun) EndRun();
            SetV035PrimaryControlsEnabled(true);
            DisableLegacyManualControlsV042();
            RefreshInstalledAppsV043();
        }
    }

    private async Task<(WorkbenchAcceptanceReceipt Receipt, string ArtifactPath)> RunV043AcceptanceArtifactAsync(CancellationToken cancellationToken)
    {
        var context = new RuntimeContext(CatalogRootBox.Text, true, false);
        var receipt = await new WorkbenchV043AcceptanceHarness(_acceptanceHarness).RunAsync(context, cancellationToken);
        var dir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"v0.43-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, CommandCodec.Serialize(receipt), new UTF8Encoding(false), cancellationToken);
        return (receipt, path);
    }

    private async void PublishAcceptedV043Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveSettings();
            var candidate = await _fixedGitHubPublicationV043Service.PreviewAsync(WorkspaceRootBox.Text, CancellationToken.None);
            var preview = $"Опубликовать принятый Workbench v0.43?\n\nRemote: {candidate.RemoteName}\nURL: {candidate.RemoteUrl}\nAccepted HEAD: {candidate.Head}\nParent: {candidate.Parent} / {FixedGitHubPublicationV043Service.ExpectedParentTag}\nTag: {candidate.AcceptedTag}\n\nНажимайте Yes только после проверки на реальном Windows-host: один клик по App открывает отдельную read-only вкладку с деревом, повторный клик не создаёт дубликат. Только exact fast-forward/tag; Lifecycle остаётся отдельным действием.";
            if (MessageBox.Show(this, preview, "Publish accepted v0.43", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            SetV035PrimaryControlsEnabled(false);
            DisableLegacyManualControlsV042();
            BeginRun($"publish-v0.43-{DateTime.Now:yyyyMMddHHmmss}");
            StatusText.Text = "RUNNING: publish accepted v0.43 to fixed GitHub remote";
            var receipt = await _fixedGitHubPublicationV043Service.PublishAsync(candidate, _cts!.Token);
            var path = await FixedGitHubPublicationV043Service.WriteReceiptAsync(WorkspaceRootBox.Text, receipt, _cts.Token);
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Publication = receipt,
                PublicationReceiptPath = path,
                AppTreeAuthorityCreated = false,
                AppFileContentReadAuthorityCreated = false,
                NextExplicitAction = "Lifecycle receipt"
            });
            OutputTabs.SelectedItem = AcceptanceTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: remote main/v0.43 tag == {receipt.LocalHead}";
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally
        {
            EndRun();
            SetV035PrimaryControlsEnabled(true);
            DisableLegacyManualControlsV042();
            RefreshInstalledAppsV043();
        }
    }

    private sealed record AppTreeTabTagV043(string ApplicationId);
}
