using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Win32;

namespace Matawaka.Workbench.JevLab;

internal sealed class JevLabWindow : Window
{
    private static readonly Brush CanvasColor = Color("#0C121B"), PanelColor = Color("#141E2B"), LineColor = Color("#293647"), TextColor = Color("#E6EDF5"), MutedColor = Color("#A2B2C5"), AccentColor = Color("#8CC8FF");
    private readonly StackPanel _results = new();
    private readonly TextBlock _status = Label("Выберите демонстрацию или откройте сохранённое наблюдение.", 14, MutedColor);
    private readonly TextBox _path = new() { MinWidth = 220 };
    private readonly List<Button> _actions = [];
    private JevObservation? _observation;
    private string _state = "EMPTY";
    internal bool Started { get; set; }
    internal string DisplayState => _state;
    internal int VisibleSignalCount { get; private set; }
    internal JevObservation? Observation => _observation;
    internal string StatusText => _status.Text;
    internal static string DemoPath(string name) => System.IO.Path.Combine(AppContext.BaseDirectory, "fixtures", name, "inputs.json");

    internal JevLabWindow()
    {
        Title = "Matawaka Workbench — Jev Lab (эксперимент)";
        Width = 1230; Height = 960; MinWidth = 1000; MinHeight = 720;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = CanvasColor; Foreground = TextColor;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 14;
        UseLayoutRounding = true; SnapsToDevicePixels = true;
        AutomationProperties.SetName(this, "Workbench Jev Lab — offline experimental observation viewer");
        Content = BuildWindow();
        ShowEmpty();
    }

    private UIElement BuildWindow()
    {
        var root = new Grid { Margin = new Thickness(26, 22, 26, 18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 22) };
        var badge = Chip("OFFLINE  /  ЭКСПЕРИМЕНТ", AccentColor);
        DockPanel.SetDock(badge, Dock.Right); top.Children.Add(badge);
        var title = new StackPanel();
        title.Children.Add(Label("MATAWAKA  /  WORKBENCH", 12, MutedColor));
        title.Children.Add(Label("Jev Lab", 30, TextColor, FontWeights.SemiBold));
        top.Children.Add(title); root.Children.Add(top);

        var body = new Grid(); Grid.SetRow(body, 1);
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(265) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var sidebar = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        sidebar.Children.Add(Label("НАБЛЮДЕНИЕ", 12, MutedColor, FontWeights.SemiBold));
        sidebar.Children.Add(Label("Попробуйте два сценария", 18, TextColor, FontWeights.SemiBold, new Thickness(0, 10, 0, 12)));
        sidebar.Children.Add(Action("Демо: чтение отчёта", "DemoReadOnly", async () => await LoadObservationAsync(DemoPath("read-only"))));
        sidebar.Children.Add(Action("Демо: расширение scope", "DemoScopeExpansion", async () => await LoadObservationAsync(DemoPath("scope-expansion"))));
        sidebar.Children.Add(Label("Ответы в демонстрациях заданы вручную. Они позволяют проверить интерфейс и не измеряют качество Jev.", 12, MutedColor, margin: new Thickness(0, 4, 0, 24)));
        sidebar.Children.Add(Label("Сохранённые файлы", 18, TextColor, FontWeights.SemiBold));
        sidebar.Children.Add(Label("Выберите inputs.json со ссылками на candidate и receipt и их SHA-256.", 12, MutedColor, margin: new Thickness(0, 8, 0, 10)));
        StylePath(); sidebar.Children.Add(_path);
        sidebar.Children.Add(Action("Открыть файл…", "BrowseManifest", BrowseAsync));
        sidebar.Children.Add(Action("Загрузить по пути", "LoadManifest", async () => await LoadObservationAsync(_path.Text)));
        sidebar.Children.Add(Action("Очистить", "ClearObservation", () => { Clear(); return Task.CompletedTask; }));
        sidebar.Children.Add(Label("Это отдельное экспериментальное окно Workbench. Основной рабочий процесс приложения к нему ещё не подключён.", 12, MutedColor, margin: new Thickness(0, 22, 0, 0)));
        body.Children.Add(new ScrollViewer { Content = sidebar, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });

        var view = new Grid(); Grid.SetColumn(view, 2);
        view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        view.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var statusCard = Card(_status, new Thickness(14)); statusCard.Margin = new Thickness(0, 0, 0, 14);
        AutomationProperties.SetAutomationId(_status, "ObservationStatus"); view.Children.Add(statusCard);
        var scroll = new ScrollViewer { Content = _results, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); view.Children.Add(scroll); body.Children.Add(view); root.Children.Add(body);
        var footer = Label("Вероятностная оценка ≠ разрешение на действие. Эталон и качество модели в этом окне не оцениваются.", 12, MutedColor, margin: new Thickness(0, 16, 0, 0));
        Grid.SetRow(footer, 2); root.Children.Add(footer);
        return root;
    }

    private void StylePath()
    {
        _path.Background = CanvasColor; _path.Foreground = TextColor; _path.CaretBrush = AccentColor;
        _path.BorderBrush = LineColor; _path.Padding = new Thickness(9); _path.Margin = new Thickness(0, 0, 0, 10);
        _path.TextWrapping = TextWrapping.NoWrap;
        AutomationProperties.SetName(_path, "Абсолютный путь к inputs.json"); AutomationProperties.SetAutomationId(_path, "ManifestPath");
        _path.KeyDown += async (_, e) => { if (e.Key == System.Windows.Input.Key.Enter && _path.IsEnabled) await LoadObservationAsync(_path.Text); };
    }

    private async Task BrowseAsync()
    {
        var dialog = new OpenFileDialog { Title = "Открыть manifest сохранённого наблюдения", Filter = "JSON manifest (*.json)|*.json", CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog(this) == true) await LoadObservationAsync(dialog.FileName);
    }

    internal async Task<bool> LoadObservationAsync(string manifestPath)
    {
        if (_state == "LOADING") return false;
        _observation = null; VisibleSignalCount = 0; _results.Children.Clear();
        _state = "LOADING"; _status.Text = "Проверка файлов и связи candidate → request → receipt…"; _status.Foreground = MutedColor;
        _path.Text = manifestPath;
        foreach (var button in _actions) button.IsEnabled = false;
        _path.IsEnabled = false;
        try
        {
            var observation = await Task.Run(() => JevObservationReader.Load(manifestPath));
            _observation = observation; _state = "LOADED";
            _status.Text = "Файлы и связь запроса проверены. Эталон не проверялся."; _status.Foreground = AccentColor;
            ShowObservation(observation);
            return true;
        }
        catch (Exception error)
        {
            _observation = null; VisibleSignalCount = 0; _results.Children.Clear(); _state = "ERROR";
            var code = error is JevInputException input ? input.Code : "FILE_READ_FAILED";
            _status.Text = $"Наблюдение не загружено · {code}"; _status.Foreground = Color("#FFC995");
            var explanation = new StackPanel();
            explanation.Children.Add(Label("Проверьте исходные файлы", 23, TextColor, FontWeights.SemiBold));
            explanation.Children.Add(Label("Путь должен вести к локальному manifest с оригинальными candidate и receipt. Их хэши, форматы и связь запроса должны совпадать. Предыдущие оценки очищены.", 14, MutedColor, margin: new Thickness(0, 12, 0, 0)));
            _results.Children.Add(Card(explanation, new Thickness(24)));
            return false;
        }
        finally { foreach (var button in _actions) button.IsEnabled = true; _path.IsEnabled = true; }
    }

    internal void Clear()
    {
        _observation = null; VisibleSignalCount = 0; _state = "EMPTY"; _results.Children.Clear(); _path.Text = "";
        _status.Text = "Выберите демонстрацию или откройте сохранённое наблюдение."; _status.Foreground = MutedColor;
        ShowEmpty();
    }

    private void ShowEmpty()
    {
        var welcome = new StackPanel { Margin = new Thickness(10, 20, 10, 20) };
        welcome.Children.Add(Label("Посмотрите на операцию\nчерез вопросы Jev", 29, TextColor, FontWeights.SemiBold));
        welcome.Children.Add(Label("Шесть отдельных оценок помогают изучать намерение, область действия и предполагаемые эффекты операции.", 16, MutedColor, margin: new Thickness(0, 18, 0, 26)));
        welcome.Children.Add(Label("01   Выберите демонстрацию слева", 15, AccentColor, margin: new Thickness(0, 0, 0, 14)));
        welcome.Children.Add(Label("02   Сравните значения и формулировки вопросов", 15, TextColor, margin: new Thickness(0, 0, 0, 14)));
        welcome.Children.Add(Label("03   Загрузите сохранённое наблюдение через manifest", 15, TextColor));
        _results.Children.Add(Card(welcome, new Thickness(24)));
    }

    private void ShowObservation(JevObservation o)
    {
        var source = new StackPanel();
        var synthetic = o.Provider == "synthetic.offline";
        source.Children.Add(Label(synthetic ? "СИНТЕТИЧЕСКАЯ ДЕМОНСТРАЦИЯ" : "СОХРАНЁННОЕ НАБЛЮДЕНИЕ", 12, AccentColor, FontWeights.SemiBold));
        source.Children.Add(Label($"ID из manifest: {o.CaseId}", 21, TextColor, FontWeights.SemiBold, new Thickness(0, 7, 0, 5)));
        source.Children.Add(Label(synthetic ? "Ответы заданы вручную · вызов модели не выполнялся" : $"Источник в receipt: {o.Provider} · модель: {o.ObservedModel}", 12, MutedColor));
        using var context = JsonDocument.Parse(o.ContextJson);
        foreach (var (key, title) in new[] { ("declaredIntent", "Намерение"), ("requestedOperation", "Операция"), ("presentedScope", "Заданная область") })
        {
            source.Children.Add(Label(title, 11, MutedColor, FontWeights.SemiBold, new Thickness(0, 12, 0, 3)));
            source.Children.Add(Label(context.RootElement.GetProperty(key).GetString() ?? "", 13, TextColor));
        }
        _results.Children.Add(Card(source, new Thickness(18)));
        _results.Children.Add(Label("Вероятность ответа «да» на каждый вопрос", 17, TextColor, FontWeights.SemiBold, new Thickness(0, 20, 0, 4)));
        _results.Children.Add(Label("Это оценка истинности утверждения моделью, а не процент надёжности, безопасности или разрешения.", 12, MutedColor, margin: new Thickness(0, 0, 0, 12)));
        var cards = new UniformGrid { Columns = 2 };
        foreach (var signal in o.Signals) cards.Children.Add(SignalCard(signal));
        VisibleSignalCount = o.Signals.Count; _results.Children.Add(cards);
        var questions = new StackPanel();
        foreach (var signal in o.Signals)
        {
            questions.Children.Add(Label(signal.Id, 12, AccentColor, FontWeights.SemiBold, new Thickness(0, 12, 0, 4)));
            questions.Children.Add(Label(signal.Question, 13, TextColor));
        }
        _results.Children.Add(new Expander { Header = "Точные вопросы, к которым относятся оценки", Foreground = MutedColor, Content = questions, Margin = new Thickness(0, 12, 0, 8) });
        var facts = new StackPanel();
        facts.Children.Add(Label($"Запрошенная модель: {o.RequestedModel}\nНаблюдаемая модель: {o.ObservedModel}\nКласс случая (декларация manifest): {o.SampleClass}\nЧеловеческий эталон: не проверялся", 12, MutedColor));
        facts.Children.Add(Label("Проверены байты candidate/receipt и связи запроса. Хэш не удостоверяет автора, время, происхождение ответа или независимость review.", 12, MutedColor, margin: new Thickness(0, 10, 0, 10)));
        facts.Children.Add(Label($"candidate: {o.CandidateRawSha256}\nreceipt: {o.ReceiptRawSha256}\nrequest: {o.RequestDigest}", 11, MutedColor));
        _results.Children.Add(new Expander { Header = "Сведения о проверке и происхождении", Foreground = MutedColor, Content = facts, Margin = new Thickness(0, 12, 0, 8) });
    }

    private UIElement SignalCard(JevSignal s)
    {
        var names = new Dictionary<string, string> {
            ["goalAlignment"] = "Соответствие заявленной цели", ["operationalSpecificity"] = "Конкретность описания действия",
            ["scopeExpansion"] = "Расширение заданной области", ["causesExternalMutation"] = "Изменение внешнего состояния",
            ["hasReliableRollback"] = "Надёжный откат изменений", ["externalCommunication"] = "Передача информации вовне"
        };
        var content = new StackPanel();
        var heading = new DockPanel();
        var score = Label((s.Probability * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%", 23, AccentColor, FontWeights.SemiBold);
        AutomationProperties.SetAutomationId(score, $"Probability_{s.Id}"); DockPanel.SetDock(score, Dock.Right); heading.Children.Add(score);
        heading.Children.Add(Label(names[s.Id], 13, TextColor, FontWeights.SemiBold, new Thickness(0, 4, 12, 0))); content.Children.Add(heading);
        var bar = new ProgressBar { Minimum = 0, Maximum = 1, Value = s.Probability, Height = 5, Foreground = AccentColor, Background = LineColor, BorderThickness = new Thickness(0), Margin = new Thickness(0, 12, 0, 8) };
        AutomationProperties.SetName(bar, names[s.Id]); content.Children.Add(bar);
        content.Children.Add(Label(s.ResearchOnly ? "RESEARCH_ONLY · исследовательский сигнал" : s.Id == "hasReliableRollback" ? "Условный вопрос об откате при изменениях" : "Диагностическая оценка", 10, MutedColor));
        var card = Card(content, new Thickness(14)); card.Margin = new Thickness(0, 0, 8, 8); card.MinHeight = 105;
        card.ToolTip = new TextBlock { Text = s.Question, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 };
        AutomationProperties.SetHelpText(card, s.Question);
        return card;
    }

    private Button Action(string title, string id, Func<Task> action)
    {
        var button = new Button { Content = title, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 10, 12, 10), Background = PanelColor, Foreground = TextColor, BorderBrush = LineColor, BorderThickness = new Thickness(1), Cursor = System.Windows.Input.Cursors.Hand, Margin = new Thickness(0, 0, 0, 9) };
        AutomationProperties.SetAutomationId(button, id); AutomationProperties.SetName(button, title);
        button.Click += async (_, _) => await action(); _actions.Add(button); return button;
    }
    private static Brush Color(string value) => new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(value));
    private static TextBlock Label(string text, double size, Brush brush, FontWeight? weight = null, Thickness? margin = null) => new() { Text = text, FontSize = size, Foreground = brush, FontWeight = weight ?? FontWeights.Normal, TextWrapping = TextWrapping.Wrap, Margin = margin ?? new Thickness(0), LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
    private static Border Card(UIElement child, Thickness padding) => new() { Background = PanelColor, BorderBrush = LineColor, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = padding, Child = child };
    private static Border Chip(string text, Brush color) => new() { Child = Label(text, 11, color, FontWeights.SemiBold), Background = PanelColor, Padding = new Thickness(12, 9, 12, 9), CornerRadius = new CornerRadius(5), VerticalAlignment = VerticalAlignment.Top };
}
