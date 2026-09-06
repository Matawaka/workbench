using Matawaka.Workbench.Protocol;

namespace Matawaka.Workbench.App;

public enum ProvenanceReviewLanguageV0561
{
    Russian,
    English
}

public sealed record ProvenanceReviewFactV0561(string Label, string Value, string Explanation);

public sealed record ProvenanceReviewPresentationV0561(
    string LanguageCode,
    string TabHeader,
    string Headline,
    string Summary,
    string ObservedSectionTitle,
    string BoundariesSectionTitle,
    string EvidenceSectionTitle,
    string CopyHint,
    IReadOnlyList<ProvenanceReviewFactV0561> ObservedFacts,
    IReadOnlyList<ProvenanceReviewFactV0561> Boundaries,
    IReadOnlyList<ProvenanceReviewFactV0561> EvidenceSource,
    string HumanReviewPrompt);

public static class ProvenanceReviewPresentationServiceV0561
{
    public const string Headline = "PROVENANCE OBSERVED — NO AUTHORITY";
    public const string RussianHeadline = "ПРОВЕНАНС ЗАФИКСИРОВАН — ПОЛНОМОЧИЙ НЕТ";
    public const string UnsignedTag = "UNSIGNED / NOT VERIFIED";
    public const string NotEstablished = "NOT ESTABLISHED";
    public const string NotCreated = "NOT CREATED";
    public const string ExpectedDecision = "PROVENANCE_OBSERVED_NO_AUTHORITY";
    public const string ExpectedReleaseCommit = "ea852feeb0e8d92a8977bb251693e7e977913dca";

    public static ProvenanceReviewPresentationV0561 Create(
        ProvenanceAdmissionReceipt receipt,
        ProvenanceReviewLanguageV0561 language = ProvenanceReviewLanguageV0561.Russian)
    {
        ValidateReceipt(receipt);
        return language == ProvenanceReviewLanguageV0561.Russian
            ? CreateRussian(receipt)
            : CreateEnglish(receipt);
    }

    private static void ValidateReceipt(ProvenanceAdmissionReceipt receipt)
    {
        if (!string.Equals(receipt.Decision, ExpectedDecision, StringComparison.Ordinal))
            throw new InvalidDataException($"Unexpected provenance admission decision: {receipt.Decision}.");
        if (!receipt.C2paExternalReferenceBindingEstablished)
            throw new InvalidDataException("C2PA external-reference binding was not established.");
        if (!receipt.ImmutableExternalResolutionExactBytesMatched)
            throw new InvalidDataException("Immutable external evidence did not match exact bytes.");
        if (!receipt.LiveC2paValidationAccepted)
            throw new InvalidDataException("Live C2PA validation was not accepted.");
        if (receipt.GitTagSignatureVerified)
            throw new InvalidDataException("Unsigned Workbench tag must not be presented as cryptographically verified.");
        if (!string.Equals(receipt.ObservedWorkbenchReleaseCommit, ExpectedReleaseCommit, StringComparison.Ordinal))
            throw new InvalidDataException("Unexpected observed Workbench release commit.");

        if (receipt.AuthorityCreated ||
            receipt.NetworkAccessPerformed ||
            receipt.ProcessStarted ||
            receipt.ModelInvocationPerformed ||
            receipt.RuntimeExecutionPerformed ||
            receipt.ResponseAuthorityCreated ||
            receipt.DisplayPermitCreated ||
            receipt.ActionPermitCreated ||
            receipt.SuccessorPermitCreated)
            throw new InvalidDataException("Provenance admission contains an authority/effect promotion and cannot be presented as review-only evidence.");
    }

    private static ProvenanceReviewPresentationV0561 CreateRussian(ProvenanceAdmissionReceipt receipt) => new(
        "ru",
        "Русский",
        RussianHeadline,
        "Криптографическая связь происхождения принята только как доказательство. Этот экран не подтверждает доверие, истинность, разрешение или полномочие на действие.",
        "Что было зафиксировано",
        "Что НЕ было установлено или создано",
        "Источник доказательства",
        "Точные идентификаторы ниже можно выделить и скопировать (Ctrl+C).",
        new[]
        {
            new ProvenanceReviewFactV0561("C2PA external-reference", "ХЕШ-СВЯЗЬ ЗАФИКСИРОВАНА", "Принятое доказательство сообщает об одной стандартной связи c2pa.external-reference."),
            new ProvenanceReviewFactV0561("Внешние данные", "ТОЧНОЕ СОВПАДЕНИЕ", "Неизменяемый внешний источник совпал с ожидаемыми байтами доказательства."),
            new ProvenanceReviewFactV0561("Проверка C2PA", "ПРИНЯТА", "Принятое доказательство сообщает об успешной live-проверке C2PA."),
            new ProvenanceReviewFactV0561("Наблюдаемая версия Workbench", receipt.ObservedWorkbenchReleaseCommit, "Зафиксирована только идентичность версии; это не создаёт полномочий на выпуск.")
        },
        new[]
        {
            new ProvenanceReviewFactV0561("Подпись Git-тега", "НЕ ПОДПИСАН / НЕ ПРОВЕРЕН (UNSIGNED / NOT VERIFIED)", "Аннотированный тег Workbench зафиксирован как неподписанный. Проверенная криптографическая подпись Git-тега не заявляется."),
            new ProvenanceReviewFactV0561("Истинность", "НЕ УСТАНОВЛЕНА (NOT ESTABLISHED)", "Корректная связь происхождения не подтверждает истинность лежащих в основе утверждений."),
            new ProvenanceReviewFactV0561("Полномочие на публикацию", "НЕ УСТАНОВЛЕНО (NOT ESTABLISHED)", "Публикация в репозитории и провенанс не устанавливают, кто был уполномочен публиковать."),
            new ProvenanceReviewFactV0561("Полномочия runtime / модели", "НЕ СОЗДАНЫ (NOT CREATED)", "Это наблюдение провенанса не создаёт полномочий на запуск runtime или model-request."),
            new ProvenanceReviewFactV0561("Полномочия ответа / отображения", "НЕ СОЗДАНЫ (NOT CREATED)", "Это наблюдение провенанса не создаёт разрешения на ответ или отображение."),
            new ProvenanceReviewFactV0561("Разрешения действия / successor", "НЕ СОЗДАНЫ (NOT CREATED)", "Это наблюдение провенанса не создаёт разрешения на внешнее действие или successor-переход.")
        },
        new[]
        {
            new ProvenanceReviewFactV0561("SHA-256 доказательства", receipt.ObservedEvidenceSha256, "Точный digest принятого доказательства."),
            new ProvenanceReviewFactV0561("Источник доказательства", $"{receipt.EvidenceBinding.Repository}@{receipt.EvidenceBinding.Frontier}", "Зафиксированный frontier источника принятого доказательства."),
            new ProvenanceReviewFactV0561("Путь доказательства", receipt.EvidenceBinding.Path, "Зафиксированный путь qualification receipt в репозитории."),
            new ProvenanceReviewFactV0561("Решение admission", receipt.Decision, "Workbench классифицировал переданное доказательство только как наблюдение, без полномочий.")
        },
        "Проверка человеком: понятно ли примерно за пять секунд, что провенанс зафиксирован, но истинность, доверие и полномочия НЕ предоставлены?");

    private static ProvenanceReviewPresentationV0561 CreateEnglish(ProvenanceAdmissionReceipt receipt) => new(
        "en",
        "English",
        Headline,
        "A cryptographic provenance relationship was admitted as evidence. This screen does not grant trust, truth, permission, or authority to act.",
        "What was observed",
        "What was NOT established or created",
        "Evidence source",
        "Exact identifiers below can be selected and copied (Ctrl+C).",
        new[]
        {
            new ProvenanceReviewFactV0561("C2PA external-reference", "HASH BINDING OBSERVED", "The admitted evidence reports one standard c2pa.external-reference binding."),
            new ProvenanceReviewFactV0561("External evidence bytes", "EXACT MATCH", "The immutable external resolution matched the expected evidence bytes."),
            new ProvenanceReviewFactV0561("Live C2PA validation", "ACCEPTED", "The accepted evidence reports a successful live C2PA validation surface."),
            new ProvenanceReviewFactV0561("Workbench release observed", receipt.ObservedWorkbenchReleaseCommit, "Observed release identity only; this does not create release authority.")
        },
        new[]
        {
            new ProvenanceReviewFactV0561("Git tag signature", UnsignedTag, "The annotated Workbench tag was observed as unsigned. No verified Git tag signature is claimed."),
            new ProvenanceReviewFactV0561("Truth", NotEstablished, "A valid provenance relationship does not certify the truth of the underlying claims."),
            new ProvenanceReviewFactV0561("Publication authority", NotEstablished, "Repository publication and provenance do not establish who was authorized to publish."),
            new ProvenanceReviewFactV0561("Runtime / model authority", NotCreated, "No runtime execution or model-request authority is created by this provenance observation."),
            new ProvenanceReviewFactV0561("Response / display authority", NotCreated, "No response or display permission is created by this provenance observation."),
            new ProvenanceReviewFactV0561("Action / successor permits", NotCreated, "No external action or successor permission is created by this provenance observation.")
        },
        new[]
        {
            new ProvenanceReviewFactV0561("Evidence SHA-256", receipt.ObservedEvidenceSha256, "Exact admitted evidence digest."),
            new ProvenanceReviewFactV0561("Evidence source", $"{receipt.EvidenceBinding.Repository}@{receipt.EvidenceBinding.Frontier}", "Pinned source frontier for the admitted evidence."),
            new ProvenanceReviewFactV0561("Evidence path", receipt.EvidenceBinding.Path, "Pinned repository path of the admitted qualification receipt."),
            new ProvenanceReviewFactV0561("Admission decision", receipt.Decision, "Workbench classified the supplied evidence as observation-only, with no authority.")
        },
        "Human review: within five seconds, is it unmistakable that provenance was observed while truth, trust, and authority were NOT granted?");
}
