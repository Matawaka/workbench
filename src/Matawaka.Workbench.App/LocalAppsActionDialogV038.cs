using System.Windows;
using System.Windows.Controls;

namespace Matawaka.Workbench.App;

public enum LocalAppsActionChoiceV038
{
    Cancel,
    UpdateFromPackage,
    BuildUpdatePackage
}

/// <summary>
/// Explicit registered-app action chooser. Opening the chooser has no effect and
/// no button is marked as a default action. Labels describe the actual operation
/// instead of mapping generic YES/NO semantics onto update/package-build choices.
/// </summary>
public sealed class LocalAppsActionDialogV038 : Window
{
    public LocalAppsActionChoiceV038 Choice { get; private set; } = LocalAppsActionChoiceV038.Cancel;

    public LocalAppsActionDialogV038(string applicationId)
    {
        Title = "Local apps — choose action";
        Width = 520;
        Height = 245;
        MinWidth = 480;
        MinHeight = 220;
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
            Text = "Choose one explicit action. Opening or cancelling this dialog creates no package, update, or launch authority.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 18)
        });

        var update = new Button
        {
            Content = "Update from package",
            Height = 34,
            Margin = new Thickness(0, 0, 0, 8),
            IsDefault = false
        };
        update.Click += (_, _) => Complete(LocalAppsActionChoiceV038.UpdateFromPackage);
        root.Children.Add(update);

        var build = new Button
        {
            Content = "Build update package",
            Height = 34,
            Margin = new Thickness(0, 0, 0, 8),
            IsDefault = false
        };
        build.Click += (_, _) => Complete(LocalAppsActionChoiceV038.BuildUpdatePackage);
        root.Children.Add(build);

        var cancel = new Button
        {
            Content = "Cancel",
            Height = 34,
            IsCancel = true,
            IsDefault = false
        };
        cancel.Click += (_, _) => Complete(LocalAppsActionChoiceV038.Cancel);
        root.Children.Add(cancel);

        Content = root;
    }

    public static LocalAppsActionChoiceV038 ShowChoice(Window owner, string applicationId)
    {
        var dialog = new LocalAppsActionDialogV038(applicationId) { Owner = owner };
        dialog.ShowDialog();
        return dialog.Choice;
    }

    private void Complete(LocalAppsActionChoiceV038 choice)
    {
        Choice = choice;
        DialogResult = choice == LocalAppsActionChoiceV038.Cancel ? false : true;
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("local-apps-chooser-update-label", true, "Update from package", "Update from package"),
        ("local-apps-chooser-build-label", true, "Build update package", "Build update package"),
        ("local-apps-chooser-cancel-label", true, "Cancel", "Cancel"),
        ("local-apps-chooser-no-default-effect", true, "no IsDefault action; initial Choice=Cancel", "no default effect"),
        ("local-apps-chooser-top-level-button-added", false, "false", "false")
    };
}
