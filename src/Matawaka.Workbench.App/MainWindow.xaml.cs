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
    private readonly RecoveryCapabilityAdmissionService _recoveryCapabilityAdmissionService = new();
    private readonly RecoveryNegativeControlMatrixService _recoveryNegativeControlMatrixService = new();
    private readonly RecoveryEvidenceClosureService _recoveryEvidenceClosureService = new();
    private readonly RecoveryEvidenceReplayService _recoveryEvidenceReplayService = new();
    private readonly RecoveryEvidenceRelocationDrillService _recoveryEvidenceRelocationDrillService = new();
    private readonly RecoveryEvidenceTransportService _recoveryEvidenceTransportService = new();
    private readonly RecoveryEvidenceTransportIndependenceDrillService _recoveryTransportIndependenceDrillService = new();
    private readonly RecoveryTransportAdversarialControlMatrixService _recoveryTransportAdversarialControlMatrixService = new();
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
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  acceptance.started           v0.27 matrix");

            var context = new RuntimeContext(
                CatalogRootBox.Text,
                AgentEnabledBox.IsChecked == true,
                false);

            var receipt = await _acceptanceHarness.RunAsync(context, _cts!.Token);
            var artifactDir = Path.Combine(WorkspaceRootBox.Text, "Workbench", "artifacts", "acceptance");
            Directory.CreateDirectory(artifactDir);
            var artifactPath = Path.Combine(artifactDir, $"v0.27-{DateTime.Now:yyyyMMdd-HHmmss}.json");
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
        var id = $"accept-v0.27-{DateTime.Now:yyyyMMddHHmmss}";
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

            if (MessageBox.Show(this, preview.ToString(), "Принять Workbench v0.27", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
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
            var artifactPath = Path.Combine(artifactDir, $"recovery-assessment-v0.27-{DateTime.Now:yyyyMMdd-HHmmss}.json");
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
            var artifactPath = Path.Combine(artifactDir, $"recovery-plan-v0.27-{DateTime.Now:yyyyMMdd-HHmmss}.json");
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

            if (MessageBox.Show(this, preview.ToString(), "Recovery execute v0.24", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
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

    private async void RecoveryAdmissionButton_Click(object sender, RoutedEventArgs e)
    {
        var id = $"recovery-admission-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            SaveSettings();
            BeginRun(id);
            StatusText.Text = "RUNNING: read-only bounded recovery capability admission";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  recovery.admission.started  retained-v0.19-drill-evidence");

            var result = await _recoveryCapabilityAdmissionService.AssessAsync(WorkspaceRootBox.Text, _cts!.Token);

            RecoveryTextBox.Text = CommandCodec.Serialize(new
            {
                Admission = result.Receipt,
                AdmissionArtifactPath = result.ArtifactPath
            });
            OutputTabs.SelectedItem = RecoveryTab;
            ProgressBar.Value = 100;
            _currentTerminalState = result.Receipt.Admitted ? CommandTerminalState.Completed : CommandTerminalState.Failed;
            StatusText.Text = result.Receipt.Admitted
                ? $"COMPLETED: recovery capability admission {result.Receipt.Status}; generalRecoveryAuthority=false"
                : $"FAILED: recovery capability admission {result.Receipt.Status}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  recovery.admission.{(result.Receipt.Admitted ? "completed" : "failed"),-9} status={result.Receipt.Status}; generalRecoveryAuthority=false");
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


    private async void RecoveryNegativeControlsButton_Click(object sender, RoutedEventArgs e)
    {
        var id = $"recovery-negative-controls-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            var preview = new StringBuilder();
            preview.AppendLine("Запустить изолированную матрицу отрицательных recovery-controls?");
            preview.AppendLine();
            preview.AppendLine("Будут созданы три вложенных Git-fixture только под Workbench/.workbench/recovery-negative-controls.");
            preview.AppendLine("Проверяются отказы для unknown dirty state, byte drift after plan и dirty path-set drift after plan.");
            preview.AppendLine("Основной Workbench repository не изменяется. Build/checkpoint/network/catalog/Agent Execute не разрешаются.");
            preview.AppendLine("Fixture и receipts сохраняются как evidence.");

            if (MessageBox.Show(this, preview.ToString(), "Recovery negative controls v0.21", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            SaveSettings();
            BeginRun(id);
            StatusText.Text = "RUNNING: isolated recovery negative-control matrix";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  recovery.negatives.started    isolatedFixtures=true; mainMutation=false");

            var result = await _recoveryNegativeControlMatrixService.RunAsync(WorkspaceRootBox.Text, _cts!.Token);
            RecoveryTextBox.Text = CommandCodec.Serialize(new
            {
                Matrix = result.Receipt,
                MatrixArtifactPath = result.ArtifactPath
            });
            OutputTabs.SelectedItem = RecoveryTab;
            ProgressBar.Value = 100;
            _currentTerminalState = result.Receipt.Passed ? CommandTerminalState.Completed : CommandTerminalState.Failed;
            StatusText.Text = result.Receipt.Passed
                ? $"COMPLETED: recovery negative-control matrix PASSED; {result.ArtifactPath}"
                : $"FAILED: recovery negative-control matrix; {result.ArtifactPath}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  recovery.negatives.{(result.Receipt.Passed ? "completed" : "failed"),-9} passed={result.Receipt.Passed}; allRefusedBeforeAuthority={result.Receipt.AllRecoveryAttemptsRefusedBeforeAuthority}; mainUnchanged={result.Receipt.MainRepositoryUnchanged}");
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

    private async void RecoveryEvidenceClosureButton_Click(object sender, RoutedEventArgs e)
    {
        var id = $"recovery-evidence-closure-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            SaveSettings();
            BeginRun(id);
            StatusText.Text = "RUNNING: byte-bound recovery evidence closure";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  recovery.closure.started    drill=v0.19; admission=v0.20; negatives=v0.21; authorityExpansion=false");

            var result = await _recoveryEvidenceClosureService.CloseAsync(WorkspaceRootBox.Text, _cts!.Token);
            RecoveryTextBox.Text = CommandCodec.Serialize(new
            {
                Closure = result.Receipt,
                ClosureArtifactPath = result.ArtifactPath
            });
            OutputTabs.SelectedItem = RecoveryTab;
            ProgressBar.Value = 100;
            _currentTerminalState = result.Receipt.Closed ? CommandTerminalState.Completed : CommandTerminalState.Failed;
            StatusText.Text = result.Receipt.Closed
                ? $"COMPLETED: recovery evidence closure {result.Receipt.Status}; authorityExpansion=false"
                : $"FAILED: recovery evidence closure {result.Receipt.Status}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  recovery.closure.{(result.Receipt.Closed ? "completed" : "failed"),-9} status={result.Receipt.Status}; crossBindings={result.Receipt.CrossEvidenceBindingsVerified}; authorityExpansion=false");
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


    private async void RecoveryEvidenceReplayButton_Click(object sender, RoutedEventArgs e)
    {
        var id = $"recovery-evidence-replay-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            SaveSettings();
            BeginRun(id);
            StatusText.Text = "RUNNING: portable replay of byte-bound recovery evidence";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  recovery.replay.started     closure=v0.22; fixturePathDereference=false; authorityExpansion=false");

            var result = await _recoveryEvidenceReplayService.ReplayAsync(WorkspaceRootBox.Text, _cts!.Token);
            RecoveryTextBox.Text = CommandCodec.Serialize(new
            {
                Replay = result.Receipt,
                ReplayArtifactPath = result.ArtifactPath
            });
            OutputTabs.SelectedItem = RecoveryTab;
            ProgressBar.Value = 100;
            _currentTerminalState = result.Receipt.Replayed ? CommandTerminalState.Completed : CommandTerminalState.Failed;
            StatusText.Text = result.Receipt.Replayed
                ? $"COMPLETED: recovery evidence replay {result.Receipt.Status}; liveAuthority=false"
                : $"FAILED: recovery evidence replay {result.Receipt.Status}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  recovery.replay.{(result.Receipt.Replayed ? "completed" : "failed"),-9} status={result.Receipt.Status}; digestReproduced={result.Receipt.ClosureDigestReproduced}; fixturePathDereference=false");
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

    private async void RecoveryEvidenceRelocationButton_Click(object sender, RoutedEventArgs e)
    {
        var id = $"recovery-evidence-relocation-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            SaveSettings();
            var preview = new StringBuilder();
            preview.AppendLine("Создать relocatable copy последнего принятого v0.23 replay capsule и повторно воспроизвести evidence envelope только из relocated bytes?");
            preview.AppendLine();
            preview.AppendLine("Разрешается только копирование exact JSON evidence в Workbench/.workbench/recovery-replay-relocations и запись drill receipt.");
            preview.AppendLine("Исходный replay capsule, source evidence, fixture roots, source tree, Git history, build, сеть, каталог и Agent Execute не изменяются.");
            if (MessageBox.Show(this, preview.ToString(), "Recovery relocate v0.24", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            BeginRun(id);
            StatusText.Text = "RUNNING: relocate replay capsule and replay from copied bytes";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  recovery.relocate.started   source=v0.23 replay capsule; explicitUiConfirmation=true; liveAuthority=false");

            var result = await _recoveryEvidenceRelocationDrillService.RunAsync(WorkspaceRootBox.Text, _cts!.Token);
            RecoveryTextBox.Text = CommandCodec.Serialize(new
            {
                Relocation = result.Receipt,
                RelocationArtifactPath = result.ArtifactPath
            });
            OutputTabs.SelectedItem = RecoveryTab;
            ProgressBar.Value = 100;
            _currentTerminalState = result.Receipt.Passed ? CommandTerminalState.Completed : CommandTerminalState.Failed;
            StatusText.Text = result.Receipt.Passed
                ? $"COMPLETED: recovery relocation drill {result.Receipt.Status}; crossMachineProof=false"
                : $"FAILED: recovery relocation drill {result.Receipt.Status}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  recovery.relocate.{(result.Receipt.Passed ? "completed" : "failed"),-9} status={result.Receipt.Status}; digestReproduced={result.Receipt.RelocatedEvidenceEnvelopeDigestReproduced}; sourceCapsuleDereferenceDuringReplay=false");
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

    private async void RecoveryEvidenceExportButton_Click(object sender, RoutedEventArgs e)
    {
        var id = $"recovery-evidence-export-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            SaveSettings();
            var preview = new StringBuilder();
            preview.AppendLine("Экспортировать доказанный relocated recovery replay capsule в самодостаточный локальный transport ZIP?");
            preview.AppendLine();
            preview.AppendLine("Разрешается только чтение retained v0.24 relocation evidence, создание ZIP и export receipt под Workbench/artifacts/recovery-transports.");
            preview.AppendLine("Recovery execution, source mutation, build, Git checkpoint, сеть, каталог и Agent Execute не разрешаются. Export не доказывает producer authentication или cross-machine portability.");
            if (MessageBox.Show(this, preview.ToString(), "Recovery export v0.25", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            BeginRun(id);
            StatusText.Text = "RUNNING: export self-contained recovery evidence transport ZIP";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  recovery.export.started      source=v0.24 relocation; explicitUiConfirmation=true; liveAuthority=false");

            var result = await _recoveryEvidenceTransportService.ExportAsync(WorkspaceRootBox.Text, true, _cts!.Token);
            RecoveryTextBox.Text = CommandCodec.Serialize(new
            {
                Export = result.Receipt,
                ExportArtifactPath = result.ArtifactPath
            });
            OutputTabs.SelectedItem = RecoveryTab;
            ProgressBar.Value = 100;
            _currentTerminalState = result.Receipt.Exported ? CommandTerminalState.Completed : CommandTerminalState.Failed;
            StatusText.Text = result.Receipt.Exported
                ? $"COMPLETED: recovery transport exported; {result.Receipt.TransportZipPath}"
                : $"FAILED: recovery transport export {result.Receipt.Status}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  recovery.export.{(result.Receipt.Exported ? "completed" : "failed"),-9} status={result.Receipt.Status}; manifestVerified={result.Receipt.TransportManifestVerified}; authorityExpansion=false");
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

    private async void RecoveryEvidenceImportButton_Click(object sender, RoutedEventArgs e)
    {
        var id = $"recovery-evidence-import-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            SaveSettings();
            var dialog = new OpenFileDialog
            {
                Filter = "Matawaka recovery transport ZIP (*.zip)|*.zip|Все файлы (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false,
                Title = "Выберите v0.25 recovery evidence transport ZIP"
            };
            if (dialog.ShowDialog(this) != true)
                return;

            var inspection = await _recoveryEvidenceTransportService.InspectAsync(dialog.FileName, _cts?.Token ?? CancellationToken.None);
            var preview = new StringBuilder();
            preview.AppendLine("Импортировать проверенный recovery evidence transport ZIP в отдельный Workbench-local evidence root?");
            preview.AppendLine();
            preview.AppendLine($"ZIP: {dialog.FileName}");
            preview.AppendLine($"SHA-256: {inspection.TransportZipSha256}");
            preview.AppendLine($"Files: {inspection.Manifest.Files.Count}; evidence envelope: {inspection.Manifest.EvidenceEnvelopeDigest}");
            preview.AppendLine();
            preview.AppendLine("Импорт повторно проверит exact ZIP/file bytes и replay semantics, затем запишет только evidence copies + import receipt. Live recovery/source/build/checkpoint/network authority не создаётся.");
            if (MessageBox.Show(this, preview.ToString(), "Recovery import v0.25", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            BeginRun(id);
            StatusText.Text = "RUNNING: verify and import recovery evidence transport ZIP";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  recovery.import.started      zipSha={inspection.TransportZipSha256}; explicitUiConfirmation=true; liveAuthority=false");

            var result = await _recoveryEvidenceTransportService.ImportAsync(WorkspaceRootBox.Text, dialog.FileName, true, _cts!.Token);
            RecoveryTextBox.Text = CommandCodec.Serialize(new
            {
                Import = result.Receipt,
                ImportArtifactPath = result.ArtifactPath
            });
            OutputTabs.SelectedItem = RecoveryTab;
            ProgressBar.Value = 100;
            _currentTerminalState = result.Receipt.Verified ? CommandTerminalState.Completed : CommandTerminalState.Failed;
            StatusText.Text = result.Receipt.Verified
                ? $"COMPLETED: recovery transport import {result.Receipt.Status}; liveAuthority=false"
                : $"FAILED: recovery transport import {result.Receipt.Status}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  recovery.import.{(result.Receipt.Verified ? "completed" : "failed"),-9} status={result.Receipt.Status}; envelopeReproduced={result.Receipt.EvidenceEnvelopeDigestReproduced}; originalRootsRequired=false");
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

    private async void RecoveryTransportIndependenceButton_Click(object sender, RoutedEventArgs e)
    {
        var id = $"recovery-transport-independence-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            SaveSettings();
            var preview = new StringBuilder();
            preview.AppendLine("Проверить независимость уже принятого v0.25 recovery transport от исходных replay/relocation evidence roots?");
            preview.AppendLine();
            preview.AppendLine("Workbench свяжет retained passing v0.25 import receipt и exact transport ZIP, скопирует ZIP в disjoint .workbench root, затем выполнит inspect/replay и materialization только из copied transport bytes.");
            preview.AppendLine("Исходный transport/evidence не изменяется. Source tree, Git history, build, сеть, каталог и Agent Execute не разрешаются. Path guard является application-level, не OS sandbox.");
            if (MessageBox.Show(this, preview.ToString(), "Transport independence v0.26", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            BeginRun(id);
            StatusText.Text = "RUNNING: copied transport independence drill";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  recovery.transport-independence.started  source=v0.25 passing import; explicitUiConfirmation=true; liveAuthority=false");

            var result = await _recoveryTransportIndependenceDrillService.RunAsync(WorkspaceRootBox.Text, true, _cts!.Token);
            RecoveryTextBox.Text = CommandCodec.Serialize(new
            {
                Independence = result.Receipt,
                IndependenceArtifactPath = result.ArtifactPath
            });
            OutputTabs.SelectedItem = RecoveryTab;
            ProgressBar.Value = 100;
            _currentTerminalState = result.Receipt.Passed ? CommandTerminalState.Completed : CommandTerminalState.Failed;
            StatusText.Text = result.Receipt.Passed
                ? $"COMPLETED: recovery transport independence {result.Receipt.Status}; crossMachineProof=false"
                : $"FAILED: recovery transport independence {result.Receipt.Status}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  recovery.transport-independence.{(result.Receipt.Passed ? "completed" : "failed"),-9} status={result.Receipt.Status}; copiedZipOnly={result.Receipt.ReplayUsedOnlyCopiedTransportBytes}; originalEvidenceAccessAttempts={result.Receipt.OriginalEvidencePathAccessAttemptsDuringTransportReplay}");
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

    private async void RecoveryTransportAdversarialControlsButton_Click(object sender, RoutedEventArgs e)
    {
        var id = $"transport-negatives-v0.27-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            var preview = new StringBuilder();
            preview.AppendLine("Запустить изолированную transport adversarial negative-control matrix?");
            preview.AppendLine();
            preview.AppendLine("Будут созданы только три локальные копии уже byte-bound recovery transport ZIP под Workbench/.workbench.");
            preview.AppendLine("Контроли: drift после SHA-binding, extra ZIP entry, transport-manifest drift.");
            preview.AppendLine("Ожидается отказ до evidence materialization. Source transport, main source tree, Git HEAD/tag, сеть и Agent Execute не изменяются.");

            if (MessageBox.Show(this, preview.ToString(), "Transport negatives v0.27", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            SaveSettings();
            BeginRun(id);
            StatusText.Text = "RUNNING: isolated transport adversarial negative controls";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transport.negatives.started v0.27 isolated controls");

            var result = await _recoveryTransportAdversarialControlMatrixService.RunAsync(
                WorkspaceRootBox.Text,
                true,
                _cts!.Token);

            RecoveryTextBox.Text = CommandCodec.Serialize(new
            {
                Matrix = result.Receipt,
                MatrixArtifactPath = result.ArtifactPath
            });
            OutputTabs.SelectedItem = RecoveryTab;
            ProgressBar.Value = 100;
            _currentTerminalState = result.Receipt.Passed ? CommandTerminalState.Completed : CommandTerminalState.Failed;
            StatusText.Text = result.Receipt.Passed
                ? $"COMPLETED: transport adversarial controls PASSED; {result.ArtifactPath}"
                : $"FAILED: transport adversarial control matrix failed; {result.ArtifactPath}";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  transport.negatives.{(result.Receipt.Passed ? "completed" : "failed"),-10} passed={result.Receipt.Passed}; mainUnchanged={result.Receipt.MainRepositoryUnchanged}");
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
        RecoveryAdmissionButton.IsEnabled = false;
        RecoveryNegativeControlsButton.IsEnabled = false;
        RecoveryEvidenceClosureButton.IsEnabled = false;
        RecoveryEvidenceReplayButton.IsEnabled = false;
        RecoveryEvidenceRelocationButton.IsEnabled = false;
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
        RecoveryAdmissionButton.IsEnabled = true;
        RecoveryNegativeControlsButton.IsEnabled = true;
        RecoveryEvidenceClosureButton.IsEnabled = true;
        RecoveryEvidenceReplayButton.IsEnabled = true;
        RecoveryEvidenceRelocationButton.IsEnabled = true;
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
