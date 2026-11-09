using System.Windows;
using AlphaLedger.App.Localization;

namespace AlphaLedger.App.Views;

public partial class UpdateNotesWindow : Window
{
    public UpdateNotesWindow(string version, IReadOnlyList<string> changes)
    {
        InitializeComponent();
        VersionText.Text = string.Format(
            LocalizationService.Instance.CurrentCulture,
            LocalizationService.Instance.GetString("UpdateNotesVersion"),
            version);
        ChangesList.ItemsSource = changes;
        NoChangesText.Visibility = changes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
