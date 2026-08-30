using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using Matawaka.Workbench.Protocol;
using Matawaka.Workbench.Runtime;
using Microsoft.Win32;

namespace Matawaka.Workbench.App;

public partial class MainWindow : Window
{
    private readonly ICommandRunner _router = new CommandRouter();
    private CancellationTokenSource? _cts;

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
            StatusText.Text = "JSON корректен";
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
        catch (OperationCanceledException)
        {
            StatusText.Text = "Остановлено";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  command.cancelled");
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
            StatusText.Text = result.Summary;
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
        catch (OperationCanceledException)
        {
            StatusText.Text = "Остановлено";
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
            StatusText.Text = $"Настройки сохранены: {WorkbenchSettingsStore.SettingsPath}";
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
        StatusText.Text = $"Выполняется: {id}";
        ResultTextBox.Clear();
        EvidenceTextBox.Clear();
        AgentTextBox.Clear();
    }

    private void EndRun()
    {
        RunButton.IsEnabled = true;
        CancelButton.IsEnabled = false;
    }

    private void Log(WorkbenchProgress e)
    {
        ProgressBar.Value = Math.Clamp(e.Percent, 0, 100);
        StatusText.Text = e.Message;
        var line = $"{e.Timestamp:HH:mm:ss}  {e.Percent,3}%  {e.Event,-28} {e.Message}";
        EventList.Items.Add(line);
        if (EventList.Items.Count > 0)
            EventList.ScrollIntoView(EventList.Items[EventList.Items.Count - 1]);

        if (e.Event.StartsWith("agent.", StringComparison.OrdinalIgnoreCase) ||
            e.Event.StartsWith("authority.", StringComparison.OrdinalIgnoreCase))
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
            ? "No evidence payload for this command."
            : CommandCodec.Serialize(result.Evidence);

        if (result.Agent is not null)
        {
            AgentTextBox.AppendText(Environment.NewLine + "--- RECEIPT ---" + Environment.NewLine);
            AgentTextBox.AppendText(CommandCodec.Serialize(result.Agent));
        }

        OutputTabs.SelectedItem = result.Evidence is not null ? EvidenceTab : ResultTab;
    }

    private void ShowFailure(Exception ex)
    {
        StatusText.Text = "Ошибка";
        EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  ERROR  {ex.Message}");
        OutputTabs.SelectedItem = EventsTab;
        EndRun();
    }

    private const string DefaultCommand = """
{
  "schema": "matawaka.command/v1",
  "id": "game-companion-observe-001",
  "kind": "agent.run",
  "target": "game-intellectual-companion",
  "policyProfile": "uu-aap-bridge-v0",
  "payload": {
    "mode": "propose",
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
