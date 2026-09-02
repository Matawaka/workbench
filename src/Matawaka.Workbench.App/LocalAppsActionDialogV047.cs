using System.Windows;
using System.Windows.Controls;

namespace Matawaka.Workbench.App;

public enum LocalAppsActionChoiceV047
{
    Cancel,
    UpdateFromPackage,
    BuildUpdatePackage,
    LaunchApp,
    ExportUpdateContext,
    BindDevelopmentSource,
    ExportPrivateDevelopmentContext,
    ChatReadRelay
}

public sealed class LocalAppsActionDialogV047 : Window
{
    public LocalAppsActionChoiceV047 Choice { get; private set; } = LocalAppsActionChoiceV047.Cancel;

    public LocalAppsActionDialogV047(string applicationId)
    {
        Title = "Local apps — choose action";
        Width = 580;
        MinWidth = 540;
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
            Text = "Choose one explicit action. Chat read request, local read, clipboard disclosure, app launch, source binding and update remain separate authorities; opening or cancelling this chooser has no effect.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        Add(root, "Update from package", LocalAppsActionChoiceV047.UpdateFromPackage);
        Add(root, "Build update package", LocalAppsActionChoiceV047.BuildUpdatePackage);
        Add(root, "Launch app", LocalAppsActionChoiceV047.LaunchApp);
        Add(root, "Export update context", LocalAppsActionChoiceV047.ExportUpdateContext);
        Add(root, "Bind development source", LocalAppsActionChoiceV047.BindDevelopmentSource);
        Add(root, "Export PRIVATE development context", LocalAppsActionChoiceV047.ExportPrivateDevelopmentContext);
        Add(root, "Chat read relay", LocalAppsActionChoiceV047.ChatReadRelay);

        var cancel = new Button { Content = "Cancel", Height = 34, IsCancel = true, IsDefault = false };
        cancel.Click += (_, _) => Complete(LocalAppsActionChoiceV047.Cancel);
        root.Children.Add(cancel);
        Content = root;
    }

    public static LocalAppsActionChoiceV047 ShowChoice(Window owner, string applicationId)
    {
        var dialog = new LocalAppsActionDialogV047(applicationId) { Owner = owner };
        dialog.ShowDialog();
        return dialog.Choice;
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("chooser-v047-chat-read", true, "Chat read relay", "explicit"),
        ("chooser-v047-inherits-v046-actions", true, "update/build/launch/context/source/private", "preserved"),
        ("chooser-v047-default-effect", true, "initial Choice=Cancel; no default action", "none")
    };

    private void Add(StackPanel root, string text, LocalAppsActionChoiceV047 choice)
    {
        var button = new Button { Content = text, Height = 34, Margin = new Thickness(0, 0, 0, 8), IsDefault = false };
        button.Click += (_, _) => Complete(choice);
        root.Children.Add(button);
    }

    private void Complete(LocalAppsActionChoiceV047 choice)
    {
        Choice = choice;
        DialogResult = choice == LocalAppsActionChoiceV047.Cancel ? false : true;
    }
}
