using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Matawaka.Workbench.Protocol;
using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private readonly LocalCheckpointV044Service _checkpointV044Service = new();
    private readonly FixedGitHubPublicationV044Service _fixedGitHubPublicationV044Service = new();
    private bool _v044LoadedBootstrapChecked;

    internal void ConfigureV044Routing()
    {
        ConfigureV043Routing();
        Title = "Matawaka Workbench v0.44";

        Loaded -= Window_LoadedV043;
        Loaded += Window_LoadedV044;
        PublishAcceptedButton.Click -= PublishAcceptedV043Button_Click;
        PublishAcceptedButton.Click += PublishAcceptedV044Button_Click;

        Activated -= WindowV043_Activated;
        Activated += WindowV044_Activated;
        UpdateLocalAppButton.Click -= UpdateLocalAppV043Refresh_Click;
        UpdateLocalAppButton.Click += UpdateLocalAppV044Refresh_Click;

        DisableLegacyManualControlsV042();
        RefreshInstalledAppsV044();
    }

    private void WindowV044_Activated(object? sender, EventArgs e)
        => RefreshInstalledAppsV044();

    private void UpdateLocalAppV044Refresh_Click(object sender, RoutedEventArgs e)
        => Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ContextIdle,
            new Action(RefreshInstalledAppsV044));

    private void RefreshInstalledAppsV044()
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
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  apps.v044.observation.warning    {ex.Message}");
        }
    }

    private void InstalledAppV044Button_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: InstalledAppV042 app }) return;
        try
        {
            OpenOrRefreshAppTreeTabV044(app);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"WARNING: app tree unavailable for {app.ApplicationId}: {ex.Message}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  app-tree.v044.warning         app={app.ApplicationId}; {ex.Message}");
        }
    }

    private void OpenOrRefreshAppTreeTabV044(InstalledAppV042 app)
    {
        var observation = WorkbenchAppTreeV043Service.Read(WorkspaceRootBox.Text, app.ApplicationId);
        var existing = FindDynamicTabV044("tree", app.ApplicationId, null);
        var content = BuildAppTreeContentV044(observation);

        if (existing is null)
        {
            existing = new TabItem
            {
                Tag = new DynamicInspectionTabTagV044("tree", app.ApplicationId, null),
                Content = content
            };
            existing.Header = BuildClosableHeaderV044(existing, observation.TabHeader);
            OutputTabs.Items.Add(existing);
        }
        else
        {
            existing.Content = content;
            existing.Header = BuildClosableHeaderV044(existing, observation.TabHeader);
        }

        OutputTabs.SelectedItem = existing;
        StatusText.Text = $"COMPLETED: opened read-only tree for {observation.ApplicationId}; double-click a text file to inspect contents";
        EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  app-tree.v044.opened          app={observation.ApplicationId}; dirs={observation.DirectoryCount}; files={observation.FileCount}; closable=true; fileDoubleClick=true; authority=false");
    }

    private FrameworkElement BuildAppTreeContentV044(AppTreeObservationV043 observation)
    {
        var grid = new Grid { Margin = new Thickness(6) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var summary = new TextBlock
        {
            Text = observation.Summary + "   |   Double-click a text file to open contents",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(2, 0, 2, 6)
        };
        grid.Children.Add(summary);

        var tree = new TreeView
        {
            ItemsSource = new[] { observation.Root },
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Tag = observation.ApplicationId
        };
        tree.MouseDoubleClick += AppTreeV044_MouseDoubleClick;
        tree.Loaded += (_, _) =>
        {
            if (tree.ItemContainerGenerator.ContainerFromIndex(0) is TreeViewItem root)
                root.IsExpanded = true;
        };
        Grid.SetRow(tree, 1);
        grid.Children.Add(tree);
        return grid;
    }

    private void AppTreeV044_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TreeView { Tag: string applicationId } tree) return;
        if (e.OriginalSource is not DependencyObject source) return;
        if (ItemsControl.ContainerFromElement(tree, source) is not TreeViewItem { DataContext: AppTreeNodeV043 node }) return;
        if (node.IsDirectory || string.IsNullOrWhiteSpace(node.RelativePath)) return;

        e.Handled = true;
        try
        {
            OpenOrRefreshAppTextTabV044(applicationId, node.RelativePath);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"WARNING: text file unavailable: {applicationId}/{node.RelativePath}: {ex.Message}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  app-text.v044.warning         app={applicationId}; path={node.RelativePath}; {ex.Message}");
        }
    }

    private void OpenOrRefreshAppTextTabV044(string applicationId, string relativePath)
    {
        var observation = WorkbenchAppTextV044Service.Read(WorkspaceRootBox.Text, applicationId, relativePath);
        var existing = FindDynamicTabV044("text", observation.ApplicationId, observation.RelativePath);
        var textBox = BuildAppTextContentV044(observation);

        if (existing is null)
        {
            existing = new TabItem
            {
                Tag = new DynamicInspectionTabTagV044("text", observation.ApplicationId, observation.RelativePath),
                Content = textBox
            };
            existing.Header = BuildClosableHeaderV044(existing, observation.TabTitle);
            OutputTabs.Items.Add(existing);
        }
        else
        {
            existing.Content = textBox;
            existing.Header = BuildClosableHeaderV044(existing, observation.TabTitle);
        }

        OutputTabs.SelectedItem = existing;
        StatusText.Text = $"COMPLETED: opened read-only text {observation.ApplicationId}/{observation.RelativePath}; {observation.Bytes:N0} B; {observation.EncodingName}";
        EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  app-text.v044.opened          app={observation.ApplicationId}; path={observation.RelativePath}; bytes={observation.Bytes}; encoding={observation.EncodingName}; write=false; execute=false");
    }

    private static TextBox BuildAppTextContentV044(AppTextObservationV044 observation)
    {
        var textBox = new TextBox
        {
            Text = observation.Text,
            IsReadOnly = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            TextWrapping = TextWrapping.NoWrap,
            ToolTip = observation.Summary,
            Padding = new Thickness(6)
        };
        JsonSearchPresentationV0412Service.ConfigureVisibleInactiveSelection(textBox);
        return textBox;
    }

    private TabItem? FindDynamicTabV044(string kind, string applicationId, string? relativePath)
        => OutputTabs.Items.OfType<TabItem>().FirstOrDefault(tab =>
            tab.Tag is DynamicInspectionTabTagV044 tag &&
            tag.Kind.Equals(kind, StringComparison.Ordinal) &&
            tag.ApplicationId.Equals(applicationId, StringComparison.Ordinal) &&
            string.Equals(tag.RelativePath, relativePath, StringComparison.Ordinal));

    private FrameworkElement BuildClosableHeaderV044(TabItem tab, string title)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0)
        });
        var close = new Button
        {
            Content = "×",
            Width = 18,
            Height = 18,
            Padding = new Thickness(0),
            Margin = new Thickness(1, 0, 0, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            ToolTip = "Close this inspection tab",
            Tag = tab,
            Focusable = false
        };
        close.Click += CloseDynamicTabV044_Click;
        panel.Children.Add(close);
        return panel;
    }

    private void CloseDynamicTabV044_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { Tag: TabItem tab }) return;
        if (tab.Tag is not DynamicInspectionTabTagV044 tag) return;

        var selected = ReferenceEquals(OutputTabs.SelectedItem, tab);
        OutputTabs.Items.Remove(tab);
        if (selected) OutputTabs.SelectedItem = EventsTab;
        StatusText.Text = $"COMPLETED: closed {tag.Kind} inspection tab; application state unchanged";
        EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  inspection-tab.v044.closed       kind={tag.Kind}; app={tag.ApplicationId}; path={tag.RelativePath ?? "-"}; mutation=false");
    }

    private async void Window_LoadedV044(object sender, RoutedEventArgs e)
    {
        RefreshInstalledAppsV044();
        if (_v044LoadedBootstrapChecked) return;
        _v044LoadedBootstrapChecked = true;
        TransitionBootstrapV040Claim? claim = null;
        var beganRun = false;
        try
        {
            claim = await _transitionBootstrapV040Service.TryClaimFirstBootAsync(
                WorkspaceRootBox.Text,
                LocalCheckpointV044Service.Version,
                LocalCheckpointV044Service.TargetTag,
                CancellationToken.None);
            if (claim is null)
            {
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v044 none; automaticValidation=false; automaticAccept=false");
                return;
            }

            SetV035PrimaryControlsEnabled(false);
            DisableLegacyManualControlsV042();
            BeginRun($"first-boot-bootstrap-v0.44-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;
            StatusText.Text = $"RUNNING: v0.44 first-boot validation; lease={claim.Lease.LeaseId}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v044 consuming lease={claim.Lease.LeaseId}; pid={Environment.ProcessId}; retry=false");

            var tested = await RunV044AcceptanceArtifactAsync(_cts!.Token);
            _lastAcceptanceReceipt = tested.Receipt;
            _lastAcceptanceArtifactPath = tested.ArtifactPath;
            _lastAcceptanceConsumed = false;

            if (!tested.Receipt.Passed)
            {
                await _transitionBootstrapV040Service.MarkFailedNoRetryAsync(
                    claim.Lease, claim.LeasePath, "v0.44 first-boot validation returned Passed=false", CancellationToken.None);
                _currentTerminalState = CommandTerminalState.Failed;
                StatusText.Text = "FAILED: v0.44 first-boot validation did not pass; automatic local Accept refused; no retry authority";
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

            var checkpointCandidate = await _checkpointV044Service.PreviewAsync(
                WorkspaceRootBox.Text, tested.ArtifactPath, tested.Receipt, _cts.Token);
            var checkpoint = await _checkpointV044Service.AcceptFromBootstrapAsync(
                checkpointCandidate, claim.Lease.LeaseId, _cts.Token);
            var checkpointPath = await LocalCheckpointV044Service.WriteReceiptAsync(
                WorkspaceRootBox.Text, checkpoint, _cts.Token);
            var completed = await _transitionBootstrapV040Service.FinalizeAcceptedAsync(
                claim, tested.ArtifactPath, checkpointPath, _cts.Token);

            _lastAcceptanceConsumed = true;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: v0.44 first-boot validation PASS + automatic local Accept; {checkpoint.Tag} -> {checkpoint.NewHead}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v044 completed lease={completed.LeaseId}; validated=true; accepted=true; publish=false; lifecycle=false");
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
                AppFileDoubleClickTextInspection = true,
                DynamicInspectionTabsClosable = true,
                FixedWorkbenchTabsClosable = false,
                AppTextMaxBytes = WorkbenchAppTextV044Service.MaxTextBytes,
                AppTextReadOnly = true,
                AppTextExecutionAuthority = false,
                VisibleTopLevelMaintenanceButtons = 5,
                AutomaticPublishPerformed = false,
                AutomaticLifecyclePerformed = false,
                NextExplicitActions = new[] { "Real-host double-click/close check", "Publish accepted", "Lifecycle receipt" }
            });
            OutputTabs.SelectedItem = AcceptanceTab;
            RefreshInstalledAppsV044();
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
            RefreshInstalledAppsV044();
        }
    }

    private async Task<(WorkbenchAcceptanceReceipt Receipt, string ArtifactPath)> RunV044AcceptanceArtifactAsync(CancellationToken cancellationToken)
    {
        var context = new RuntimeContext(CatalogRootBox.Text, true, false);
        var receipt = await new WorkbenchV044AcceptanceHarness(_acceptanceHarness).RunAsync(context, cancellationToken);
        var dir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"v0.44-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, CommandCodec.Serialize(receipt), new UTF8Encoding(false), cancellationToken);
        return (receipt, path);
    }

    private async void PublishAcceptedV044Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveSettings();
            var candidate = await _fixedGitHubPublicationV044Service.PreviewAsync(WorkspaceRootBox.Text, CancellationToken.None);
            var preview = $"Опубликовать принятый Workbench v0.44?\n\nRemote: {candidate.RemoteName}\nURL: {candidate.RemoteUrl}\nAccepted HEAD: {candidate.Head}\nParent: {candidate.Parent} / {FixedGitHubPublicationV044Service.ExpectedParentTag}\nTag: {candidate.AcceptedTag}\n\nНажимайте Yes только после real-host проверки: double-click текстового файла открывает read-only вкладку, повтор не создаёт дубль, × закрывает dynamic inspection tab. Только exact fast-forward/tag; Lifecycle остаётся отдельным действием.";
            if (MessageBox.Show(this, preview, "Publish accepted v0.44", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            SetV035PrimaryControlsEnabled(false);
            DisableLegacyManualControlsV042();
            BeginRun($"publish-v0.44-{DateTime.Now:yyyyMMddHHmmss}");
            StatusText.Text = "RUNNING: publish accepted v0.44 to fixed GitHub remote";
            var receipt = await _fixedGitHubPublicationV044Service.PublishAsync(candidate, _cts!.Token);
            var path = await FixedGitHubPublicationV044Service.WriteReceiptAsync(WorkspaceRootBox.Text, receipt, _cts.Token);
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Publication = receipt,
                PublicationReceiptPath = path,
                AppTextInspectionAuthorityCreated = false,
                AppMutationAuthorityCreated = false,
                NextExplicitAction = "Lifecycle receipt"
            });
            OutputTabs.SelectedItem = AcceptanceTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: remote main/v0.44 tag == {receipt.LocalHead}";
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally
        {
            EndRun();
            SetV035PrimaryControlsEnabled(true);
            DisableLegacyManualControlsV042();
            RefreshInstalledAppsV044();
        }
    }

    private sealed record DynamicInspectionTabTagV044(string Kind, string ApplicationId, string? RelativePath);
}
