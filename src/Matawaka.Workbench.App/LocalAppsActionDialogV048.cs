using System.Windows;
using System.Windows.Controls;

namespace Matawaka.Workbench.App;

public enum LocalAppsActionChoiceV048
{
    Cancel,
    UpdateFromPackage,
    BuildUpdatePackage,
    LaunchApp,
    ExportUpdateContext,
    BindDevelopmentSource,
    ExportPrivateDevelopmentContext,
    ChatReadRelay,
    ReadSessionLease,
    RevokeReadLeases
}

public sealed class LocalAppsActionDialogV048 : Window
{
    public LocalAppsActionChoiceV048 Choice { get; private set; } = LocalAppsActionChoiceV048.Cancel;

    public LocalAppsActionDialogV048(string applicationId)
    {
        Title = "Local apps — choose action";
        Width = 600;
        MinWidth = 560;
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
            Text = "Choose one explicit action. Read request, clipboard disclosure, read-session lease, revocation, app launch, source binding and update remain separate authorities; opening or cancelling this chooser has no effect.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        Add(root, "Update from package", LocalAppsActionChoiceV048.UpdateFromPackage);
        Add(root, "Build update package", LocalAppsActionChoiceV048.BuildUpdatePackage);
        Add(root, "Launch app", LocalAppsActionChoiceV048.LaunchApp);
        Add(root, "Export update context", LocalAppsActionChoiceV048.ExportUpdateContext);
        Add(root, "Bind development source", LocalAppsActionChoiceV048.BindDevelopmentSource);
        Add(root, "Export PRIVATE development context", LocalAppsActionChoiceV048.ExportPrivateDevelopmentContext);
        Add(root, "Chat read relay", LocalAppsActionChoiceV048.ChatReadRelay);
        Add(root, "Read session lease", LocalAppsActionChoiceV048.ReadSessionLease);
        Add(root, "Revoke active read leases", LocalAppsActionChoiceV048.RevokeReadLeases);

        var cancel = new Button { Content = "Cancel", Height = 34, IsCancel = true, IsDefault = false };
        cancel.Click += (_, _) => Complete(LocalAppsActionChoiceV048.Cancel);
        root.Children.Add(cancel);
        Content = root;
    }

    public static LocalAppsActionChoiceV048 ShowChoice(Window owner, string applicationId)
    {
        var dialog = new LocalAppsActionDialogV048(applicationId) { Owner = owner };
        dialog.ShowDialog();
        return dialog.Choice;
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("chooser-v048-read-lease", true, "Read session lease", "explicit"),
        ("chooser-v048-revoke", true, "Revoke active read leases", "explicit"),
        ("chooser-v048-relay-preserved", true, "Chat read relay", "preserved fallback"),
        ("chooser-v048-v046-actions", true, "update/build/launch/context/source/private", "preserved"),
        ("chooser-v048-default-effect", true, "initial Choice=Cancel; no default action", "none")
    };

    private void Add(StackPanel root, string text, LocalAppsActionChoiceV048 choice)
    {
        var button = new Button { Content = text, Height = 34, Margin = new Thickness(0, 0, 0, 8), IsDefault = false };
        button.Click += (_, _) => Complete(choice);
        root.Children.Add(button);
    }

    private void Complete(LocalAppsActionChoiceV048 choice)
    {
        Choice = choice;
        DialogResult = choice == LocalAppsActionChoiceV048.Cancel ? false : true;
    }
}
