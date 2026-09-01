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
    private readonly LocalCheckpointV041Service _checkpointV041Service = new();
    private readonly FixedGitHubPublicationV041Service _fixedGitHubPublicationV041Service = new();
    private bool _v041LoadedBootstrapChecked;

    internal void ConfigureV041Routing()
    {
        Title = "Matawaka Workbench v0.41.0";

        // Preserve accepted v0.40 Update Workbench / v0.39 launch and Local Apps handlers.
        // Only release-bound startup, acceptance, publication and read-only search routing change.
        Loaded -= Window_LoadedV040;
        Loaded += Window_LoadedV041;
        SelfTestButton.Click -= SelfTestV040Button_Click;
        SelfTestButton.Click += SelfTestV041Button_Click;
        AcceptCheckpointButton.Click -= AcceptCheckpointV040Button_Click;
        AcceptCheckpointButton.Click += AcceptCheckpointV041Button_Click;
        PublishAcceptedButton.Click -= PublishAcceptedV040Button_Click;
        PublishAcceptedButton.Click += PublishAcceptedV041Button_Click;
        PreviewKeyDown += WindowV041_PreviewKeyDown;
        OutputTabs.SelectionChanged += OutputTabsV041_SelectionChanged;
    }

    private async void Window_LoadedV041(object sender, RoutedEventArgs e)
    {
        if (_v041LoadedBootstrapChecked) return;
        _v041LoadedBootstrapChecked = true;
        TransitionBootstrapV040Claim? claim = null;
        var beganRun = false;
        try
        {
            claim = await _transitionBootstrapV040Service.TryClaimFirstBootAsync(
                WorkspaceRootBox.Text,
                LocalCheckpointV041Service.Version,
                LocalCheckpointV041Service.TargetTag,
                CancellationToken.None);
            if (claim is null)
            {
                EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v041 none; automaticSelfTest=false; automaticAccept=false");
                return;
            }

            SetV035PrimaryControlsEnabled(false);
            BeginRun($"first-boot-bootstrap-v0.41-{DateTime.Now:yyyyMMddHHmmss}");
            beganRun = true;
            StatusText.Text = $"RUNNING: v0.41 first-boot one-shot Self-test; lease={claim.Lease.LeaseId}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v041 consuming lease={claim.Lease.LeaseId}; pid={Environment.ProcessId}; retry=false");

            var tested = await RunV041AcceptanceArtifactAsync(_cts!.Token);
            _lastAcceptanceReceipt = tested.Receipt;
            _lastAcceptanceArtifactPath = tested.ArtifactPath;
            _lastAcceptanceConsumed = false;

            if (!tested.Receipt.Passed)
            {
                await _transitionBootstrapV040Service.MarkFailedNoRetryAsync(
                    claim.Lease, claim.LeasePath, "v0.41 first-boot Self-test returned Passed=false", CancellationToken.None);
                _currentTerminalState = CommandTerminalState.Failed;
                StatusText.Text = "FAILED: v0.41 first-boot Self-test did not pass; automatic Accept refused; no retry authority";
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

            var checkpointCandidate = await _checkpointV041Service.PreviewAsync(
                WorkspaceRootBox.Text, tested.ArtifactPath, tested.Receipt, _cts.Token);
            var checkpoint = await _checkpointV041Service.AcceptFromBootstrapAsync(
                checkpointCandidate, claim.Lease.LeaseId, _cts.Token);
            var checkpointPath = await LocalCheckpointV041Service.WriteReceiptAsync(
                WorkspaceRootBox.Text, checkpoint, _cts.Token);
            var completed = await _transitionBootstrapV040Service.FinalizeAcceptedAsync(
                claim, tested.ArtifactPath, checkpointPath, _cts.Token);

            _lastAcceptanceConsumed = true;
            AcceptCheckpointButton.IsEnabled = false;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: v0.41 first-boot Self-test PASS + automatic local Accept; {checkpoint.Tag} -> {checkpoint.NewHead}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transition-bootstrap.v041 completed lease={completed.LeaseId}; selfTest=true; accepted=true; publish=false; lifecycle=false");
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

    private async Task<(WorkbenchAcceptanceReceipt Receipt, string ArtifactPath)> RunV041AcceptanceArtifactAsync(CancellationToken cancellationToken)
    {
        var context = new RuntimeContext(CatalogRootBox.Text, true, false);
        var receipt = await new WorkbenchV041AcceptanceHarness(_acceptanceHarness).RunAsync(context, cancellationToken);
        var dir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"v0.41-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, CommandCodec.Serialize(receipt), new UTF8Encoding(false), cancellationToken);
        return (receipt, path);
    }

    private async void SelfTestV041Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveSettings();
            SetV035PrimaryControlsEnabled(false);
            BeginRun($"self-test-v0.41-{DateTime.Now:yyyyMMddHHmmss}");
            StatusText.Text = "RUNNING: manual v0.41 Self-test; no bootstrap authority created";
            var tested = await RunV041AcceptanceArtifactAsync(_cts!.Token);
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
                LocalAppImportAuthorityCreated = false,
                BootstrapLeaseCreated = false,
                AutomaticAcceptPerformed = false,
                LocalCheckpointAvailable = tested.Receipt.Passed
            });
            OutputTabs.SelectedItem = AcceptanceTab;
            ProgressBar.Value = 100;
            _currentTerminalState = tested.Receipt.Passed ? CommandTerminalState.Completed : CommandTerminalState.Failed;
            StatusText.Text = tested.Receipt.Passed ? $"COMPLETED: manual v0.41 Self-test PASSED; {tested.ArtifactPath}" : "FAILED: v0.41 acceptance matrix has failing checks";
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally
        {
            EndRun();
            SetV035PrimaryControlsEnabled(true);
            AcceptCheckpointButton.IsEnabled = _lastAcceptanceReceipt?.Passed == true && !_lastAcceptanceConsumed &&
                                               _lastAcceptanceReceipt.Version == LocalCheckpointV041Service.Version;
        }
    }

    private async void AcceptCheckpointV041Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_lastAcceptanceReceipt is null || !_lastAcceptanceReceipt.Passed ||
                _lastAcceptanceReceipt.Version != LocalCheckpointV041Service.Version || string.IsNullOrWhiteSpace(_lastAcceptanceArtifactPath))
                throw new InvalidDataException("Run a passing v0.41 Self-test before manual Accept.");
            if (_lastAcceptanceConsumed) throw new InvalidDataException("The latest v0.41 Self-test receipt has already been consumed.");
            SaveSettings();
            var candidate = await _checkpointV041Service.PreviewAsync(
                WorkspaceRootBox.Text, _lastAcceptanceArtifactPath, _lastAcceptanceReceipt, CancellationToken.None);
            var preview = $"Создать локальный accepted checkpoint Workbench v0.41 вручную?\n\nPredecessor: {candidate.PreviousHead} / {candidate.ExpectedPredecessorTag}\nTarget tag: {candidate.TargetTag}\nAcceptance SHA-256: {candidate.AcceptanceArtifactSha256}\n\nJSON search is read-only navigation; chat handoff guidance creates no import authority. Publish и Lifecycle остаются отдельными решениями.";
            if (MessageBox.Show(this, preview, "Принять Workbench v0.41", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            SetV035PrimaryControlsEnabled(false);
            BeginRun($"accept-v0.41-{DateTime.Now:yyyyMMddHHmmss}");
            var receipt = await _checkpointV041Service.AcceptAsync(candidate, _cts!.Token);
            var path = await LocalCheckpointV041Service.WriteReceiptAsync(WorkspaceRootBox.Text, receipt, _cts.Token);
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

    private async void PublishAcceptedV041Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveSettings();
            var candidate = await _fixedGitHubPublicationV041Service.PreviewAsync(WorkspaceRootBox.Text, CancellationToken.None);
            var preview = $"Опубликовать принятый Workbench v0.41?\n\nRemote: {candidate.RemoteName}\nURL: {candidate.RemoteUrl}\nAccepted HEAD: {candidate.Head}\nParent: {candidate.Parent}\nTag: {candidate.AcceptedTag}\n\nSearch/handoff guidance create no publication authority. Only exact fast-forward/tag; no force/tag movement.";
            if (MessageBox.Show(this, preview, "Publish accepted v0.41", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            SetV035PrimaryControlsEnabled(false);
            BeginRun($"publish-v0.41-{DateTime.Now:yyyyMMddHHmmss}");
            var receipt = await _fixedGitHubPublicationV041Service.PublishAsync(candidate, _cts!.Token);
            var path = await FixedGitHubPublicationV041Service.WriteReceiptAsync(WorkspaceRootBox.Text, receipt, _cts.Token);
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

    private void WindowV041_PreviewKeyDown(object sender, KeyEventArgs e)
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
            FindInCurrentOutput(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
                ? JsonOutputSearchDirection.Previous
                : JsonOutputSearchDirection.Next);
            e.Handled = true;
        }
    }

    private void JsonSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            FindInCurrentOutput(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
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

    private void JsonSearchPreviousButton_Click(object sender, RoutedEventArgs e)
        => FindInCurrentOutput(JsonOutputSearchDirection.Previous);

    private void JsonSearchNextButton_Click(object sender, RoutedEventArgs e)
        => FindInCurrentOutput(JsonOutputSearchDirection.Next);

    private void JsonSearchClearButton_Click(object sender, RoutedEventArgs e)
    {
        JsonSearchBox.Clear();
        JsonSearchStatusText.Text = "";
        JsonSearchBox.Focus();
    }

    private void OutputTabsV041_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, OutputTabs)) return;
        JsonSearchStatusText.Text = "";
    }

    private void FindInCurrentOutput(JsonOutputSearchDirection direction)
    {
        var target = GetCurrentOutputTextBox();
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

        target.Select(match.Start, match.Length);
        target.CaretIndex = match.Start + match.Length;
        var line = target.GetLineIndexFromCharacterIndex(match.Start);
        if (line >= 0) target.ScrollToLine(line);
        JsonSearchStatusText.Text = $"{match.Ordinal} / {match.Total}" + (match.Wrapped ? " ↻" : "");
        JsonSearchBox.Focus();
    }

    private TextBox? GetCurrentOutputTextBox()
    {
        if (OutputTabs.SelectedItem is not TabItem tab) return null;
        return tab.Content as TextBox;
    }
}
