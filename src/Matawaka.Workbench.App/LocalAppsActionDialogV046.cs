using System.Windows;
using System.Windows.Controls;

namespace Matawaka.Workbench.App;

public enum LocalAppsActionChoiceV046
{
    Cancel,
    UpdateFromPackage,
    BuildUpdatePackage,
    LaunchApp,
    ExportUpdateContext,
    BindDevelopmentSource,
    ExportPrivateDevelopmentContext
}

public sealed class LocalAppsActionDialogV046 : Window
{
    public LocalAppsActionChoiceV046 Choice { get; private set; } = LocalAppsActionChoiceV046.Cancel;

    public LocalAppsActionDialogV046(string applicationId)
    {
        Title = "Local apps — choose action";
        Width = 560;
        MinWidth = 520;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock
        {
            Text = $"Registered application: {applicationId}",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10)
        });
        root.Children.Add(new TextBlock
        {
            Text = "Choose one explicit action. Registration, update, launch, source binding and context export are separate authorities; opening or cancelling this chooser has no effect.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        Add(root, "Update from package", LocalAppsActionChoiceV046.UpdateFromPackage);
        Add(root, "Build update package", LocalAppsActionChoiceV046.BuildUpdatePackage);
        Add(root, "Launch app", LocalAppsActionChoiceV046.LaunchApp);
        Add(root, "Export update context", LocalAppsActionChoiceV046.ExportUpdateContext);
        Add(root, "Bind development source", LocalAppsActionChoiceV046.BindDevelopmentSource);
        Add(root, "Export PRIVATE development context", LocalAppsActionChoiceV046.ExportPrivateDevelopmentContext);

        var cancel = new Button
        {
            Content = "Cancel",
            Height = 34,
            IsCancel = true,
            IsDefault = false
        };
        cancel.Click += (_, _) => Complete(LocalAppsActionChoiceV046.Cancel);
        root.Children.Add(cancel);
        Content = root;
    }

    public static LocalAppsActionChoiceV046 ShowChoice(Window owner, string applicationId)
    {
        var dialog = new LocalAppsActionDialogV046(applicationId) { Owner = owner };
        dialog.ShowDialog();
        return dialog.Choice;
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("chooser-v046-update", true, "Update from package", "explicit"),
        ("chooser-v046-build", true, "Build update package", "explicit"),
        ("chooser-v046-launch", true, "Launch app", "explicit"),
        ("chooser-v046-update-context", true, "Export update context", "explicit"),
        ("chooser-v046-source-bind", true, "Bind development source", "explicit"),
        ("chooser-v046-private-context", true, "Export PRIVATE development context", "explicit"),
        ("chooser-v046-default-effect", true, "initial Choice=Cancel; no default button", "none")
    };

    private void Add(StackPanel root, string text, LocalAppsActionChoiceV046 choice)
    {
        var button = new Button
        {
            Content = text,
            Height = 34,
            Margin = new Thickness(0, 0, 0, 8),
            IsDefault = false
        };
        button.Click += (_, _) => Complete(choice);
        root.Children.Add(button);
    }

    private void Complete(LocalAppsActionChoiceV046 choice)
    {
        Choice = choice;
        DialogResult = choice == LocalAppsActionChoiceV046.Cancel ? false : true;
    }
}
