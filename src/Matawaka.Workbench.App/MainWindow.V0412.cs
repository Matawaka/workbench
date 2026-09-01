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
    private readonly LocalCheckpointV0412Service _checkpointV0412Service = new();
    private readonly FixedGitHubPublicationV0412Service _fixedGitHubPublicationV0412Service = new();
    private bool _v0412LoadedBootstrapChecked;

    internal void ConfigureV0412Routing()
    {
        // Install the complete v0.41.1 successor routing first, then replace only
        // release-bound handlers and search presentation with the v0.41.2 repair.
        ConfigureV0411Routing();
        Title = "Matawaka Workbench v0.41.2";

        Loaded -= Window_LoadedV0411;
        Loaded += Window_LoadedV0412;
        SelfTestButton.Click -= SelfTestV0411Button_Click;
        SelfTestButton.Click += SelfTestV0412Button_Click;
        AcceptCheckpointButton.Click -= AcceptCheckpointV0411Button_Click;
        AcceptCheckpointButton.Click += AcceptCheckpointV0412Button_Click;
        PublishAcceptedButton.Click -= PublishAcceptedV0411Button_Click;
        PublishAcceptedButton.Click += PublishAcceptedV0412Button_Click;

        PreviewKeyDown -= WindowV0411_PreviewKeyDown;
        PreviewKeyDown += WindowV0412_PreviewKeyDown;
        OutputTabs.SelectionChanged -= OutputTabsV0411_SelectionChanged;
        OutputTabs.SelectionChanged += OutputTabsV0412_SelectionChanged;
        JsonSearchBox.KeyDown -= JsonSearchBoxV0411_KeyDown;
        JsonSearchBox.KeyDown += JsonSearchBoxV0412_KeyDown;
        JsonSearchPreviousButton.Click -= JsonSearchPreviousV0411Button_Click;
        JsonSearchPreviousButton.Click += JsonSearchPreviousV0412Button_Click;
        JsonSearchNextButton.Click -= JsonSearchNextV0411Button_Click;
        JsonSearchNextButton.Click += JsonSearchNextV0412Button_Click;
        JsonSearchClearButton.Click -= JsonSearchClearV0411Button_Click;
        JsonSearchClearButton.Click += JsonSearchClearV0412Button_Click;

        JsonSearchPresentationV0412Service.ConfigureVisibleInactiveSelection(
            UpdatePlanTextBox,
            LocalAppsTextBox,
            AcceptanceTextBox,
            LifecycleTextBox);
    }

    private async void Window_LoadedV0412(object sender, RoutedEventArgs e)
    {
        if (_v0412LoadedBootstrapChecked) return;
        _v0412LoadedBootstrapChecked = true;
        TransitionBootstrapV040Claim? claim = null;
        var beganRun = false;
        try
        {
            claim = await _transitionBootstrapV040Service.TryClaimFirstBootAsync(
                WorkspaceRootBox.Text,
                LocalCheckpointV0412Service.Version,
                LocalCheckpointV0412Service.TargetTag,
                CancellationToken.None);
            if (claim is null)
            {
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v0412 none; automaticSelfTest=false; automaticAccept=false");
                return;
            }

            SetV035PrimaryControlsEnabled(false);
            BeginRun($"first-boot-bootstrap-v0.41.2-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;
            StatusText.Text = $"RUNNING: v0.41.2 first-boot one-shot Self-test; lease={claim.Lease.LeaseId}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v0412 consuming lease={claim.Lease.LeaseId}; pid={Environment.ProcessId}; retry=false");

            var tested = await RunV0412AcceptanceArtifactAsync(_cts!.Token);
            _lastAcceptanceReceipt = tested.Receipt;
            _lastAcceptanceArtifactPath = tested.ArtifactPath;
            _lastAcceptanceConsumed = false;

            if (!tested.Receipt.Passed)
            {
                await _transitionBootstrapV040Service.MarkFailedNoRetryAsync(
                    claim.Lease, claim.LeasePath, "v0.41.2 first-boot Self-test returned Passed=false", CancellationToken.None);
                _currentTerminalState = CommandTerminalState.Failed;
                StatusText.Text = "FAILED: v0.41.2 first-boot Self-test did not pass; automatic Accept refused; no retry authority";
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

            var checkpointCandidate = await _checkpointV0412Service.PreviewAsync(
                WorkspaceRootBox.Text, tested.ArtifactPath, tested.Receipt, _cts.Token);
            var checkpoint = await _checkpointV0412Service.AcceptFromBootstrapAsync(
                checkpointCandidate, claim.Lease.LeaseId, _cts.Token);
            var checkpointPath = await LocalCheckpointV0412Service.WriteReceiptAsync(
                WorkspaceRootBox.Text, checkpoint, _cts.Token);
            var completed = await _transitionBootstrapV040Service.FinalizeAcceptedAsync(
                claim, tested.ArtifactPath, checkpointPath, _cts.Token);

            _lastAcceptanceConsumed = true;
            AcceptCheckpointButton.IsEnabled = false;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: v0.41.2 first-boot Self-test PASS + automatic local Accept; {checkpoint.Tag} -> {checkpoint.NewHead}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v0412 completed lease={completed.LeaseId}; selfTest=true; accepted=true; publish=false; lifecycle=false");
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
                JsonSearchFocusPrimedSelection = true,
                RealHostVisualQualificationRequiredBeforePublication = true,
                NextExplicitActions = new[] { "Real-host visible search check", "Publish accepted", "Lifecycle receipt" }
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

    private async Task<(WorkbenchAcceptanceReceipt Receipt, string ArtifactPath)> RunV0412AcceptanceArtifactAsync(CancellationToken cancellationToken)
    {
        var context = new RuntimeContext(CatalogRootBox.Text, true, false);
        var receipt = await new WorkbenchV0412AcceptanceHarness(_acceptanceHarness).RunAsync(context, cancellationToken);
        var dir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"v0.41.2-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, CommandCodec.Serialize(receipt), new UTF8Encoding(false), cancellationToken);
        return (receipt, path);
    }

    private async void SelfTestV0412Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveSettings();
            SetV035PrimaryControlsEnabled(false);
            BeginRun($"self-test-v0.41.2-{DateTime.Now:yyyyMMddHHmmss}");
            StatusText.Text = "RUNNING: manual v0.41.2 Self-test; focus-primed search presentation, no bootstrap authority created";
            var tested = await RunV0412AcceptanceArtifactAsync(_cts!.Token);
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
                JsonSearchFocusPrimedSelection = true,
                RealHostVisualQualificationRequiredBeforePublication = true,
                LocalAppImportAuthorityCreated = false,
                BootstrapLeaseCreated = false,
                AutomaticAcceptPerformed = false,
                LocalCheckpointAvailable = tested.Receipt.Passed
            });
            OutputTabs.SelectedItem = AcceptanceTab;
            ProgressBar.Value = 100;
            _currentTerminalState = tested.Receipt.Passed ? CommandTerminalState.Completed : CommandTerminalState.Failed;
            StatusText.Text = tested.Receipt.Passed ? $"COMPLETED: manual v0.41.2 Self-test PASSED; {tested.ArtifactPath}" : "FAILED: v0.41.2 acceptance matrix has failing checks";
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally
        {
            EndRun();
            SetV035PrimaryControlsEnabled(true);
            AcceptCheckpointButton.IsEnabled = _lastAcceptanceReceipt?.Passed == true && !_lastAcceptanceConsumed &&
                                               _lastAcceptanceReceipt.Version == LocalCheckpointV0412Service.Version;
        }
    }

    private async void AcceptCheckpointV0412Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_lastAcceptanceReceipt is null || !_lastAcceptanceReceipt.Passed ||
                _lastAcceptanceReceipt.Version != LocalCheckpointV0412Service.Version || string.IsNullOrWhiteSpace(_lastAcceptanceArtifactPath))
                throw new InvalidDataException("Run a passing v0.41.2 Self-test before manual Accept.");
            if (_lastAcceptanceConsumed) throw new InvalidDataException("The latest v0.41.2 Self-test receipt has already been consumed.");
            SaveSettings();
            var candidate = await _checkpointV0412Service.PreviewAsync(
                WorkspaceRootBox.Text, _lastAcceptanceArtifactPath, _lastAcceptanceReceipt, CancellationToken.None);
            var preview = $"Создать локальный accepted checkpoint Workbench v0.41.2 вручную?\n\nPredecessor: {candidate.PreviousHead} / {candidate.ExpectedPredecessorTag}\nTarget tag: {candidate.TargetTag}\nAcceptance SHA-256: {candidate.AcceptanceArtifactSha256}\n\nFocus-primed visible search is presentation-only. После Accept требуется реальная визуальная проверка; Publish и Lifecycle остаются отдельными решениями.";
            if (MessageBox.Show(this, preview, "Принять Workbench v0.41.2", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            SetV035PrimaryControlsEnabled(false);
            BeginRun($"accept-v0.41.2-{DateTime.Now:yyyyMMddHHmmss}");
            var receipt = await _checkpointV0412Service.AcceptAsync(candidate, _cts!.Token);
            var path = await LocalCheckpointV0412Service.WriteReceiptAsync(WorkspaceRootBox.Text, receipt, _cts.Token);
            _lastAcceptanceConsumed = true;
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Acceptance = _lastAcceptanceReceipt,
                AcceptanceArtifactPath = _lastAcceptanceArtifactPath,
                Checkpoint = receipt,
                CheckpointReceiptPath = path,
                ManualAccept = true,
                BootstrapLeaseConsumed = false,
                RealHostVisualQualificationRequiredBeforePublication = true,
                NextExplicitActions = new[] { "Real-host visible search check", "Publish accepted", "Lifecycle receipt" }
            });
            OutputTabs.SelectedItem = AcceptanceTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: {receipt.Tag} -> {receipt.NewHead}; visual search qualification still required before Publish";
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally { EndRun(); SetV035PrimaryControlsEnabled(true); AcceptCheckpointButton.IsEnabled = false; }
    }

    private async void PublishAcceptedV0412Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveSettings();
            var candidate = await _fixedGitHubPublicationV0412Service.PreviewAsync(WorkspaceRootBox.Text, CancellationToken.None);
            var preview = $"Опубликовать принятый Workbench v0.41.2?\n\nRemote: {candidate.RemoteName}\nURL: {candidate.RemoteUrl}\nAccepted HEAD: {candidate.Head}\nLocal predecessor: {candidate.Parent} / {FixedGitHubPublicationV0412Service.LocalPredecessorTag}\nRemote base: {FixedGitHubPublicationV0412Service.ExpectedRemoteBase} / {FixedGitHubPublicationV0412Service.RemoteBaseTag}\nTag: {candidate.AcceptedTag}\n\nНажимайте Yes только после реальной визуальной проверки: найденный текст остаётся подсвеченным при фокусе в строке поиска. Failed v0.41.1 tag не публикуется. Только exact fast-forward + v0.41.2 tag, без force/tag movement.";
            if (MessageBox.Show(this, preview, "Publish accepted v0.41.2", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            SetV035PrimaryControlsEnabled(false);
            BeginRun($"publish-v0.41.2-{DateTime.Now:yyyyMMddHHmmss}");
            var receipt = await _fixedGitHubPublicationV0412Service.PublishAsync(candidate, _cts!.Token);
            var path = await FixedGitHubPublicationV0412Service.WriteReceiptAsync(WorkspaceRootBox.Text, receipt, _cts.Token);
            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Publication = receipt,
                PublicationReceiptPath = path,
                FailedV0411TagPublished = false,
                JsonSearchAuthorityCreated = false,
                LocalAppImportAuthorityCreated = false,
                TransitionBootstrapAuthorityCreated = false,
                AutomaticAcceptAuthorityCreated = false,
                NextExplicitAction = "Lifecycle receipt"
            });
            OutputTabs.SelectedItem = AcceptanceTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: remote main/v0.41.2 tag == {receipt.LocalHead}; v0.41.1 tag remains unpublished";
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally { EndRun(); SetV035PrimaryControlsEnabled(true); }
    }

    private void WindowV0412_PreviewKeyDown(object sender, KeyEventArgs e)
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
            FindInCurrentOutputV0412(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
                ? JsonOutputSearchDirection.Previous
                : JsonOutputSearchDirection.Next);
            e.Handled = true;
        }
    }

    private void JsonSearchBoxV0412_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            FindInCurrentOutputV0412(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
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

    private void JsonSearchPreviousV0412Button_Click(object sender, RoutedEventArgs e)
        => FindInCurrentOutputV0412(JsonOutputSearchDirection.Previous);

    private void JsonSearchNextV0412Button_Click(object sender, RoutedEventArgs e)
        => FindInCurrentOutputV0412(JsonOutputSearchDirection.Next);

    private void JsonSearchClearV0412Button_Click(object sender, RoutedEventArgs e)
    {
        JsonSearchBox.Clear();
        JsonSearchStatusText.Text = "";
        JsonSearchBox.Focus();
    }

    private void OutputTabsV0412_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, OutputTabs)) return;
        JsonSearchStatusText.Text = "";
    }

    private void FindInCurrentOutputV0412(JsonOutputSearchDirection direction)
    {
        var target = GetCurrentOutputTextBoxV0412();
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

        var presentation = JsonSearchPresentationV0412Service.PresentMatch(target, JsonSearchBox, match);
        JsonSearchStatusText.Text = $"{match.Ordinal} / {match.Total}" + (match.Wrapped ? " ↻" : "");
        if (!presentation.SearchFocusRestored || !presentation.OutputUnchanged)
            throw new InvalidOperationException("v0.41.2 search presentation contract was not preserved.");
    }

    private TextBox? GetCurrentOutputTextBoxV0412()
    {
        if (OutputTabs.SelectedItem is not TabItem tab) return null;
        return tab.Content as TextBox;
    }
}
