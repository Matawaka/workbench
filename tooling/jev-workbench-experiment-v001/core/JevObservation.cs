namespace Matawaka.Workbench.JevLab;

public sealed record JevSignal(string Id, string Title, double Probability, bool ResearchOnly, string Question = "");

/// <summary>An offline display view. Integrity is not provider authenticity or reference admission.</summary>
public sealed class JevObservation
{
    public string CaseId { get; }
    public string SampleClass { get; }
    public string RequestedModel { get; }
    public string ObservedModel { get; }
    public string Provider { get; }
    public string CandidateRawSha256 { get; }
    public string ReceiptRawSha256 { get; }
    public string RequestDigest { get; }
    public string ContextJson { get; }
    public IReadOnlyList<JevSignal> Signals { get; }
    public string ReferenceStatus => "NOT_ASSESSED";
    public string IntegritySummary => "RAW_HASHES_AND_CANDIDATE_REQUEST_RECEIPT_BINDINGS_MATCHED; " +
        "MANIFEST_DECLARATIONS_NOT_AUTHENTICATED; RESPONSE_LEASE_AND_SOURCE_ORIGINALS_NOT_VERIFIED; " +
        "NO_REFERENCE_ADMISSION; NO_AUTHORITY";

    internal JevObservation(string caseId, string sampleClass, string requestedModel, string observedModel,
        string provider, string candidateRawSha256, string receiptRawSha256, string requestDigest,
        string contextJson, IEnumerable<JevSignal> signals)
    {
        CaseId = caseId;
        SampleClass = sampleClass;
        RequestedModel = requestedModel;
        ObservedModel = observedModel;
        Provider = provider;
        CandidateRawSha256 = candidateRawSha256;
        ReceiptRawSha256 = receiptRawSha256;
        RequestDigest = requestDigest;
        ContextJson = contextJson;
        Signals = Array.AsReadOnly(signals.ToArray());
    }
}
