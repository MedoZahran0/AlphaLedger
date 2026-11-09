using System.Windows;
using System.Windows.Input;
using AlphaLedger.App.Localization;
using AlphaLedger.App.Services;

namespace AlphaLedger.App.Views;

public partial class PasswordGateWindow : Window
{
    private readonly AppLockService _appLockService;
    private bool _isFirstRun;

    public PasswordGateWindow(AppLockService appLockService)
    {
        InitializeComponent();
        _appLockService = appLockService;
    }

    public async Task InitializeAsync()
    {
        _isFirstRun = !await _appLockService.IsPasswordConfiguredAsync();
        if (_isFirstRun)
        {
            SubtitleText.Text = LocalizationService.Instance.GetString("AppLockSetupSubtitle");
            ConfirmPanel.Visibility = Visibility.Visible;
            ContinueButton.Content = LocalizationService.Instance.GetString("AppLockSetupButton");
        }

        Loaded += (_, _) => PasswordInput.Focus();
    }

    private async void ContinueButton_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        try
        {
            if (_isFirstRun)
            {
                if (PasswordInput.Password.Length < 8)
                {
                    ShowError("AppLockPasswordTooShort");
                    return;
                }

                if (!string.Equals(PasswordInput.Password, ConfirmPasswordInput.Password, StringComparison.Ordinal))
                {
                    ShowError("AppLockPasswordsMismatch");
                    return;
                }

                await _appLockService.ConfigurePasswordAsync(PasswordInput.Password);
                DialogResult = true;
                return;
            }

            if (!await _appLockService.VerifyPasswordAsync(PasswordInput.Password))
            {
                ShowError("AppLockIncorrectPassword");
                PasswordInput.Clear();
                PasswordInput.Focus();
                return;
            }

            DialogResult = true;
        }
        catch (Exception ex)
        {
            ErrorText.Text = $"{LocalizationService.Instance.GetString("AppLockSaveFailed")} {ex.Message}";
            ErrorText.Visibility = Visibility.Visible;
        }
    }

    private void PasswordInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        ContinueButton_Click(sender, new RoutedEventArgs());
    }

    private void ShowError(string resourceKey)
    {
        ErrorText.Text = LocalizationService.Instance.GetString(resourceKey);
        ErrorText.Visibility = Visibility.Visible;
    }
}
