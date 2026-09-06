using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Matawaka.Workbench.AgentHost;
using Matawaka.Workbench.Protocol;
using Matawaka.Workbench.Runtime;

namespace Matawaka.Workbench.App;

public enum CapabilityEvidenceReviewLanguageV059
{
    Russian,
    English
}

public sealed record CapabilityEvidenceReviewFactV059(
    string Label,
    string Value,
    string Explanation,
    bool Copyable = false);

public sealed record CapabilityEvidenceReviewPresentationV059(
    string Language,
    string TabHeader,
    string Headline,
    string Summary,
    string PolicySectionTitle,
    IReadOnlyList<CapabilityEvidenceReviewFactV059> PolicyFacts,
    string AuditSectionTitle,
    IReadOnlyList<CapabilityEvidenceReviewFactV059> AuditFacts,
    string NonEffectsSectionTitle,
    IReadOnlyList<CapabilityEvidenceReviewFactV059> NonEffects,
    string TechnicalSectionTitle,
    IReadOnlyList<CapabilityEvidenceReviewFactV059> TechnicalFacts,
    string CopyHint,
    string HumanReviewPrompt);

/// <summary>
/// Human-facing projection of an already-produced base capability receipt and an
/// audit-only v0.58 capability-evidence receipt. Presentation never participates
/// in policy evaluation and never creates authority.
/// </summary>
public static class CapabilityEvidenceReviewPresentationServiceV059
{
    public static CapabilityEvidenceReviewPresentationV059 Create(
        CapabilityReceipt authority,
        LiveCapabilityEvidenceAuditReceiptV058 audit,
        CapabilityEvidenceReviewLanguageV059 language)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(audit);
        Validate(authority, audit);

        var decision = authority.Decision;
        var composition = audit.Composition;
        var allowed = string.Equals(decision.Decision, "allow", StringComparison.OrdinalIgnoreCase);
        var russian = language == CapabilityEvidenceReviewLanguageV059.Russian;

        var headline = russian
            ? allowed
                ? "ПОЛИТИКА РАЗРЕШИЛА ТОЛЬКО ЧТЕНИЕ — ПРОВЕРКА СВЕДЕНИЙ ПОЛНОМОЧИЙ НЕ ДОБАВИЛА"
                : "ПОЛИТИКА ОТКАЗАЛА — ПРОВЕРКА СВЕДЕНИЙ НЕ МОЖЕТ ИЗМЕНИТЬ ОТКАЗ"
            : allowed
                ? "POLICY ALLOWED READ-ONLY — EVIDENCE REVIEW ADDED NO AUTHORITY"
                : "POLICY DENIED — EVIDENCE REVIEW CANNOT OVERRIDE THE DENIAL";

        var policyFacts = russian
            ? new[]
            {
                new CapabilityEvidenceReviewFactV059("Решение политики", allowed ? "РАЗРЕШЕНО" : "ОТКАЗ", "Это базовое решение, которое определяет допустимость операции."),
                new CapabilityEvidenceReviewFactV059("Полномочие", FriendlyAuthority(decision.AuthorityGranted, true), "Именно это полномочие выдала policy; сведения о происхождении его не расширяют."),
                new CapabilityEvidenceReviewFactV059("Бюджет изменений", decision.MutationBudgetGranted.ToString(), "0 означает отсутствие разрешения на изменения."),
                new CapabilityEvidenceReviewFactV059("Сеть", YesNo(decision.NetworkAccessGranted, true), "Сетевой доступ не появляется из-за проверки сведений."),
                new CapabilityEvidenceReviewFactV059("Произвольный процесс", YesNo(decision.ArbitraryProcessExecutionGranted, true), "Проверка сведений не создаёт право запускать произвольные процессы.")
            }
            : new[]
            {
                new CapabilityEvidenceReviewFactV059("Policy decision", allowed ? "ALLOWED" : "DENIED", "This is the base decision that controls whether the operation is permitted."),
                new CapabilityEvidenceReviewFactV059("Authority", FriendlyAuthority(decision.AuthorityGranted, false), "This is the authority granted by policy; origin evidence does not widen it."),
                new CapabilityEvidenceReviewFactV059("Mutation budget", decision.MutationBudgetGranted.ToString(), "0 means no mutation permission."),
                new CapabilityEvidenceReviewFactV059("Network", YesNo(decision.NetworkAccessGranted, false), "Network access is not created by evidence review."),
                new CapabilityEvidenceReviewFactV059("Arbitrary process", YesNo(decision.ArbitraryProcessExecutionGranted, false), "Evidence review does not grant arbitrary process execution.")
            };

        var evidenceDecision = composition?.EvidenceContext.EvidenceDecision ?? "EVIDENCE_REJECTED";
        var evidenceRole = composition?.EvidenceContext.EvidenceRole ?? "NO_ACCEPTED_EVIDENCE_CONTEXT";
        var auditFacts = russian
            ? new[]
            {
                new CapabilityEvidenceReviewFactV059("Статус проверки", FriendlyAuditStatus(audit.Status, true), "Это результат дополнительной проверки сведений, а не новое разрешение."),
                new CapabilityEvidenceReviewFactV059("Решение по сведениям", evidenceDecision, "PROVENANCE_OBSERVED_NO_AUTHORITY означает: происхождение зафиксировано, полномочий не создано."),
                new CapabilityEvidenceReviewFactV059("Роль сведений", FriendlyEvidenceRole(evidenceRole, true), "Сведения могут быть контекстом/ограничением, но не источником allow."),
                new CapabilityEvidenceReviewFactV059("Изменено базовое решение", YesNo(audit.BaseDecisionChanged, true), "Должно оставаться НЕТ."),
                new CapabilityEvidenceReviewFactV059("Изменены полномочия provider", YesNo(audit.ProviderAuthorityChanged, true), "Должно оставаться НЕТ."),
                new CapabilityEvidenceReviewFactV059("Изменён semantic authority contract", YesNo(audit.SemanticAuthorityContractChanged, true), "Должно оставаться НЕТ.")
            }
            : new[]
            {
                new CapabilityEvidenceReviewFactV059("Review status", FriendlyAuditStatus(audit.Status, false), "This is supplemental evidence review, not a new permission."),
                new CapabilityEvidenceReviewFactV059("Evidence decision", evidenceDecision, "PROVENANCE_OBSERVED_NO_AUTHORITY means origin was recorded and no authority was created."),
                new CapabilityEvidenceReviewFactV059("Evidence role", FriendlyEvidenceRole(evidenceRole, false), "Evidence may constrain or explain context, but it is not an allow source."),
                new CapabilityEvidenceReviewFactV059("Base decision changed", YesNo(audit.BaseDecisionChanged, false), "Must remain NO."),
                new CapabilityEvidenceReviewFactV059("Provider authority changed", YesNo(audit.ProviderAuthorityChanged, false), "Must remain NO."),
                new CapabilityEvidenceReviewFactV059("Semantic authority contract changed", YesNo(audit.SemanticAuthorityContractChanged, false), "Must remain NO.")
            };

        var nonEffects = russian
            ? new[]
            {
                new CapabilityEvidenceReviewFactV059("Полномочие модели", YesNo(audit.ModelInvocationAuthorityCreated, true), "Не создаётся."),
                new CapabilityEvidenceReviewFactV059("Полномочие runtime", YesNo(audit.RuntimeExecutionAuthorityCreated, true), "Не создаётся."),
                new CapabilityEvidenceReviewFactV059("ResponseAuthority", YesNo(audit.ResponseAuthorityCreated, true), "Не создаётся."),
                new CapabilityEvidenceReviewFactV059("DisplayPermit", YesNo(audit.DisplayPermitCreated, true), "Не создаётся."),
                new CapabilityEvidenceReviewFactV059("ActionPermit", YesNo(audit.ActionPermitCreated, true), "Не создаётся."),
                new CapabilityEvidenceReviewFactV059("SuccessorPermit", YesNo(audit.SuccessorPermitCreated, true), "Не создаётся.")
            }
            : new[]
            {
                new CapabilityEvidenceReviewFactV059("Model authority", YesNo(audit.ModelInvocationAuthorityCreated, false), "Not created."),
                new CapabilityEvidenceReviewFactV059("Runtime authority", YesNo(audit.RuntimeExecutionAuthorityCreated, false), "Not created."),
                new CapabilityEvidenceReviewFactV059("ResponseAuthority", YesNo(audit.ResponseAuthorityCreated, false), "Not created."),
                new CapabilityEvidenceReviewFactV059("DisplayPermit", YesNo(audit.DisplayPermitCreated, false), "Not created."),
                new CapabilityEvidenceReviewFactV059("ActionPermit", YesNo(audit.ActionPermitCreated, false), "Not created."),
                new CapabilityEvidenceReviewFactV059("SuccessorPermit", YesNo(audit.SuccessorPermitCreated, false), "Not created.")
            };

        var technical = new List<CapabilityEvidenceReviewFactV059>
        {
            new(russian ? "Policy" : "Policy", decision.Policy, russian ? "Точная identity policy, принявшей базовое решение." : "Exact identity of the policy that made the base decision.", true),
            new(russian ? "Request ID" : "Request ID", authority.Request.Id, russian ? "Точная identity запроса полномочий." : "Exact capability-request identity.", true),
            new(russian ? "Audit schema" : "Audit schema", audit.Schema, russian ? "Схема дополнительного audit receipt." : "Supplemental audit receipt schema.", true)
        };

        if (composition is not null)
        {
            technical.Add(new CapabilityEvidenceReviewFactV059(russian ? "Роль evidence" : "Evidence role", composition.EvidenceContext.EvidenceRole, russian ? "Точное машинное значение роли." : "Exact machine evidence-role value.", true));
            technical.Add(new CapabilityEvidenceReviewFactV059(russian ? "Repository" : "Repository", composition.EvidenceContext.EvidenceRepository, russian ? "Источник проверенных сведений." : "Repository that supplied the reviewed evidence.", true));
            technical.Add(new CapabilityEvidenceReviewFactV059(russian ? "Frontier" : "Frontier", composition.EvidenceContext.EvidenceFrontier, russian ? "Точный source frontier." : "Exact source frontier.", true));
            technical.Add(new CapabilityEvidenceReviewFactV059(russian ? "Path" : "Path", composition.EvidenceContext.EvidencePath, russian ? "Точный путь evidence artifact." : "Exact evidence artifact path.", true));
            technical.Add(new CapabilityEvidenceReviewFactV059(russian ? "SHA-256" : "SHA-256", composition.EvidenceContext.EvidenceSha256, russian ? "Точный digest проверенных bytes." : "Exact digest of the reviewed bytes.", true));
        }

        return new CapabilityEvidenceReviewPresentationV059(
            russian ? "ru" : "en",
            russian ? "Русский" : "English",
            headline,
            russian
                ? "Первая часть отвечает на вопрос «что разрешено политикой». Вторая — «какие сведения были проверены». Проверенные сведения могут ограничивать или объяснять контекст, но сами по себе не выдают разрешение."
                : "The first section answers what policy permitted. The second answers what evidence was reviewed. Reviewed evidence may constrain or explain context, but it does not grant permission by itself.",
            russian ? "1. ЧТО РАЗРЕШЕНО ПОЛИТИКОЙ" : "1. WHAT POLICY ALLOWED",
            policyFacts,
            russian ? "2. ЧТО ПОКАЗАЛА ПРОВЕРКА СВЕДЕНИЙ" : "2. WHAT THE EVIDENCE REVIEW SHOWED",
            auditFacts,
            russian ? "3. ЧЕГО ЭТА ПРОВЕРКА НЕ РАЗРЕШАЕТ" : "3. WHAT THIS REVIEW DOES NOT AUTHORIZE",
            nonEffects,
            russian ? "4. ТОЧНЫЕ ЗНАЧЕНИЯ" : "4. EXACT VALUES",
            technical,
            russian
                ? "Значения ниже не сокращаются: выделите строку и скопируйте её при необходимости."
                : "Values below are not truncated: select a line and copy it when needed.",
            russian
                ? "Проверка человеком: сразу ли понятно, что решение policy и проверка сведений — разные вещи, а жёлтая строка не сообщает о новом разрешении?"
                : "Human review: is it immediately clear that the policy decision and evidence review are different things, and that the yellow banner does not announce a new permission?");
    }

    private static void Validate(CapabilityReceipt authority, LiveCapabilityEvidenceAuditReceiptV058 audit)
    {
        if (!string.Equals(authority.Schema, "matawaka.capability-receipt/v1", StringComparison.Ordinal))
            throw new InvalidDataException("Unexpected capability receipt schema for v0.59 presentation.");
        if (!string.Equals(authority.Request.Id, authority.Decision.RequestId, StringComparison.Ordinal))
            throw new InvalidDataException("Capability request/decision identity mismatch in v0.59 presentation.");
        if (!string.Equals(audit.Schema, LiveCapabilityEvidenceAuditServiceV058.ReceiptSchema, StringComparison.Ordinal))
            throw new InvalidDataException("Unexpected live capability evidence audit schema.");
        if (audit.BaseDecisionChanged || audit.ProviderAuthorityChanged || audit.SemanticAuthorityContractChanged ||
            audit.AuthorityCreated || audit.ModelInvocationAuthorityCreated || audit.RuntimeExecutionAuthorityCreated ||
            audit.ResponseAuthorityCreated || audit.DisplayPermitCreated || audit.ActionPermitCreated || audit.SuccessorPermitCreated)
            throw new InvalidDataException("v0.59 refuses to present an audit receipt that reports authority promotion.");

        if (audit.Composition is null)
        {
            if (!string.Equals(audit.Status, LiveCapabilityEvidenceAuditServiceV058.RejectedStatus, StringComparison.Ordinal))
                throw new InvalidDataException("Missing composition is only valid for a rejected supplemental evidence audit.");
            return;
        }

        var composition = audit.Composition;
        if (!string.Equals(composition.Request.Id, authority.Request.Id, StringComparison.Ordinal) ||
            !string.Equals(composition.BaseDecision.RequestId, authority.Decision.RequestId, StringComparison.Ordinal) ||
            !string.Equals(composition.BaseDecision.Decision, authority.Decision.Decision, StringComparison.Ordinal) ||
            !string.Equals(composition.BaseDecision.Policy, authority.Decision.Policy, StringComparison.Ordinal) ||
            !string.Equals(composition.BaseDecision.AuthorityGranted, authority.Decision.AuthorityGranted, StringComparison.Ordinal) ||
            composition.BaseDecision.MutationBudgetGranted != authority.Decision.MutationBudgetGranted ||
            composition.BaseDecision.NetworkAccessGranted != authority.Decision.NetworkAccessGranted ||
            composition.BaseDecision.ArbitraryProcessExecutionGranted != authority.Decision.ArbitraryProcessExecutionGranted)
            throw new InvalidDataException("Audit composition is not bound to the supplied base capability decision.");

        if (composition.AuthorityIncreased || composition.MutationBudgetIncreased || composition.NetworkGrantIncreased ||
            composition.ArbitraryProcessGrantIncreased || composition.ModelAuthorityCreated || composition.RuntimeAuthorityCreated ||
            composition.ResponseAuthorityCreated || composition.DisplayPermitCreated || composition.ActionPermitCreated || composition.SuccessorPermitCreated)
            throw new InvalidDataException("v0.59 refuses to present a composition that reports authority promotion.");
    }

    private static string YesNo(bool value, bool russian)
        => russian ? (value ? "ДА" : "НЕТ") : (value ? "YES" : "NO");

    private static string FriendlyAuthority(string value, bool russian)
        => russian
            ? value.ToLowerInvariant() switch
            {
                "read-only" => "ТОЛЬКО ЧТЕНИЕ",
                "none" => "НЕТ",
                _ => value
            }
            : value.ToLowerInvariant() switch
            {
                "read-only" => "READ-ONLY",
                "none" => "NONE",
                _ => value.ToUpperInvariant()
            };

    private static string FriendlyAuditStatus(string value, bool russian)
        => russian
            ? value switch
            {
                LiveCapabilityEvidenceAuditServiceV058.ComposedStatus => "СВЕДЕНИЯ ПРИЛОЖЕНЫ — ПОЛНОМОЧИЯ НЕ ИЗМЕНЕНЫ",
                LiveCapabilityEvidenceAuditServiceV058.RejectedStatus => "СВЕДЕНИЯ ОТКЛОНЕНЫ — ПОЛНОМОЧИЯ НЕ ИЗМЕНЕНЫ",
                _ => value
            }
            : value switch
            {
                LiveCapabilityEvidenceAuditServiceV058.ComposedStatus => "EVIDENCE ATTACHED — AUTHORITY UNCHANGED",
                LiveCapabilityEvidenceAuditServiceV058.RejectedStatus => "EVIDENCE REJECTED — AUTHORITY UNCHANGED",
                _ => value
            };

    private static string FriendlyEvidenceRole(string value, bool russian)
        => string.Equals(value, ProvenanceCapabilityEvidenceComposer.EvidenceRole, StringComparison.Ordinal)
            ? russian ? "ТОЛЬКО КОНТЕКСТ/ОГРАНИЧЕНИЕ — НЕ ПОЛНОМОЧИЕ" : "CONTEXT/CONSTRAINT ONLY — NOT AUTHORITY"
            : value;
}

public partial class MainWindow
{
    public const string CapabilityEvidenceReviewOnlyArgumentV059 = "--capability-evidence-review-only";

    private TabItem? _capabilityEvidenceReviewTabV059;

    internal void ConfigureV059CapabilityEvidenceReviewRouting(bool reviewOnly)
    {
        if (!reviewOnly) return;

        var (authority, audit) = CreateCapabilityEvidenceReviewFixtureV059();
        ShowCapabilityEvidenceReviewV059(authority, audit);

        // Review-only mode is detached from boot/acceptance/publication and from
        // agent/provider/SemanticHost execution. It exists only for human semantic review.
        Loaded -= Window_LoadedV040;
        Loaded -= Window_LoadedV055;
        Loaded -= Window_LoadedV0552;
        Closing -= Window_Closing;

        PrimaryMaintenanceSurface.IsEnabled = false;
        PrimaryMaintenanceSurface.IsHitTestVisible = false;
        InstalledAppsList.IsEnabled = false;
        InstalledAppsList.IsHitTestVisible = false;
        HistoricalCompatibilityBindings.IsEnabled = false;
        HistoricalCompatibilityBindings.IsHitTestVisible = false;

        Title = "Matawaka Workbench — authority / evidence review candidate (read-only)";
        StatusText.Text = "READ-ONLY REVIEW — POLICY AUTHORITY ≠ EVIDENCE AUDIT";
        ProgressBar.Value = 0;
        if (_capabilityEvidenceReviewTabV059 is not null)
            OutputTabs.SelectedItem = _capabilityEvidenceReviewTabV059;
    }

    public void ShowCapabilityEvidenceReviewV059(
        CapabilityReceipt authority,
        LiveCapabilityEvidenceAuditReceiptV058 audit)
    {
        var russian = CapabilityEvidenceReviewPresentationServiceV059.Create(
            authority, audit, CapabilityEvidenceReviewLanguageV059.Russian);
        var english = CapabilityEvidenceReviewPresentationServiceV059.Create(
            authority, audit, CapabilityEvidenceReviewLanguageV059.English);

        if (_capabilityEvidenceReviewTabV059 is not null && OutputTabs.Items.Contains(_capabilityEvidenceReviewTabV059))
            OutputTabs.Items.Remove(_capabilityEvidenceReviewTabV059);

        _capabilityEvidenceReviewTabV059 = BuildCapabilityEvidenceReviewTabV059(russian, english);
        var authorityIndex = OutputTabs.Items.IndexOf(AuthorityTab);
        OutputTabs.Items.Insert(authorityIndex >= 0 ? authorityIndex + 1 : OutputTabs.Items.Count, _capabilityEvidenceReviewTabV059);
    }

    public static (CapabilityReceipt Authority, LiveCapabilityEvidenceAuditReceiptV058 Audit)
        CreateCapabilityEvidenceReviewFixtureV059()
    {
        var request = new CapabilityRequest(
            "matawaka.capability-request/v1",
            "v059-human-review:capability",
            "development-agent",
            "agent.observe",
            "observe",
            "v059-human-review-fixture",
            "read-only",
            0,
            false,
            false);
        var decision = new FreeShieldReadOnlyCapabilityPolicy().Decide(request, agentEnabled: true);
        var authority = new CapabilityReceipt("matawaka.capability-receipt/v1", request, decision);
        var audit = new LiveCapabilityEvidenceAuditServiceV058().Observe(request, decision);
        return (authority, audit);
    }

    private static TabItem BuildCapabilityEvidenceReviewTabV059(
        CapabilityEvidenceReviewPresentationV059 russian,
        CapabilityEvidenceReviewPresentationV059 english)
    {
        var languageTabs = new TabControl
        {
            SelectedIndex = 0,
            Margin = new Thickness(0)
        };
        languageTabs.Items.Add(BuildCapabilityEvidenceLanguageTabV059(russian));
        languageTabs.Items.Add(BuildCapabilityEvidenceLanguageTabV059(english));

        return new TabItem
        {
            Header = "Authority / Evidence",
            Content = languageTabs
        };
    }

    private static TabItem BuildCapabilityEvidenceLanguageTabV059(
        CapabilityEvidenceReviewPresentationV059 presentation)
    {
        var content = new StackPanel { Margin = new Thickness(18) };

        var bannerContent = new StackPanel();
        bannerContent.Children.Add(new TextBlock
        {
            Text = presentation.Headline,
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 7)
        });
        bannerContent.Children.Add(new TextBlock
        {
            Text = presentation.Summary,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 1120,
            HorizontalAlignment = HorizontalAlignment.Left
        });

        content.Children.Add(new Border
        {
            Background = Brushes.LightGoldenrodYellow,
            BorderBrush = Brushes.Goldenrod,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 16),
            Child = bannerContent
        });

        AddCapabilityEvidenceSectionV059(content, presentation.PolicySectionTitle, presentation.PolicyFacts, false, null);
        AddCapabilityEvidenceSectionV059(content, presentation.AuditSectionTitle, presentation.AuditFacts, false, null);
        AddCapabilityEvidenceSectionV059(content, presentation.NonEffectsSectionTitle, presentation.NonEffects, false, null);
        AddCapabilityEvidenceSectionV059(content, presentation.TechnicalSectionTitle, presentation.TechnicalFacts, true, presentation.CopyHint);

        var humanReview = new Border
        {
            BorderBrush = SystemColors.ControlDarkBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 10, 0, 0)
        };
        humanReview.Child = new TextBlock
        {
            Text = presentation.HumanReviewPrompt,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 1120
        };
        content.Children.Add(humanReview);

        return new TabItem
        {
            Header = presentation.TabHeader,
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = content
            }
        };
    }

    private static void AddCapabilityEvidenceSectionV059(
        Panel parent,
        string title,
        IReadOnlyList<CapabilityEvidenceReviewFactV059> facts,
        bool copyableValues,
        string? copyHint)
    {
        parent.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 10, 0, 7)
        });

        if (copyableValues && !string.IsNullOrWhiteSpace(copyHint))
        {
            parent.Children.Add(new TextBlock
            {
                Text = copyHint,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 7)
            });
        }

        var section = new StackPanel();
        foreach (var fact in facts)
        {
            var row = new Grid
            {
                Margin = new Thickness(0, 0, 0, 7),
                MaxWidth = 1160,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(235) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(copyableValues ? 650 : 330) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var label = new TextBlock
            {
                Text = fact.Label,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            };

            FrameworkElement value;
            if (copyableValues || fact.Copyable)
            {
                value = new TextBox
                {
                    Text = fact.Value,
                    IsReadOnly = true,
                    IsReadOnlyCaretVisible = true,
                    FontFamily = new FontFamily("Consolas"),
                    TextWrapping = TextWrapping.NoWrap,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Padding = new Thickness(4, 2, 4, 2),
                    Margin = new Thickness(10, 0, 10, 0),
                    ToolTip = copyHint
                };
            }
            else
            {
                value = new TextBlock
                {
                    Text = fact.Value,
                    FontFamily = new FontFamily("Consolas"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(10, 0, 10, 0)
                };
            }

            var explanation = new TextBlock
            {
                Text = fact.Explanation,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 360
            };

            Grid.SetColumn(label, 0);
            Grid.SetColumn(value, 1);
            Grid.SetColumn(explanation, 2);
            row.Children.Add(label);
            row.Children.Add(value);
            row.Children.Add(explanation);
            section.Children.Add(row);
        }

        parent.Children.Add(new Border
        {
            BorderBrush = SystemColors.ControlLightBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10),
            Child = section
        });
    }
}
