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
    private readonly WorkbenchAcceptanceHarness _acceptanceHarness;
    private readonly LocalCheckpointService _checkpointService = new();
    private readonly LocalUpdateIntakeService _updateIntakeService = new();
    private readonly LocalUpdateMaterializationService _updateMaterializationService;
    private readonly StagedUpdateApplyPlanService _stagedApplyPlanService = new();
    private readonly BoundedUpdateApplyBuildService _applyBuildService;
    private readonly MaintenanceRecoveryAssessmentService _recoveryAssessmentService = new();
    private readonly MaintenanceRecoveryPlanService _recoveryPlanService = new();
    private readonly MaintenanceRecoveryExecutionService _recoveryExecutionService = new();
    private readonly IsolatedRecoveryDrillService _isolatedRecoveryDrillService = new();
    private WorkbenchAcceptanceReceipt? _lastAcceptanceReceipt;
    private string? _lastAcceptanceArtifactPath;
    private bool _lastAcceptanceConsumed;
    private WorkbenchUpdatePlanReceipt? _lastUpdatePlanReceipt;
    private string? _lastUpdatePlanArtifactPath;
    private string? _lastUpdatePackagePath;
    private bool _lastUpdatePlanConsumed;
    private WorkbenchUpdateMaterializationReceipt? _lastMaterializationReceipt;
    private string? _lastMaterializationArtifactPath;
    private WorkbenchStagedApplyPlanReceipt? _lastStagedApplyPlanReceipt;
    private string? _lastStagedApplyPlanArtifactPath;
    private WorkbenchUpdateApplyBuildReceipt? _lastApplyBuildReceipt;
    private string? _lastApplyBuildArtifactPath;
    private MaintenanceRecoveryAssessmentReceipt? _lastRecoveryAssessmentReceipt;
    private string? _lastRecoveryAssessmentArtifactPath;
    private MaintenanceRecoveryPlanReceipt? _lastRecoveryPlanReceipt;
    private string? _lastRecoveryPlanArtifactPath;
    private MaintenanceRecoveryExecutionReceipt? _lastRecoveryExecutionReceipt;
    private string? _lastRecoveryExecutionArtifactPath;
    private string? _lastRecoveryExecutionAuthorityPath;
    private CancellationTokenSource? _cts;
    private WorkbenchProgressReceipt? _lastProgressReceipt;
    private CommandTerminalState? _currentTerminalState;
    private int _runEpoch;

    public MainWindow()
    {
        InitializeComponent();
        _acceptanceHarness = new WorkbenchAcceptanceHarness(_router);
        _updateMaterializationService = new LocalUpdateMaterializationService(_updateIntakeService);
        _applyBuildService = new BoundedUpdateApplyBuildService(_stagedApplyPlanService);
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


    private async void SelfTestButton_Click(object sender, RoutedEventArgs e)
    {
        var id = $"self-test-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            if (AgentEnabledBox.IsChecked != true)
                throw new InvalidDataException("Self-test requires 'Агент включен' to be explicitly enabled.");

            SaveSettings();
            BeginRun(id);
            StatusText.Text = "RUNNING: acceptance matrix (2 propose providers + denied execute)";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  acceptance.started           v0.19 matrix");

            var context = new RuntimeContext(
                CatalogRootBox.Text,
                AgentEnabledBox.IsChecked == true,
                false);

            var receipt = await _acceptanceHarness.RunAsync(context, _cts!.Token);
            var artifactDir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
            Directory.CreateDirectory(artifactDir);
            var artifactPath = Path.Combine(artifactDir, $"v0.19-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            await File.WriteAllTextAsync(
                artifactPath,
                CommandCodec.Serialize(receipt),
                new UTF8Encoding(false),
                _cts.Token);

            _lastAcceptanceReceipt = receipt;
            _lastAcceptanceArtifactPath = artifactPath;
            _lastAcceptanceConsumed = false;
            AcceptCheckpointButton.IsEnabled = receipt.Passed;

            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Receipt = receipt,
                ArtifactPath = artifactPath
            });
            OutputTabs.SelectedItem = AcceptanceTab;
            ProgressBar.Value = 100;
            _currentTerminalState = receipt.Passed ? CommandTerminalState.Completed : CommandTerminalState.Failed;
            StatusText.Text = receipt.Passed
                ? $"COMPLETED: acceptance PASSED; {artifactPath}"
                : $"FAILED: acceptance matrix has failing checks; {artifactPath}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  acceptance.{(receipt.Passed ? "completed" : "failed"),-18} passed={receipt.Passed}; {artifactPath}");
        }
        catch (OperationCanceledException)
        {
            ShowCancelled();
        }
        catch (InvalidDataException ex)
        {
            ShowInvalid(ex);
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
        finally
        {
            EndRun();
        }
    }

    private async void AcceptCheckpointButton_Click(object sender, RoutedEventArgs e)
    {
        var id = $"accept-v0.19-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            if (_lastAcceptanceReceipt is null || !_lastAcceptanceReceipt.Passed || string.IsNullOrWhiteSpace(_lastAcceptanceArtifactPath))
                throw new InvalidDataException("Run a passing Self-test in this Workbench process before accepting the local checkpoint.");
            if (_lastAcceptanceConsumed)
                throw new InvalidDataException("The latest Self-test receipt has already been consumed by a local checkpoint acceptance.");

            SaveSettings();
            var candidate = await _checkpointService.PreviewAsync(
                WorkspaceRootBox.Text,
                _lastAcceptanceArtifactPath,
                _lastAcceptanceReceipt,
                CancellationToken.None);

            var preview = new StringBuilder();
            preview.AppendLine("Создать локальный accepted checkpoint Workbench?");
            preview.AppendLine();
            preview.AppendLine($"Predecessor: {candidate.PreviousHead}");
            preview.AppendLine($"Tag: {candidate.TargetTag}");
            preview.AppendLine($"Acceptance SHA-256: {candidate.AcceptanceArtifactSha256}");
            preview.AppendLine();
            preview.AppendLine("Изменения Workbench, которые войдут в commit:");
            foreach (var file in candidate.ChangedFiles.Take(30)) preview.AppendLine($"  {file}");
            if (candidate.ChangedFiles.Count > 30) preview.AppendLine($"  ... +{candidate.ChangedFiles.Count - 30} files");
            preview.AppendLine();
            preview.AppendLine("Операция локальная: git add/commit/tag только в Workbench. Git push/fetch, сеть и каталог Matawaka не изменяются. Agent Execute не включается.");

            if (MessageBox.Show(this, preview.ToString(), "Принять Workbench v0.19", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            BeginRun(id);
            StatusText.Text = "RUNNING: explicit local Workbench checkpoint";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  checkpoint.requested        tag={candidate.TargetTag}; files={candidate.ChangedFiles.Count}");

            var receipt = await _checkpointService.AcceptAsync(candidate, _cts!.Token);
            var receiptPath = await LocalCheckpointService.WriteReceiptAsync(WorkspaceRootBox.Text, receipt, _cts.Token);
            _lastAcceptanceConsumed = true;

            AcceptanceTextBox.Text = CommandCodec.Serialize(new
            {
                Acceptance = _lastAcceptanceReceipt,
                AcceptanceArtifactPath = _lastAcceptanceArtifactPath,
                Checkpoint = receipt,
                CheckpointReceiptPath = receiptPath
            });
            OutputTabs.SelectedItem = AcceptanceTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: {receipt.Tag} -> {receipt.NewHead}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  checkpoint.completed        {receipt.Tag} -> {receipt.NewHead}; remotePush=false; catalogMutation=false");
        }
        catch (OperationCanceledException)
        {
            ShowCancelled();
        }
        catch (InvalidDataException ex)
        {
            ShowInvalid(ex);
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
        finally
        {
            EndRun();
        }
    }

    private async void UpdatePlanButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Workbench update package (*.zip)|*.zip|Все файлы (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;

        var id = $"update-plan-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            _lastUpdatePlanReceipt = null;
            _lastUpdatePlanArtifactPath = null;
            _lastUpdatePackagePath = null;
            _lastUpdatePlanConsumed = true;
            MaterializeUpdateButton.IsEnabled = false;
            StagedApplyPlanButton.IsEnabled = false;
            _lastMaterializationReceipt = null;
            _lastMaterializationArtifactPath = null;
            _lastStagedApplyPlanReceipt = null;
            _lastStagedApplyPlanArtifactPath = null;
            _lastApplyBuildReceipt = null;
            _lastApplyBuildArtifactPath = null;
            ApplyBuildUpdateButton.IsEnabled = false;
            LaunchCandidateButton.IsEnabled = false;

            SaveSettings();
            BeginRun(id);
            StatusText.Text = "RUNNING: local update package intake (plan only)";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  update.plan.started          {Path.GetFileName(dialog.FileName)}");

            var planned = await _updateIntakeService.PlanAsync(
                dialog.FileName,
                WorkspaceRootBox.Text,
                _cts!.Token);

            _lastUpdatePlanReceipt = planned.Receipt;
            _lastUpdatePlanArtifactPath = planned.ArtifactPath;
            _lastUpdatePackagePath = dialog.FileName;
            _lastUpdatePlanConsumed = false;
            MaterializeUpdateButton.IsEnabled = IsMaterializationReady();

            UpdatePlanTextBox.Text = CommandCodec.Serialize(new
            {
                Plan = planned.Receipt,
                PlanArtifactPath = planned.ArtifactPath,
                MaterializationAvailable = IsMaterializationReady()
            });
            OutputTabs.SelectedItem = UpdatePlanTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: update intake {planned.Receipt.Status}; materialization=false";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  update.plan.completed        {planned.Receipt.Status}; materialization=false; {planned.ArtifactPath}");
        }
        catch (OperationCanceledException)
        {
            ShowCancelled();
        }
        catch (InvalidDataException ex)
        {
            ShowInvalid(ex);
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
        finally
        {
            EndRun();
        }
    }

    private async void MaterializeUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        var id = $"update-materialize-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            if (!IsMaterializationReady() || _lastUpdatePlanReceipt is null || string.IsNullOrWhiteSpace(_lastUpdatePackagePath))
                throw new InvalidDataException("Create a READY update plan in this Workbench process before materialization.");

            var plan = _lastUpdatePlanReceipt;
            var preview = new StringBuilder();
            preview.AppendLine("Материализовать проверенный update package в локальный staging?");
            preview.AppendLine();
            preview.AppendLine($"Package: {plan.PackageFileName}");
            preview.AppendLine($"SHA-256: {plan.PackageSha256}");
            preview.AppendLine($"Predecessor: {plan.PredecessorCommit} / {plan.PredecessorTag}");
            preview.AppendLine($"Target: {plan.TargetVersion} / {plan.TargetTag}");
            preview.AppendLine($"Payload: {plan.PayloadFileCount} files; {plan.PayloadBytes} bytes");
            preview.AppendLine();
            preview.AppendLine("Разрешается только запись проверенных payload bytes в Workbench/.workbench/update-materializations и materialization receipt. Source tree, build, git commit/tag, сеть, каталог Matawaka и Agent Execute не разрешаются.");

            if (MessageBox.Show(this, preview.ToString(), "Материализовать Workbench update", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            SaveSettings();
            BeginRun(id);
            StatusText.Text = "RUNNING: explicit staging-only update materialization";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  update.materialization.requested package={plan.PackageFileName}; target={plan.TargetVersion}");

            var materialized = await _updateMaterializationService.MaterializeAsync(
                _lastUpdatePackagePath,
                plan,
                WorkspaceRootBox.Text,
                _cts!.Token);

            _lastUpdatePlanConsumed = true;
            _lastMaterializationReceipt = materialized.Receipt;
            _lastMaterializationArtifactPath = materialized.ArtifactPath;
            _lastStagedApplyPlanReceipt = null;
            _lastStagedApplyPlanArtifactPath = null;
            _lastApplyBuildReceipt = null;
            _lastApplyBuildArtifactPath = null;
            StagedApplyPlanButton.IsEnabled = true;
            ApplyBuildUpdateButton.IsEnabled = false;
            LaunchCandidateButton.IsEnabled = false;
            UpdatePlanTextBox.Text = CommandCodec.Serialize(new
            {
                Plan = plan,
                PlanArtifactPath = _lastUpdatePlanArtifactPath,
                Materialization = materialized.Receipt,
                MaterializationReceiptPath = materialized.ArtifactPath,
                StagedApplyPlanAvailable = true
            });
            OutputTabs.SelectedItem = UpdatePlanTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: staging materialized -> {materialized.Receipt.StagingRoot}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  update.materialization.completed stagingOnly=true; files={materialized.Receipt.PayloadFileCount}; build=false; sourceMutation=false");
        }
        catch (OperationCanceledException)
        {
            ShowCancelled();
        }
        catch (InvalidDataException ex)
        {
            ShowInvalid(ex);
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
        finally
        {
            EndRun();
        }
    }

    private async void StagedApplyPlanButton_Click(object sender, RoutedEventArgs e)
    {
        var id = $"update-apply-plan-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            if (_lastMaterializationReceipt is null || string.IsNullOrWhiteSpace(_lastMaterializationArtifactPath))
                throw new InvalidDataException("Materialize a validated update package in this Workbench process before creating a staged source-apply plan.");

            SaveSettings();
            BeginRun(id);
            StatusText.Text = "RUNNING: read-only staged source-apply plan";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  update.apply-plan.started    target={_lastMaterializationReceipt.TargetVersion}");

            var planned = await _stagedApplyPlanService.PlanAsync(
                _lastMaterializationReceipt,
                WorkspaceRootBox.Text,
                _cts!.Token);

            _lastStagedApplyPlanReceipt = planned.Receipt;
            _lastStagedApplyPlanArtifactPath = planned.ArtifactPath;
            _lastApplyBuildReceipt = null;
            _lastApplyBuildArtifactPath = null;
            ApplyBuildUpdateButton.IsEnabled = string.Equals(planned.Receipt.Status, "READY_FOR_SEPARATE_SOURCE_APPLY_AUTHORITY", StringComparison.Ordinal);
            LaunchCandidateButton.IsEnabled = false;

            UpdatePlanTextBox.Text = CommandCodec.Serialize(new
            {
                Materialization = _lastMaterializationReceipt,
                MaterializationReceiptPath = _lastMaterializationArtifactPath,
                ApplyPlan = planned.Receipt,
                ApplyPlanArtifactPath = planned.ArtifactPath
            });
            OutputTabs.SelectedItem = UpdatePlanTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: {planned.Receipt.Status}; sourceMutation=false; build=false";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  update.apply-plan.completed  {planned.Receipt.Status}; add={planned.Receipt.AddCount}; replace={planned.Receipt.ReplaceCount}; noop={planned.Receipt.NoOpCount}; sourceMutation=false");
        }
        catch (OperationCanceledException)
        {
            ShowCancelled();
        }
        catch (InvalidDataException ex)
        {
            ShowInvalid(ex);
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
        finally
        {
            EndRun();
        }
    }

    private async void ApplyBuildUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        var id = $"update-apply-build-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            if (_lastMaterializationReceipt is null || _lastStagedApplyPlanReceipt is null || string.IsNullOrWhiteSpace(_lastStagedApplyPlanArtifactPath))
                throw new InvalidDataException("Create a fresh READY staged source-apply plan before applying/building an update candidate.");
            if (!string.Equals(_lastStagedApplyPlanReceipt.Status, "READY_FOR_SEPARATE_SOURCE_APPLY_AUTHORITY", StringComparison.Ordinal))
                throw new InvalidDataException("The current staged source-apply plan is not READY.");

            var plan = _lastStagedApplyPlanReceipt;
            var preview = new StringBuilder();
            preview.AppendLine("Применить точный staged source delta и собрать candidate?");
            preview.AppendLine();
            preview.AppendLine($"Predecessor: {plan.PredecessorCommit} / {plan.PredecessorTag}");
            preview.AppendLine($"Target: {plan.TargetVersion} / {plan.TargetTag}");
            preview.AppendLine($"Source delta: Add={plan.AddCount}; Replace={plan.ReplaceCount}; NoOp={plan.NoOpCount}");
            preview.AppendLine();
            foreach (var change in plan.SourceChanges.Where(item => item.Action is "Add" or "Replace").Take(30))
                preview.AppendLine($"  {change.Action,-7} {change.Path}");
            preview.AppendLine();
            preview.AppendLine("Разрешается только exact source Add/Replace из materialized staging и фиксированные локальные dotnet build/publish с --no-restore. Git checkpoint/tag/push/fetch, catalog mutation, Agent Execute и запуск candidate НЕ разрешаются этим подтверждением. OS network isolation не создаётся.");

            if (MessageBox.Show(this, preview.ToString(), "Применить + собрать Workbench candidate", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            SaveSettings();
            BeginRun(id);
            StatusText.Text = "RUNNING: explicit exact source apply + fixed offline build";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  update.apply-build.requested target={plan.TargetVersion}; add={plan.AddCount}; replace={plan.ReplaceCount}");

            var built = await _applyBuildService.ApplyAndBuildAsync(
                _lastMaterializationReceipt,
                plan,
                _lastStagedApplyPlanArtifactPath,
                WorkspaceRootBox.Text,
                _cts!.Token);

            _lastApplyBuildReceipt = built.Receipt;
            _lastApplyBuildArtifactPath = built.ArtifactPath;
            LaunchCandidateButton.IsEnabled = true;
            UpdatePlanTextBox.Text = CommandCodec.Serialize(new
            {
                Materialization = _lastMaterializationReceipt,
                MaterializationReceiptPath = _lastMaterializationArtifactPath,
                ApplyPlan = plan,
                ApplyPlanArtifactPath = _lastStagedApplyPlanArtifactPath,
                ApplyBuild = built.Receipt,
                ApplyBuildReceiptPath = built.ArtifactPath,
                ApplyBuildAuthorityPath = built.AuthorityPath,
                CandidateLaunchAvailable = true
            });
            OutputTabs.SelectedItem = UpdatePlanTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: {built.Receipt.Status}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  update.apply-build.completed candidate={built.Receipt.CandidateExecutablePath}; checkpoint=false; launch=false");
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally { EndRun(); }
    }

    private async void LaunchCandidateButton_Click(object sender, RoutedEventArgs e)
    {
        var id = $"update-launch-candidate-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            if (_lastApplyBuildReceipt is null || string.IsNullOrWhiteSpace(_lastApplyBuildArtifactPath))
                throw new InvalidDataException("Build a byte-bound candidate before launch.");

            var receipt = _lastApplyBuildReceipt;
            var preview = new StringBuilder();
            preview.AppendLine("Запустить точный собранный Workbench candidate?");
            preview.AppendLine();
            preview.AppendLine($"Target: {receipt.TargetVersion} / {receipt.TargetTag}");
            preview.AppendLine($"Executable: {receipt.CandidateExecutablePath}");
            preview.AppendLine($"SHA-256: {receipt.CandidateExecutableSha256}");
            preview.AppendLine();
            preview.AppendLine("Запуск candidate не означает acceptance. Новый Workbench должен отдельно пройти Self-test и получить отдельное подтверждение Принять. Git/network/catalog/Agent Execute authority этим действием не создаются.");
            if (MessageBox.Show(this, preview.ToString(), "Запустить Workbench candidate", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            BeginRun(id);
            StatusText.Text = "RUNNING: exact receipt-bound candidate launch";
            var launched = await _applyBuildService.LaunchCandidateAsync(receipt, WorkspaceRootBox.Text, _cts!.Token);
            UpdatePlanTextBox.Text = CommandCodec.Serialize(new
            {
                ApplyBuild = receipt,
                ApplyBuildReceiptPath = _lastApplyBuildArtifactPath,
                Launch = launched.Receipt,
                LaunchReceiptPath = launched.ArtifactPath
            });
            OutputTabs.SelectedItem = UpdatePlanTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: {launched.Receipt.Status}; pid={launched.Receipt.ProcessId}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  update.candidate.launched    pid={launched.Receipt.ProcessId}; accepted=false; checkpoint=false");
        }
        catch (OperationCanceledException) { ShowCancelled(); }
        catch (InvalidDataException ex) { ShowInvalid(ex); }
        catch (Exception ex) { ShowFailure(ex); }
        finally { EndRun(); }
    }

    private bool IsMaterializationReady()
        => _lastUpdatePlanReceipt is not null &&
           !_lastUpdatePlanConsumed &&
           !string.IsNullOrWhiteSpace(_lastUpdatePackagePath) &&
           string.Equals(_lastUpdatePlanReceipt.Status, "READY_FOR_SEPARATE_MATERIALIZATION_AUTHORITY", StringComparison.Ordinal) &&
           _lastUpdatePlanReceipt.PackageStructureValidated &&
           _lastUpdatePlanReceipt.PayloadDigestsValidated &&
           _lastUpdatePlanReceipt.PredecessorTagMatched &&
           _lastUpdatePlanReceipt.PredecessorCommitMatched;

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

    private async void RecoveryCheckButton_Click(object sender, RoutedEventArgs e)
    {
        var id = $"recovery-assessment-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            SaveSettings();
            BeginRun(id);
            StatusText.Text = "RUNNING: read-only maintenance recovery assessment";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  recovery.assessment.started  observation-only");

            _lastRecoveryAssessmentReceipt = null;
            _lastRecoveryAssessmentArtifactPath = null;
            _lastRecoveryPlanReceipt = null;
            _lastRecoveryPlanArtifactPath = null;
            _lastRecoveryExecutionReceipt = null;
            _lastRecoveryExecutionArtifactPath = null;
            _lastRecoveryExecutionAuthorityPath = null;
            RecoveryPlanButton.IsEnabled = false;
            RecoveryExecuteButton.IsEnabled = false;

            var receipt = await _recoveryAssessmentService.AssessAsync(WorkspaceRootBox.Text, _cts!.Token);
            var artifactDir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "recovery-assessments");
            Directory.CreateDirectory(artifactDir);
            var artifactPath = Path.Combine(artifactDir, $"recovery-assessment-v0.19-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            await File.WriteAllTextAsync(
                artifactPath,
                CommandCodec.Serialize(receipt),
                new UTF8Encoding(false),
                _cts.Token);

            _lastRecoveryAssessmentReceipt = receipt;
            _lastRecoveryAssessmentArtifactPath = artifactPath;
            RecoveryPlanButton.IsEnabled = true;

            RecoveryTextBox.Text = CommandCodec.Serialize(new { Assessment = receipt, AssessmentArtifactPath = artifactPath });
            OutputTabs.SelectedItem = RecoveryTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: recovery assessment {receipt.Classification}; actionAuthorized=false";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  recovery.assessment.completed classification={receipt.Classification}; actionAuthorized=false");
        }
        catch (OperationCanceledException)
        {
            ShowCancelled();
        }
        catch (InvalidDataException ex)
        {
            ShowInvalid(ex);
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
        finally
        {
            EndRun();
        }
    }

    private async void RecoveryPlanButton_Click(object sender, RoutedEventArgs e)
    {
        var id = $"recovery-plan-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            if (_lastRecoveryAssessmentReceipt is null || string.IsNullOrWhiteSpace(_lastRecoveryAssessmentArtifactPath))
                throw new InvalidDataException("Run Recovery check in this Workbench process before creating a recovery plan.");

            SaveSettings();
            BeginRun(id);
            StatusText.Text = "RUNNING: fresh assessment-bound recovery planning (read-only)";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  recovery.plan.started        classification={_lastRecoveryAssessmentReceipt.Classification}");

            var receipt = await _recoveryPlanService.PlanAsync(
                WorkspaceRootBox.Text,
                _lastRecoveryAssessmentArtifactPath,
                _lastRecoveryAssessmentReceipt,
                _cts!.Token);

            var artifactDir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "recovery-plans");
            Directory.CreateDirectory(artifactDir);
            var artifactPath = Path.Combine(artifactDir, $"recovery-plan-v0.19-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            await File.WriteAllTextAsync(
                artifactPath,
                CommandCodec.Serialize(receipt),
                new UTF8Encoding(false),
                _cts.Token);

            _lastRecoveryPlanReceipt = receipt;
            _lastRecoveryPlanArtifactPath = artifactPath;
            RecoveryTextBox.Text = CommandCodec.Serialize(new
            {
                Assessment = _lastRecoveryAssessmentReceipt,
                AssessmentArtifactPath = _lastRecoveryAssessmentArtifactPath,
                Plan = receipt,
                PlanArtifactPath = artifactPath
            });
            OutputTabs.SelectedItem = RecoveryTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: recovery plan {receipt.Status}; executionAuthorized=false";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  recovery.plan.completed      status={receipt.Status}; executionAuthorized=false");
        }
        catch (OperationCanceledException)
        {
            ShowCancelled();
        }
        catch (InvalidDataException ex)
        {
            ShowInvalid(ex);
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
        finally
        {
            EndRun();
        }
    }

    private async void RecoveryExecuteButton_Click(object sender, RoutedEventArgs e)
    {
        var id = $"recovery-execute-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            if (_lastRecoveryAssessmentReceipt is null || string.IsNullOrWhiteSpace(_lastRecoveryAssessmentArtifactPath))
                throw new InvalidDataException("Run Recovery check in this Workbench process before recovery execution.");
            if (_lastRecoveryPlanReceipt is null || string.IsNullOrWhiteSpace(_lastRecoveryPlanArtifactPath))
                throw new InvalidDataException("Create a fresh Recovery plan in this Workbench process before recovery execution.");
            if (!string.Equals(_lastRecoveryPlanReceipt.Status, "READY_FOR_SEPARATE_RECOVERY_AUTHORITY", StringComparison.Ordinal) ||
                !_lastRecoveryPlanReceipt.SeparateRecoveryAuthorityEligible)
                throw new InvalidDataException("The current recovery plan does not permit a separate recovery execution authority decision.");
            if (_lastRecoveryExecutionReceipt is not null)
                throw new InvalidDataException("The current recovery plan has already been consumed by a recovery execution attempt in this Workbench process. Run Recovery check again.");

            var preview = new StringBuilder();
            preview.AppendLine("Выполнить ограниченное восстановление Workbench?");
            preview.AppendLine();
            preview.AppendLine($"Accepted HEAD: {_lastRecoveryPlanReceipt.ReverifiedHead}");
            preview.AppendLine($"Classification: {_lastRecoveryAssessmentReceipt.Classification}");
            preview.AppendLine($"Dirty paths: {_lastRecoveryPlanReceipt.ReverifiedDirtyPaths.Count}");
            foreach (var path in _lastRecoveryPlanReceipt.ReverifiedDirtyPaths.Take(30)) preview.AppendLine($"  {path}");
            if (_lastRecoveryPlanReceipt.ReverifiedDirtyPaths.Count > 30) preview.AppendLine($"  ... +{_lastRecoveryPlanReceipt.ReverifiedDirtyPaths.Count - 30} paths");
            preview.AppendLine();
            preview.AppendLine("Разрешается только вернуть exact tracked candidate bytes к текущему accepted HEAD и удалить exact byte-reverified untracked candidate additions. Build/checkpoint/network/catalog/Agent Execute не разрешаются.");
            preview.AppendLine("После выполнения потребуется новый Recovery check.");

            if (MessageBox.Show(this, preview.ToString(), "Recovery execute v0.19", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            SaveSettings();
            BeginRun(id);
            StatusText.Text = "RUNNING: explicit bounded recovery execution";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  recovery.execution.requested dirtyPaths={_lastRecoveryPlanReceipt.ReverifiedDirtyPaths.Count}; explicitUiConfirmation=true");

            var result = await _recoveryExecutionService.ExecuteAsync(
                WorkspaceRootBox.Text,
                _lastRecoveryAssessmentArtifactPath,
                _lastRecoveryAssessmentReceipt,
                _lastRecoveryPlanArtifactPath,
                _lastRecoveryPlanReceipt,
                _cts!.Token);

            _lastRecoveryExecutionReceipt = result.Receipt;
            _lastRecoveryExecutionArtifactPath = result.ArtifactPath;
            _lastRecoveryExecutionAuthorityPath = result.AuthorityPath;

            RecoveryTextBox.Text = CommandCodec.Serialize(new
            {
                Assessment = _lastRecoveryAssessmentReceipt,
                AssessmentArtifactPath = _lastRecoveryAssessmentArtifactPath,
                Plan = _lastRecoveryPlanReceipt,
                PlanArtifactPath = _lastRecoveryPlanArtifactPath,
                Execution = result.Receipt,
                ExecutionArtifactPath = result.ArtifactPath,
                ExecutionAuthorityPath = result.AuthorityPath
            });
            OutputTabs.SelectedItem = RecoveryTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: {result.Receipt.Status}; run Recovery check";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  recovery.execution.completed status={result.Receipt.Status}; build=false; checkpoint=false; network=false");

            // Execution consumes the fresh assessment/plan. A new observation is required.
            _lastRecoveryAssessmentReceipt = null;
            _lastRecoveryAssessmentArtifactPath = null;
            _lastRecoveryPlanReceipt = null;
            _lastRecoveryPlanArtifactPath = null;
        }
        catch (OperationCanceledException)
        {
            ShowCancelled();
        }
        catch (InvalidDataException ex)
        {
            ShowInvalid(ex);
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
        finally
        {
            EndRun();
        }
    }

    private async void RecoveryDrillButton_Click(object sender, RoutedEventArgs e)
    {
        var id = $"recovery-drill-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            var preview = new StringBuilder();
            preview.AppendLine("Запустить изолированный recovery drill?");
            preview.AppendLine();
            preview.AppendLine("Drill создаёт отдельный вложенный Git fixture только под Workbench/.workbench/recovery-drills.");
            preview.AppendLine("В fixture будут созданы accepted HEAD, exact tracked Replace + untracked Add, затем реальные Recovery check/plan/execute должны вернуть fixture к тому же чистому accepted HEAD.");
            preview.AppendLine();
            preview.AppendLine("Главный Workbench repository должен быть clean и не может быть изменён drill-операцией. Build/checkpoint/network/catalog/Agent Execute не разрешаются.");
            preview.AppendLine("Fixture и receipts сохраняются как локальное evidence после завершения.");

            if (MessageBox.Show(this, preview.ToString(), "Recovery drill v0.19", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            SaveSettings();
            BeginRun(id);
            StatusText.Text = "RUNNING: isolated bounded recovery drill";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  recovery.drill.started       isolatedFixture=true; mainMutation=false");

            var result = await _isolatedRecoveryDrillService.RunAsync(WorkspaceRootBox.Text, _cts!.Token);
            RecoveryTextBox.Text = CommandCodec.Serialize(new
            {
                Drill = result.Receipt,
                DrillArtifactPath = result.ArtifactPath
            });
            OutputTabs.SelectedItem = RecoveryTab;
            ProgressBar.Value = 100;
            _currentTerminalState = result.Receipt.Passed ? CommandTerminalState.Completed : CommandTerminalState.Failed;
            StatusText.Text = result.Receipt.Passed
                ? $"COMPLETED: isolated recovery drill PASSED; {result.ArtifactPath}"
                : $"FAILED: isolated recovery drill did not converge; {result.ArtifactPath}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  recovery.drill.{(result.Receipt.Passed ? "completed" : "failed"),-11} passed={result.Receipt.Passed}; mainUnchanged={result.Receipt.MainRepositoryUnchanged}");
        }
        catch (OperationCanceledException)
        {
            ShowCancelled();
        }
        catch (InvalidDataException ex)
        {
            ShowInvalid(ex);
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
        finally
        {
            EndRun();
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
        SelfTestButton.IsEnabled = false;
        AcceptCheckpointButton.IsEnabled = false;
        UpdatePlanButton.IsEnabled = false;
        MaterializeUpdateButton.IsEnabled = false;
        StagedApplyPlanButton.IsEnabled = false;
        ApplyBuildUpdateButton.IsEnabled = false;
        LaunchCandidateButton.IsEnabled = false;
        RecoveryCheckButton.IsEnabled = false;
        RecoveryPlanButton.IsEnabled = false;
        RecoveryExecuteButton.IsEnabled = false;
        RecoveryDrillButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        ProgressBar.Value = 0;
        StatusText.Text = $"RUNNING: {id}";
        ResultTextBox.Clear();
        EvidenceTextBox.Clear();
        AuthorityTextBox.Clear();
        LivenessTextBox.Clear();
        SemanticTextBox.Clear();
        ProcessBoundaryTextBox.Clear();
        AgentTextBox.Clear();
        AcceptanceTextBox.Clear();
        UpdatePlanTextBox.Clear();
        RecoveryTextBox.Clear();
        _lastProgressReceipt = null;
        _currentTerminalState = null;
        _runEpoch++;
    }

    private void EndRun()
    {
        RunButton.IsEnabled = true;
        SelfTestButton.IsEnabled = true;
        AcceptCheckpointButton.IsEnabled = _lastAcceptanceReceipt?.Passed == true && !_lastAcceptanceConsumed && !string.IsNullOrWhiteSpace(_lastAcceptanceArtifactPath);
        UpdatePlanButton.IsEnabled = true;
        MaterializeUpdateButton.IsEnabled = IsMaterializationReady();
        StagedApplyPlanButton.IsEnabled = _lastMaterializationReceipt is not null;
        ApplyBuildUpdateButton.IsEnabled = _lastStagedApplyPlanReceipt is not null && string.Equals(_lastStagedApplyPlanReceipt.Status, "READY_FOR_SEPARATE_SOURCE_APPLY_AUTHORITY", StringComparison.Ordinal) && _lastApplyBuildReceipt is null;
        LaunchCandidateButton.IsEnabled = _lastApplyBuildReceipt is not null;
        RecoveryCheckButton.IsEnabled = true;
        RecoveryPlanButton.IsEnabled = _lastRecoveryAssessmentReceipt is not null && !string.IsNullOrWhiteSpace(_lastRecoveryAssessmentArtifactPath);
        RecoveryExecuteButton.IsEnabled = _lastRecoveryPlanReceipt is not null &&
            string.Equals(_lastRecoveryPlanReceipt.Status, "READY_FOR_SEPARATE_RECOVERY_AUTHORITY", StringComparison.Ordinal) &&
            _lastRecoveryPlanReceipt.SeparateRecoveryAuthorityEligible &&
            _lastRecoveryExecutionReceipt is null;
        RecoveryDrillButton.IsEnabled = true;
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
            if (e.Event.StartsWith("semantic.process.", StringComparison.OrdinalIgnoreCase))
            {
                ProcessBoundaryTextBox.AppendText(line + Environment.NewLine);
                ProcessBoundaryTextBox.ScrollToEnd();
            }
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

        ProcessBoundaryTextBox.Text = result.ProcessBoundary is null
            ? "No semantic process-boundary receipt for this command."
            : CommandCodec.Serialize(result.ProcessBoundary);

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
            Note = "Workbench v0.10 compatibility projection; relevant UU-AAP source files are byte-verified separately from repository HEAD, canonical JavaScript implementation not executed."
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
  "id": "game-companion-propose-v0100",
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
