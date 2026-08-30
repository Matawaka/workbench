using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Matawaka.Workbench.Protocol;

/// <summary>
/// Workbench-local compatibility projection over the PCL v0.1 progress fields.
/// It does not execute the UU-AAP JavaScript implementation and does not claim
/// canonical conformance. The exact source frontier is carried in every receipt.
/// </summary>
public static class PclCompatibleProgress
{
    public const string UuAapFrontier = "f5673a39ddeef05f82c828f6cff554518f5f8ef6";

    public static readonly ProtocolSourceBinding ProgressSource = new(
        "Matawaka/uu-aap",
        UuAapFrontier,
        "protocols/experimental/perceived-causal-liveness/v0.1/progress.js",
        "8dc15b67a6d52ef64b0686e81a86a3ad676467a5",
        "field-semantics-compatible; canonical implementation not executed");

    public static readonly ProtocolSourceBinding HumanViewSource = new(
        "Matawaka/uu-aap",
        UuAapFrontier,
        "protocols/experimental/perceived-causal-liveness/v0.1/human-view.js",
        "9a721356e0da656b45fb9c2527c720fa71ae2d5f",
        "human-view-compatible; canonical implementation not executed");

    public static readonly ProtocolSourceBinding ScopedAuthoritySource = new(
        "Matawaka/uu-aap",
        UuAapFrontier,
        "protocols/integration/scoped-authority-evidence/v0.1/authority-evidence.js",
        "f654753b14335745d7ea231aa4d722990a191321",
        "reference-only; evaluator not executed by Workbench v0.5");

    public static readonly ProtocolSourceBinding MaterializationAuthoritySource = new(
        "Matawaka/uu-aap",
        UuAapFrontier,
        "protocols/integration/materialization-authority/v0.1/materialization-authority.js",
        "7d8b367d825a0729190d789bf75b68f976767ec4",
        "reference-only; evaluator not executed by Workbench v0.5");

    public static readonly ProtocolSourceBinding ReusableAdmissionAuditSource = new(
        "Matawaka/uu-aap",
        UuAapFrontier,
        "protocols/integration/reusable-component-admission-audit/v0.1/assessment.json",
        "e0efb6ad4b65e91360d039dc8842cafafc5079bb",
        "reference-only; NO_ADMISSION guard against premature shared abstraction");

    public static bool IsTrackable(WorkbenchProgress progress)
        => progress.Phase is not null ||
           progress.ProgressKind is not null ||
           progress.WaitingOn is not null ||
           progress.NextObservableEvent is not null ||
           progress.CheckpointRef is not null;

    public static WorkbenchProgressReceipt Create(
        WorkbenchProgress input,
        WorkbenchProgressReceipt? previous,
        int runEpoch)
    {
        var meaningful = previous is null ||
            !Equals(previous.CurrentPhase, input.Phase) ||
            !Equals(previous.ProgressKind, input.ProgressKind) ||
            !Equals(previous.WaitingOn, input.WaitingOn) ||
            !Equals(previous.NextObservableEvent, input.NextObservableEvent) ||
            !Equals(previous.CheckpointRef, input.CheckpointRef);

        var projection = new
        {
            run_id = input.CommandId,
            run_epoch = runEpoch,
            observed_at = input.Timestamp,
            current_phase = input.Phase,
            progress_kind = input.ProgressKind,
            waiting_on = input.WaitingOn,
            next_observable_event = input.NextObservableEvent,
            checkpoint_ref = input.CheckpointRef,
            meaningful_progress = meaningful
        };

        var json = JsonSerializer.Serialize(projection);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();

        return new WorkbenchProgressReceipt(
            "matawaka.workbench.progress-receipt/v0.5",
            ProgressSource,
            input.CommandId,
            runEpoch,
            input.Timestamp,
            input.Phase,
            input.ProgressKind,
            input.WaitingOn,
            input.NextObservableEvent,
            input.CheckpointRef,
            meaningful,
            meaningful,
            false,
            false,
            digest);
    }

    public static WorkbenchHumanLivenessView ToHumanView(
        WorkbenchProgressReceipt receipt,
        CommandTerminalState? terminalState = null)
    {
        var terminal = terminalState.HasValue;
        var state = terminalState?.ToString().ToUpperInvariant() ?? "RUNNING";
        var nextSafeAction = terminalState switch
        {
            CommandTerminalState.Completed => "STOP; RUN CLOSED",
            CommandTerminalState.Denied => "REVISE AUTHORITY REQUEST OR STOP; NO EXTERNAL EFFECT",
            CommandTerminalState.Invalid => "CORRECT INPUT BEFORE A NEW RUN",
            CommandTerminalState.Failed => "INSPECT FAILURE; NO AUTOMATIC RETRY OR EXTERNAL EFFECT",
            CommandTerminalState.Cancelled => "STOP; START A FRESH RUN IF NEEDED",
            _ when string.Equals(receipt.WaitingOn, "AUTHORITY_DECISION", StringComparison.OrdinalIgnoreCase)
                => "WAIT FOR AUTHORITY DECISION",
            _ => "WAIT FOR OBSERVABLE PROGRESS"
        };

        return new WorkbenchHumanLivenessView(
            "matawaka.workbench.human-liveness-view/v0.5",
            HumanViewSource,
            receipt.RunId,
            receipt.RunEpoch,
            state,
            receipt.MeaningfulProgress ? receipt.ObservedAt : null,
            receipt.CurrentPhase,
            receipt.WaitingOn,
            receipt.NextObservableEvent,
            terminal,
            nextSafeAction,
            false,
            false);
    }
}
