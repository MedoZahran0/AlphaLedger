using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using AlphaLedger.App.Localization;
using AlphaLedger.App.ViewModels;

namespace AlphaLedger.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private async void ChangePassword_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel)
            return;

        await viewModel.ChangePasswordAsync(
            CurrentPasswordBox.Password,
            NewPasswordBox.Password,
            ConfirmNewPasswordBox.Password);
        CurrentPasswordBox.Clear();
        NewPasswordBox.Clear();
        ConfirmNewPasswordBox.Clear();
    }

    private void WhatsAppLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                $"{LocalizationService.Instance.GetString("SettingsWhatsAppOpenFailed")} {ex.Message}",
                LocalizationService.Instance.GetString("SettingsContactTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
