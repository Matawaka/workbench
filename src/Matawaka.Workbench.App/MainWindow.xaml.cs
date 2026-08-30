using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using Matawaka.Workbench.Protocol;
using Matawaka.Workbench.Runtime;
using Microsoft.Win32;

namespace Matawaka.Workbench.App;

public partial class MainWindow : Window
{
    private readonly ICommandRunner _router = new CommandRouter();
    private CancellationTokenSource? _cts;
    private WorkbenchProgressReceipt? _lastProgressReceipt;
    private CommandTerminalState? _currentTerminalState;
    private int _runEpoch;

    public MainWindow()
    {
        InitializeComponent();
        var settings = WorkbenchSettingsStore.Load();
        WorkspaceRootBox.Text = settings.WorkspaceRoot;
        CatalogRootBox.Text = settings.CatalogRoot;
        JsonTextBox.Text = DefaultCommand;
    }

    private void PasteButton_Click(object sender, RoutedEventArgs e)
    {
        if (Clipboard.ContainsText()) JsonTextBox.Text = Clipboard.GetText();
    }

    private async void FileButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "JSON (*.json)|*.json|Все файлы (*.*)|*.*" };
        if (dialog.ShowDialog(this) == true)
            JsonTextBox.Text = await File.ReadAllTextAsync(dialog.FileName, Encoding.UTF8);
    }

    private void ValidateButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var command = CommandCodec.Parse(JsonTextBox.Text);
            Log(new WorkbenchProgress(command.Id, "command.valid", 0, $"{command.Kind} -> {command.Target}", DateTimeOffset.Now));
            StatusText.Text = "VALID";
        }
        catch (JsonException ex)
        {
            ShowInvalid(ex);
        }
        catch (InvalidDataException ex)
        {
            ShowInvalid(ex);
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
    }

    private async void RunButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var command = CommandCodec.Parse(JsonTextBox.Text);
            await RunCommandAsync(command);
        }
        catch (JsonException ex)
        {
            ShowInvalid(ex);
        }
        catch (InvalidDataException ex)
        {
            ShowInvalid(ex);
        }
        catch (OperationCanceledException)
        {
            ShowCancelled();
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
    }

    private async Task RunCommandAsync(CommandEnvelope command)
    {
        SaveSettings();
        BeginRun(command.Id);
        var progress = new Progress<WorkbenchProgress>(Log);
        try
        {
            var context = new RuntimeContext(
                CatalogRootBox.Text,
                AgentEnabledBox.IsChecked == true,
                AllowGitFetchBox.IsChecked == true);

            var result = await _router.RunAsync(command, context, progress, _cts!.Token);
            RenderResult(result);
            ApplyTerminalState(result.TerminalState, result.Summary);
        }
        finally
        {
            EndRun();
        }
    }

    private async void InspectCatalogButton_Click(object sender, RoutedEventArgs e)
    {
        var command = new CommandEnvelope
        {
            Schema = "matawaka.command/v1",
            Id = $"catalog-{DateTime.Now:yyyyMMddHHmmss}",
            Kind = "catalog.inspect",
            Target = "Matawaka"
        };

        try
        {
            await RunCommandAsync(command);
        }
        catch (OperationCanceledException)
        {
            ShowCancelled();
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
    }

    private async void FetchCatalogButton_Click(object sender, RoutedEventArgs e)
    {
        var command = new CommandEnvelope
        {
            Schema = "matawaka.command/v1",
            Id = $"fetch-{DateTime.Now:yyyyMMddHHmmss}",
            Kind = "catalog.fetch",
            Target = "Matawaka"
        };

        try
        {
            await RunCommandAsync(command);
        }
        catch (UnauthorizedAccessException ex)
        {
            ShowDenied(ex.Message);
        }
        catch (OperationCanceledException)
        {
            ShowCancelled();
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
    }

    private void SaveWorkspaceButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(WorkspaceRootBox.Text))
                throw new InvalidDataException("Рабочее пространство не задано.");

            if (string.IsNullOrWhiteSpace(CatalogRootBox.Text))
                CatalogRootBox.Text = Path.Combine(WorkspaceRootBox.Text, "Catalog");

            SaveSettings();
            StatusText.Text = $"COMPLETED: настройки сохранены — {WorkbenchSettingsStore.SettingsPath}";
        }
        catch (InvalidDataException ex)
        {
            ShowInvalid(ex);
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        try
        {
            SaveSettings();
        }
        catch
        {
            // Closing must not be blocked by a settings-write failure.
        }
    }

    private void SaveSettings()
    {
        WorkbenchSettingsStore.Save(new WorkbenchSettings(
            WorkspaceRootBox.Text.Trim(),
            CatalogRootBox.Text.Trim()));
    }

    private void BeginRun(string id)
    {
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        RunButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        ProgressBar.Value = 0;
        StatusText.Text = $"RUNNING: {id}";
        ResultTextBox.Clear();
        EvidenceTextBox.Clear();
        AuthorityTextBox.Clear();
        LivenessTextBox.Clear();
        SemanticTextBox.Clear();
        AgentTextBox.Clear();
        _lastProgressReceipt = null;
        _currentTerminalState = null;
        _runEpoch++;
    }

    private void EndRun()
    {
        RunButton.IsEnabled = true;
        CancelButton.IsEnabled = false;
    }

    private void Log(WorkbenchProgress e)
    {
        ProgressBar.Value = Math.Clamp(e.Percent, 0, 100);
        var line = $"{e.Timestamp:HH:mm:ss}  {e.Percent,3}%  {e.Event,-28} {e.Message}";
        EventList.Items.Add(line);
        if (EventList.Items.Count > 0)
            EventList.ScrollIntoView(EventList.Items[EventList.Items.Count - 1]);

        if (e.Event.Equals("command.completed", StringComparison.OrdinalIgnoreCase))
            _currentTerminalState = CommandTerminalState.Completed;
        else if (e.Event.Equals("command.denied", StringComparison.OrdinalIgnoreCase) ||
                 e.Event.Equals("agent.denied", StringComparison.OrdinalIgnoreCase))
            _currentTerminalState = CommandTerminalState.Denied;

        if (PclCompatibleProgress.IsTrackable(e))
        {
            _lastProgressReceipt = PclCompatibleProgress.Create(e, _lastProgressReceipt, _runEpoch);
            RenderLiveness(_currentTerminalState);
        }

        if (e.Event.StartsWith("authority.", StringComparison.OrdinalIgnoreCase))
        {
            AuthorityTextBox.AppendText(line + Environment.NewLine);
            AuthorityTextBox.ScrollToEnd();
        }
        else if (e.Event.StartsWith("semantic.", StringComparison.OrdinalIgnoreCase))
        {
            SemanticTextBox.AppendText(line + Environment.NewLine);
            SemanticTextBox.ScrollToEnd();
            AgentTextBox.AppendText(line + Environment.NewLine);
            AgentTextBox.ScrollToEnd();
        }
        else if (e.Event.StartsWith("agent.", StringComparison.OrdinalIgnoreCase))
        {
            AgentTextBox.AppendText(line + Environment.NewLine);
            AgentTextBox.ScrollToEnd();
        }
    }

    private void RenderResult(CommandResult result)
    {
        ResultTextBox.Text = result.Data is null
            ? result.Summary
            : CommandCodec.Serialize(result.Data);

        EvidenceTextBox.Text = result.Evidence is null
            ? "No evidence receipt for this command."
            : CommandCodec.Serialize(result.Evidence);

        AuthorityTextBox.Text = result.Authority is null
            ? "No authority receipt for this command."
            : CommandCodec.Serialize(result.Authority);

        SemanticTextBox.Text = result.Semantic is null
            ? "No semantic-provider receipt for this command."
            : CommandCodec.Serialize(result.Semantic);

        if (result.Agent is not null)
        {
            if (AgentTextBox.Text.Length > 0)
                AgentTextBox.AppendText(Environment.NewLine);
            AgentTextBox.AppendText("--- RECEIPT ---" + Environment.NewLine);
            AgentTextBox.AppendText(CommandCodec.Serialize(result.Agent));
        }

        OutputTabs.SelectedItem = result.TerminalState switch
        {
            CommandTerminalState.Denied when result.Authority is not null => AuthorityTab,
            CommandTerminalState.Completed when result.Semantic is not null => SemanticTab,
            CommandTerminalState.Completed when result.Evidence is not null => EvidenceTab,
            _ => ResultTab
        };
    }

    private void ApplyTerminalState(CommandTerminalState state, string summary)
    {
        ProgressBar.Value = 100;
        _currentTerminalState = state;
        StatusText.Text = $"{state.ToString().ToUpperInvariant()}: {summary}";
        RenderLiveness(state);
    }

    private void RenderLiveness(CommandTerminalState? terminalState)
    {
        if (_lastProgressReceipt is null)
        {
            LivenessTextBox.Text = "No PCL-compatible progress receipt for this run.";
            return;
        }

        var view = PclCompatibleProgress.ToHumanView(_lastProgressReceipt, terminalState);
        LivenessTextBox.Text = CommandCodec.Serialize(new
        {
            ProgressReceipt = _lastProgressReceipt,
            HumanView = view,
            Note = "Workbench v0.3 compatibility projection; bound to exact UU-AAP source frontier, canonical JavaScript implementation not executed."
        });
    }

    private void ShowDenied(string message)
    {
        ApplyTerminalState(CommandTerminalState.Denied, message);
        EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  command.denied               {message}");
        OutputTabs.SelectedItem = EventsTab;
        EndRun();
    }

    private void ShowInvalid(Exception ex)
    {
        ProgressBar.Value = 0;
        _currentTerminalState = CommandTerminalState.Invalid;
        StatusText.Text = $"INVALID: {ex.Message}";
        if (_lastProgressReceipt is not null) RenderLiveness(CommandTerminalState.Invalid);
        EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  command.invalid              {ex.Message}");
        OutputTabs.SelectedItem = EventsTab;
        EndRun();
    }

    private void ShowFailure(Exception ex)
    {
        _currentTerminalState = CommandTerminalState.Failed;
        StatusText.Text = $"FAILED: {ex.Message}";
        if (_lastProgressReceipt is not null) RenderLiveness(CommandTerminalState.Failed);
        EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  command.failed               {ex.Message}");
        OutputTabs.SelectedItem = EventsTab;
        EndRun();
    }

    private void ShowCancelled()
    {
        _currentTerminalState = CommandTerminalState.Cancelled;
        StatusText.Text = "CANCELLED";
        if (_lastProgressReceipt is not null) RenderLiveness(CommandTerminalState.Cancelled);
        EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  command.cancelled");
        OutputTabs.SelectedItem = EventsTab;
        EndRun();
    }

    private const string DefaultCommand = """
{
  "schema": "matawaka.command/v1",
  "id": "game-companion-propose-v030",
  "kind": "agent.run",
  "target": "game-intellectual-companion",
  "policyProfile": "uu-aap-bridge-v0",
  "payload": {
    "mode": "propose",
    "semanticProvider": "local-contract-synthesis-v0.3",
    "mutationBudget": 0,
    "networkAccess": false,
    "arbitraryProcessExecution": false,
    "focusRepositories": ["FREESHIELD", "kontur", "uu-aap"],
    "terms": [
      "authority",
      "capability",
      "evidence",
      "receipt",
      "intent",
      "availability",
      "possibility",
      "companion",
      "solver",
      "hint",
      "attention",
      "reversible"
    ],
    "maxFilesPerRepository": 160,
    "maxEvidenceItems": 80
  }
}
""";
}
