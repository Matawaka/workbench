using Matawaka.Workbench.Protocol;
using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    /// <summary>
    /// Display-only v0.60 wiring over an already-produced CommandResult.
    /// The command/provider/SemanticHost authority path has completed before this
    /// method is called. Presentation cannot create or revise authority.
    /// </summary>
    public bool TryShowLiveCapabilityEvidenceV060(CommandResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Authority is not CapabilityReceipt authority ||
            result.CapabilityEvidence is not LiveCapabilityEvidenceAuditReceiptV058 audit)
        {
            ClearLiveCapabilityEvidenceV060();
            return false;
        }

        try
        {
            // Reuse the exact human-reviewed v0.59 presentation and validation.
            ShowCapabilityEvidenceReviewV059(authority, audit);
            if (_capabilityEvidenceReviewTabV059 is null)
                throw new InvalidDataException("v0.60 live authority/evidence presentation did not create a review tab.");

            OutputTabs.SelectedItem = _capabilityEvidenceReviewTabV059;
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  authority-evidence.displayed  live=true; authoritySource=baseCapabilityReceipt; auditAuthority=false; retry=false");
            return true;
        }
        catch (InvalidDataException ex)
        {
            ClearLiveCapabilityEvidenceV060();
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  authority-evidence.rejected   live=true; terminalStateUnchanged=true; retry=false; {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            ClearLiveCapabilityEvidenceV060();
            EventList.Items.Add($"{DateTime.Now:HH:mm:ss}  authority-evidence.failed     live=true; terminalStateUnchanged=true; retry=false; {ex.Message}");
            return false;
        }
    }

    private void ClearLiveCapabilityEvidenceV060()
    {
        if (_capabilityEvidenceReviewTabV059 is not null &&
            OutputTabs.Items.Contains(_capabilityEvidenceReviewTabV059))
        {
            OutputTabs.Items.Remove(_capabilityEvidenceReviewTabV059);
        }
        _capabilityEvidenceReviewTabV059 = null;
    }
}
