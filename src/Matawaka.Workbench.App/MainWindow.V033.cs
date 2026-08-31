using System.IO;
using System.Text;
using System.Windows;
using Matawaka.Workbench.Protocol;
using Microsoft.Win32;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    private MaintenanceUpdateOrchestratorService CreateV033Orchestrator()
        => new(_updateIntakeService, _updateMaterializationService, _stagedApplyPlanService, _applyBuildService);

    private async void UpdateCandidateButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Workbench update package (*.zip)|*.zip|Все файлы (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;

        var id = $"update-candidate-v0.33-{DateTime.Now:yyyyMMddHHmmss}";
        var beganRun = false;
        UpdateCandidateButton.IsEnabled = false;
        try
        {
            ResetV033UpdateState();
            SaveSettings();
            var orchestrator = CreateV033Orchestrator();

            // Prepare is read-only except for the existing update-plan receipt.
            // No materialization/source/build/launch effect occurs before preview.
            var preview = await orchestrator.PrepareAsync(
                dialog.FileName,
                WorkspaceRootBox.Text,
                CancellationToken.None);

            var message = new StringBuilder();
            message.AppendLine("Запустить единый maintenance-сеанс Update candidate?");
            message.AppendLine();
            message.AppendLine($"Package: {preview.PackageFileName}");
            message.AppendLine($"SHA-256: {preview.PackageSha256}");
            message.AppendLine($"Predecessor: {preview.PredecessorCommit} / {preview.PredecessorTag}");
            message.AppendLine($"Target: {preview.TargetVersion} / {preview.TargetTag}");
            message.AppendLine($"Payload: {preview.PreviewPlan.PayloadFileCount} files; {preview.PreviewPlan.PayloadBytes} bytes");
            message.AppendLine();
            message.AppendLine("После подтверждения Workbench последовательно вызовет уже существующие typed gates: fresh plan → staging materialization → staged apply plan → exact source apply/build.");
            message.AppendLine("Каждый sub-gate повторно проверяет собственные evidence/predecessor bytes и сохраняет отдельный receipt. Apply/build rollback остаётся в существующем BoundedUpdateApplyBuildService.");
            message.AppendLine();
            message.AppendLine("Этот сеанс НЕ запускает candidate, Self-test, Принять или Publish accepted. После успешной сборки отдельная кнопка «Запустить candidate» будет включена и потребует отдельного подтверждения exact executable SHA-256.");
            message.AppendLine("Agent Execute, ActionPermit, catalog mutation, Git publication и general network authority не создаются.");

            if (MessageBox.Show(this, message.ToString(), "Update candidate v0.33", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            BeginRun(id);
            beganRun = true;
            UpdateCandidateButton.IsEnabled = false;
            StatusText.Text = "RUNNING: v0.33 typed update orchestration (stops before launch)";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  update.orchestrator.started target={preview.TargetVersion}; oneUiIntent=true; launch=false");

            var receipt = await orchestrator.ExecuteConfirmedAsync(
                preview,
                WorkspaceRootBox.Text,
                _cts!.Token);
            var receiptPath = await MaintenanceUpdateOrchestratorService.WriteReceiptAsync(
                WorkspaceRootBox.Text,
                receipt,
                _cts.Token);

            // Expose the exact existing typed sub-receipts to the existing UI state.
            // Launch still consumes only the existing ApplyBuild receipt via its own gate.
            _lastUpdatePlanReceipt = receipt.FreshPlan;
            _lastUpdatePlanArtifactPath = receipt.FreshPlanArtifactPath;
            _lastUpdatePackagePath = receipt.PackagePath;
            _lastUpdatePlanConsumed = true;
            _lastMaterializationReceipt = receipt.Materialization;
            _lastMaterializationArtifactPath = receipt.MaterializationArtifactPath;
            _lastStagedApplyPlanReceipt = receipt.StagedApplyPlan;
            _lastStagedApplyPlanArtifactPath = receipt.StagedApplyPlanArtifactPath;
            _lastApplyBuildReceipt = receipt.ApplyBuild;
            _lastApplyBuildArtifactPath = receipt.ApplyBuildArtifactPath;
            LaunchCandidateButton.IsEnabled = true;

            UpdatePlanTextBox.Text = CommandCodec.Serialize(new
            {
                Orchestration = receipt,
                OrchestrationReceiptPath = receiptPath,
                LaunchCandidateAvailable = true,
                LaunchPerformed = false
            });
            OutputTabs.SelectedItem = UpdatePlanTab;
            ProgressBar.Value = 100;
            _currentTerminalState = CommandTerminalState.Completed;
            StatusText.Text = $"COMPLETED: candidate built for {receipt.TargetVersion}; separate Launch candidate required";
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  update.orchestrator.completed status={receipt.Status}; typedSubReceipts={receipt.TypedSubReceiptsPreserved}; launch=false; {receiptPath}");
        }
        catch (OperationCanceledException)
        {
            ResetV033UpdateState();
            ShowCancelled();
        }
        catch (InvalidDataException ex)
        {
            ResetV033UpdateState();
            ShowInvalid(ex);
        }
        catch (Exception ex)
        {
            ResetV033UpdateState();
            ShowFailure(ex);
        }
        finally
        {
            if (beganRun) EndRun();
            UpdateCandidateButton.IsEnabled = true;
            // EndRun knows only the historical update buttons. Preserve the v0.33
            // successful separate-launch decision after EndRun completes.
            if (_lastApplyBuildReceipt is not null &&
                string.Equals(_lastApplyBuildReceipt.Status, "CANDIDATE_BUILT_SEPARATE_LAUNCH_AUTHORITY_REQUIRED", StringComparison.Ordinal))
                LaunchCandidateButton.IsEnabled = true;
        }
    }

    private void ResetV033UpdateState()
    {
        _lastUpdatePlanReceipt = null;
        _lastUpdatePlanArtifactPath = null;
        _lastUpdatePackagePath = null;
        _lastUpdatePlanConsumed = true;
        _lastMaterializationReceipt = null;
        _lastMaterializationArtifactPath = null;
        _lastStagedApplyPlanReceipt = null;
        _lastStagedApplyPlanArtifactPath = null;
        _lastApplyBuildReceipt = null;
        _lastApplyBuildArtifactPath = null;
        LaunchCandidateButton.IsEnabled = false;
    }
}
