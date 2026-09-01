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
    private readonly LocalCheckpointV0411Service _checkpointV0411Service = new();
    private readonly FixedGitHubPublicationV0411Service _fixedGitHubPublicationV0411Service = new();
    private bool _v0411LoadedBootstrapChecked;

    internal void ConfigureV0411Routing()
    {
        // Begin with the complete accepted v0.41 routing, then replace only
        // release-bound handlers and search presentation handlers.
        ConfigureV041Routing();
        Title = "Matawaka Workbench v0.41.1";

        Loaded -= Window_LoadedV041;
        Loaded += Window_LoadedV0411;
        SelfTestButton.Click -= SelfTestV041Button_Click;
        SelfTestButton.Click += SelfTestV0411Button_Click;
        AcceptCheckpointButton.Click -= AcceptCheckpointV041Button_Click;
        AcceptCheckpointButton.Click += AcceptCheckpointV0411Button_Click;
        PublishAcceptedButton.Click -= PublishAcceptedV041Button_Click;
        PublishAcceptedButton.Click += PublishAcceptedV0411Button_Click;

        PreviewKeyDown -= WindowV041_PreviewKeyDown;
        PreviewKeyDown += WindowV0411_PreviewKeyDown;
        OutputTabs.SelectionChanged -= OutputTabsV041_SelectionChanged;
        OutputTabs.SelectionChanged += OutputTabsV0411_SelectionChanged;
        JsonSearchBox.KeyDown -= JsonSearchBox_KeyDown;
        JsonSearchBox.KeyDown += JsonSearchBoxV0411_KeyDown;
        JsonSearchPreviousButton.Click -= JsonSearchPreviousButton_Click;
        JsonSearchPreviousButton.Click += JsonSearchPreviousV0411Button_Click;
        JsonSearchNextButton.Click -= JsonSearchNextButton_Click;
        JsonSearchNextButton.Click += JsonSearchNextV0411Button_Click;
        JsonSearchClearButton.Click -= JsonSearchClearButton_Click;
        JsonSearchClearButton.Click += JsonSearchClearV0411Button_Click;

        JsonSearchPresentationV0411Service.EnableInactiveSelectionHighlight(
            UpdatePlanTextBox,
            LocalAppsTextBox,
            AcceptanceTextBox,
            LifecycleTextBox);
    }

    private async void Window_LoadedV0411(object sender, RoutedEventArgs e)
    {
        if (_v0411LoadedBootstrapChecked) return;
        _v0411LoadedBootstrapChecked = true;
        TransitionBootstrapV040Claim? claim = null;
        var beganRun = false;
        try
        {
            claim = await _transitionBootstrapV040Service.TryClaimFirstBootAsync(
                WorkspaceRootBox.Text,
                LocalCheckpointV0411Service.Version,
                LocalCheckpointV0411Service.TargetTag,
                CancellationToken.None);
            if (claim is null)
            {
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v0411 none; automaticSelfTest=false; automaticAccept=false");
                return;
            }

            SetV035PrimaryControlsEnabled(false);
            BeginRun($"first-boot-bootstrap-v0.41.1-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;
            StatusText.Text = $"RUNNING: v0.41.1 first-boot one-shot Self-test; lease={claim.Lease.LeaseId}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v0411 consuming lease={claim.Lease.LeaseId}; pid={Environment.ProcessId}; retry=false");

            var tested = await RunV0411AcceptanceArtifactAsync(_cts!.Token);
            _lastAcceptanceReceipt = tested.Receipt;
            _lastAcceptanceArtifactPath = tested.ArtifactPath;
            _lastAcceptanceConsumed = false;

            if (!tested.Receipt.Passed)
            {
                await _transitionBootstrapV040Service.MarkFailedNoRetryAsync(
                    claim.Lease, claim.LeasePath, "v0.41.1 first-boot Self-test returned Passed=false", CancellationToken.None);
                _currentTerminalState = CommandTerminalState.Failed;
                StatusText.Text = "FAILED: v0.41.1 first-boot Self-test did not pass; automatic Accept refused; no retry authority";
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

            var checkpointCandidate = await _checkpointV0411Service.PreviewAsync(
                WorkspaceRootBox.Text, tested.ArtifactPath, tested.Receipt, _cts.Token);
            var checkpoint = await _checkpointV0411Service.AcceptFromBootstrapAsync(
                checkpointCandidate, claim.Lease.LeaseId, _cts.Token);
            var checkpointPath = await LocalCheckpointV0411Service.WriteReceiptAsync(
                WorkspaceRootBox.Text, checkpoint, _cts.Token);
            var completed = await _transitionBootstrapV040Service.FinalizeAcceptedAsync(
                claim, tested.ArtifactPath, checkpointPath, _cts.Token);

            _lastAcceptanceConsumed = true;
            AcceptCheckpointButton.IsEnabled = false;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: v0.41.1 first-boot Self-test PASS + automatic local Accept; {checkpoint.Tag} -> {checkpoint.NewHead}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v0411 completed lease={completed.LeaseId}; selfTest=true; accepted=true; publish=false; lifecycle=false");
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Bootstrap = completed,
                BootstrapLeasePath = claim.LeasePath,
                Acceptance = tested.Receipt,
                AcceptanceArtifactPath = tested.ArtifactPath,
                Checkpoint = checkpoint,
                CheckpointReceiptPath = checkpointPath,
                AutomaticSelfTestPerformed = true,
                AutomaticAcceptPerformed = true,
                AutomaticPublishPerformed = false,
                AutomaticLifecyclePerformed = false,
                JsonSearchVisibleSelectionStabilized = true,
                NextExplicitActions = new[] { "Publish accepted", "Lifecycle receipt" }
            });
            OutputTabs.SelectedItem = AcceptanceTab;
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
            if (_lastAcceptanceConsumed) AcceptCheckpointButton.IsEnabled = false;
        }
    }

    private async Task<(WorkbenchAcceptanceReceipt Receipt, string ArtifactPath)> RunV0411AcceptanceArtifactAsync(CancellationToken cancellationToken)
    {
        var context = new RuntimeContext(CatalogRootBox.Text, true, false);
        var receipt = await new WorkbenchV0411AcceptanceHarness(_acceptanceHarness).RunAsync(context, cancellationToken);
        var dir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"v0.41.1-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, CommandCodec.Serialize(receipt), new UTF8Encoding(false), cancellationToken);
        return (receipt, path);
    }

    private async void SelfTestV0411Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveSettings();
            SetV035PrimaryControlsEnabled(false);
            BeginRun($"self-test-v0.41.1-{DateTime.Now:yyyyMMddHHmmss}");
            StatusText.Text = "RUNNING: manual v0.41.1 Self-test; search presentation only, no bootstrap authority created";
            var tested = await RunV0411AcceptanceArtifactAsync(_cts!.Token);
            _lastAcceptanceReceipt = tested.Receipt;
            _lastAcceptanceArtifactPath = tested.ArtifactPath;
            _lastAcceptanceConsumed = false;
            AcceptCheckpointButton.IsEnabled = tested.Receipt.Passed;
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Receipt = tested.Receipt,
                ArtifactPath = tested.ArtifactPath,
                ManualSelfTest = true,
                JsonSearchReadOnly = true,
                JsonSearchVisibleSelectionStabilized = true,
                LocalAppImportAuthorityCreated = false,
                BootstrapLeaseCreated = false,
                AutomaticAcceptPerformed = false,
                LocalCheckpointAvailable = tested.Receipt.Passed
            });
            OutputTabs.SelectedItem = AcceptanceTab;
            ProgressBar.Value = 100;
            _currentTerminalState = tested.Receipt.Passed ? CommandTerminalState.Completed : CommandTerminalState.Failed;
            StatusText.Text = tested.Receipt.Passed ? $"COMPLETED: manual v0.41.1 Self-test PASSED; {tested.ArtifactPath}" : "FAILED: v0.41.1 acceptance matrix has failing checks";
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally
        {
            EndRun();
            SetV035PrimaryControlsEnabled(true);
            AcceptCheckpointButton.IsEnabled = _lastAcceptanceReceipt?.Passed == true && !_lastAcceptanceConsumed &&
                                               _lastAcceptanceReceipt.Version == LocalCheckpointV0411Service.Version;
        }
    }

    private async void AcceptCheckpointV0411Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_lastAcceptanceReceipt is null || !_lastAcceptanceReceipt.Passed ||
                _lastAcceptanceReceipt.Version != LocalCheckpointV0411Service.Version || string.IsNullOrWhiteSpace(_lastAcceptanceArtifactPath))
                throw new InvalidDataException("Run a passing v0.41.1 Self-test before manual Accept.");
            if (_lastAcceptanceConsumed) throw new InvalidDataException("The latest v0.41.1 Self-test receipt has already been consumed.");
            SaveSettings();
            var candidate = await _checkpointV0411Service.PreviewAsync(
                WorkspaceRootBox.Text, _lastAcceptanceArtifactPath, _lastAcceptanceReceipt, CancellationToken.None);
            var preview = $"Создать локальный accepted checkpoint Workbench v0.41.1 вручную?\n\nPredecessor: {candidate.PreviousHead} / {candidate.ExpectedPredecessorTag}\nTarget tag: {candidate.TargetTag}\nAcceptance SHA-256: {candidate.AcceptanceArtifactSha256}\n\nVisible search highlighting is presentation-only. Publish и Lifecycle остаются отдельными решениями.";
            if (MessageBox.Show(this, preview, "Принять Workbench v0.41.1", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            SetV035PrimaryControlsEnabled(false);
            BeginRun($"accept-v0.41.1-{DateTime.Now:yyyyMMddHHmmss}");
            var receipt = await _checkpointV0411Service.AcceptAsync(candidate, _cts!.Token);
            var path = await LocalCheckpointV0411Service.WriteReceiptAsync(WorkspaceRootBox.Text, receipt, _cts.Token);
            _lastAcceptanceConsumed = true;
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Acceptance = _lastAcceptanceReceipt,
                AcceptanceArtifactPath = _lastAcceptanceArtifactPath,
                Checkpoint = receipt,
                CheckpointReceiptPath = path,
                ManualAccept = true,
                BootstrapLeaseConsumed = false,
                NextExplicitActions = new[] { "Publish accepted", "Lifecycle receipt" }
            });
            OutputTabs.SelectedItem = AcceptanceTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: {receipt.Tag} -> {receipt.NewHead}";
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally { EndRun(); SetV035PrimaryControlsEnabled(true); AcceptCheckpointButton.IsEnabled = false; }
    }

    private async void PublishAcceptedV0411Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveSettings();
            var candidate = await _fixedGitHubPublicationV0411Service.PreviewAsync(WorkspaceRootBox.Text, CancellationToken.None);
            var preview = $"Опубликовать принятый Workbench v0.41.1?\n\nRemote: {candidate.RemoteName}\nURL: {candidate.RemoteUrl}\nAccepted HEAD: {candidate.Head}\nParent: {candidate.Parent}\nTag: {candidate.AcceptedTag}\n\nSearch presentation creates no publication authority. Only exact fast-forward/tag, without force/tag movement.";
            if (MessageBox.Show(this, preview, "Publish accepted v0.41.1", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            SetV035PrimaryControlsEnabled(false);
            BeginRun($"publish-v0.41.1-{DateTime.Now:yyyyMMddHHmmss}");
            var receipt = await _fixedGitHubPublicationV0411Service.PublishAsync(candidate, _cts!.Token);
            var path = await FixedGitHubPublicationV0411Service.WriteReceiptAsync(WorkspaceRootBox.Text, receipt, _cts.Token);
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Publication = receipt,
                PublicationReceiptPath = path,
                JsonSearchAuthorityCreated = false,
                LocalAppImportAuthorityCreated = false,
                TransitionBootstrapAuthorityCreated = false,
                AutomaticAcceptAuthorityCreated = false,
                NextExplicitAction = "Lifecycle receipt"
            });
            OutputTabs.SelectedItem = AcceptanceTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: remote main/tag == {receipt.LocalHead}";
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally { EndRun(); SetV035PrimaryControlsEnabled(true); }
    }

    private void WindowV0411_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            JsonSearchBox.Focus();
            JsonSearchBox.SelectAll();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.F3)
        {
            FindInCurrentOutputV0411(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
                ? JsonOutputSearchDirection.Previous
                : JsonOutputSearchDirection.Next);
            e.Handled = true;
        }
    }

    private void JsonSearchBoxV0411_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            FindInCurrentOutputV0411(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
                ? JsonOutputSearchDirection.Previous
                : JsonOutputSearchDirection.Next);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            JsonSearchBox.Clear();
            JsonSearchStatusText.Text = "";
            e.Handled = true;
        }
    }

    private void JsonSearchPreviousV0411Button_Click(object sender, RoutedEventArgs e)
        => FindInCurrentOutputV0411(JsonOutputSearchDirection.Previous);

    private void JsonSearchNextV0411Button_Click(object sender, RoutedEventArgs e)
        => FindInCurrentOutputV0411(JsonOutputSearchDirection.Next);

    private void JsonSearchClearV0411Button_Click(object sender, RoutedEventArgs e)
    {
        JsonSearchBox.Clear();
        JsonSearchStatusText.Text = "";
        JsonSearchBox.Focus();
    }

    private void OutputTabsV0411_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, OutputTabs)) return;
        JsonSearchStatusText.Text = "";
    }

    private void FindInCurrentOutputV0411(JsonOutputSearchDirection direction)
    {
        var target = GetCurrentOutputTextBoxV0411();
        if (target is null)
        {
            JsonSearchStatusText.Text = "Select a text output tab";
            return;
        }
        if (string.IsNullOrEmpty(JsonSearchBox.Text))
        {
            JsonSearchStatusText.Text = "Enter text";
            JsonSearchBox.Focus();
            return;
        }

        var match = JsonOutputSearchV041Service.Find(
            target.Text,
            JsonSearchBox.Text,
            target.SelectionStart,
            target.SelectionLength,
            direction);
        if (match is null)
        {
            JsonSearchStatusText.Text = "0 / 0";
            JsonSearchBox.Focus();
            return;
        }

        JsonSearchPresentationV0411Service.PresentMatch(target, match);
        JsonSearchStatusText.Text = $"{match.Ordinal} / {match.Total}" + (match.Wrapped ? " ↻" : "");
        JsonSearchBox.Focus();
    }

    private TextBox? GetCurrentOutputTextBoxV0411()
    {
        if (OutputTabs.SelectedItem is not TabItem tab) return null;
        return tab.Content as TextBox;
    }
}
