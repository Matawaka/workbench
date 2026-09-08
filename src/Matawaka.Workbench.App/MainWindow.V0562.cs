namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    internal void ConfigureV0562Routing()
    {
        // Keep the accepted v0.55 operator/runtime route, then add the already-merged
        // provenance review surface separately from any publication authority.
        ConfigureV0552Routing();
        Title = "Matawaka Workbench v0.56.2";

        // The historical in-app v0.55.2 publisher is not a successor publication route.
        // v0.56.2 accepted publication is a separate fixed GitHub Actions gate.
        PublishAcceptedButton.IsEnabled = false;
        PublishAcceptedButton.IsHitTestVisible = false;
        PublishAcceptedButton.ToolTip = "v0.56.2 accepted publication is available only through the fixed human-confirmed GitHub Actions gate.";

        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
    }

    internal IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> ObserveV0562RoutingContract() => new[]
    {
        ("routing-v0562-title", Title == "Matawaka Workbench v0.56.2", Title, "Matawaka Workbench v0.56.2"),
        ("routing-v0562-historical-publisher-disabled", !PublishAcceptedButton.IsEnabled && !PublishAcceptedButton.IsHitTestVisible,
            $"enabled={PublishAcceptedButton.IsEnabled}; hitTest={PublishAcceptedButton.IsHitTestVisible}", "disabled; no in-app successor publication route"),
        ("routing-v0562-target-tag", QualifiedV0561EvidenceV0562.TargetAcceptedTag == "workbench-v0.56.2-accepted",
            QualifiedV0561EvidenceV0562.TargetAcceptedTag, "fresh fixed accepted tag"),
        ("routing-v0562-provenance-no-authority", ProvenanceReviewPresentationServiceV0561.ExpectedDecision == QualifiedV0561EvidenceV0562.Decision,
            ProvenanceReviewPresentationServiceV0561.ExpectedDecision, QualifiedV0561EvidenceV0562.Decision),
        ("routing-v0562-model-authority-class-unchanged", BoundedLocalModelInvocationV055Service.RequestSchema == "matawaka.local-model-invocation-request/v0.55",
            BoundedLocalModelInvocationV055Service.RequestSchema, "accepted v0.55 model-request schema unchanged"),
        ("routing-v0562-publication-separate", true,
            "normal UI exposes no v0.56.2 publish action; publication requires separate human-confirmed workflow_dispatch",
            "acceptance/qualification != publication")
    };
}
