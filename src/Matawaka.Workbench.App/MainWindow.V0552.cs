namespace Matawaka.Workbench.App;

public partial class MainWindow
{
    internal void ConfigureV0552Routing()
    {
        // Reuse the accepted v0.55 model-invocation operator route unchanged.
        ConfigureV055Routing();
        Title = "Matawaka Workbench v0.55.2";
        OperatorSurfaceV045Contract.Apply(this);
        RefreshInstalledAppsV044();
        InstallV0441TreeDoubleClickRouting();
    }

    internal IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> ObserveV0552RoutingCompatibilityContract() => new[]
    {
        ("routing-v0552-title", Title == "Matawaka Workbench v0.55.2", Title, "Matawaka Workbench v0.55.2"),
        ("routing-v0552-model-authority-class", BoundedLocalModelInvocationV055Service.RequestSchema == "matawaka.local-model-invocation-request/v0.55",
            BoundedLocalModelInvocationV055Service.RequestSchema, "accepted v0.55 model-request schema unchanged"),
        ("routing-v0552-model-output-class", BoundedLocalModelInvocationV055Service.PortableResultSchema == "matawaka.local-model-output/v0.55",
            BoundedLocalModelInvocationV055Service.PortableResultSchema, "UNTRUSTED_LOCAL_MODEL_OUTPUT remains separate from response/display authority"),
        ("routing-v0552-public-provenance-class", ProvenanceBoundRuntimeExecutionV055Service.SourceBindingSchema == "matawaka.runtime-execution-source-binding/v0.55",
            ProvenanceBoundRuntimeExecutionV055Service.SourceBindingSchema, "distinct public #82 runtime-execution provenance schema"),
        ("routing-v0552-source-not-model-authority", ProvenanceBoundRuntimeExecutionV055Service.SourceAuthorityEffect == "NONE_BY_SOURCE_RECORD",
            ProvenanceBoundRuntimeExecutionV055Service.SourceAuthorityEffect, "NONE_BY_SOURCE_RECORD"),
        ("routing-v0552-no-historical-title-reinterpretation", true,
            "successor checks v0.55 semantic contracts directly and checks its own title separately",
            "historical routing-v055-title assertion remains historical")
    };
}
