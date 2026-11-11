using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;
using AlphaLedger.App.Localization;
using AlphaLedger.App.Services;

namespace AlphaLedger.App.Views;

public partial class PasswordGateWindow : Window
{
    private const int MinimumPasswordLength = 4;

    private readonly AppLockService _appLockService;
    private bool _isFirstRun;
    private bool _isBusy;

    public PasswordGateWindow(AppLockService appLockService)
    {
        InitializeComponent();
        _appLockService = appLockService;
    }

    public async Task InitializeAsync()
    {
        try
        {
            _isFirstRun = !await _appLockService.IsPasswordConfiguredAsync();
        }
        catch
        {
            // If the stored state cannot be read, fall back to the login form so the
            // user can retry instead of being stuck on a disabled screen.
            _isFirstRun = false;
        }

        if (_isFirstRun)
        {
            TitleText.Text = LocalizationService.Instance.GetString("AppLockSetupTitle");
            SubtitleText.Text = LocalizationService.Instance.GetString("AppLockSetupSubtitle");
            ConfirmPanel.Visibility = Visibility.Visible;
            ContinueButton.Content = LocalizationService.Instance.GetString("AppLockSetupButton");
        }

        Loaded += (_, _) => PasswordInput.Focus();
    }

    private async void ContinueButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
            return;

        HideError();

        var password = PasswordInput.Password;

        if (_isFirstRun)
        {
            if (password.Length < MinimumPasswordLength)
            {
                ShowError("AppLockPasswordTooShort");
                PasswordInput.Focus();
                return;
            }

            if (!string.Equals(password, ConfirmPasswordInput.Password, StringComparison.Ordinal))
            {
                ShowError("AppLockPasswordsMismatch");
                ConfirmPasswordInput.Focus();
                return;
            }

            SetBusy(true);
            try
            {
                // Hashing (PBKDF2) and the database write run off the UI thread so the
                // window always stays responsive, even on slow disks.
                await Task.Run(() => _appLockService.ConfigurePasswordAsync(password));
                DialogResult = true;
            }
            catch (Exception ex)
            {
                ShowError("AppLockSaveFailed", ex.Message);
            }
            finally
            {
                SetBusy(false);
            }

            return;
        }

        SetBusy(true);
        try
        {
            var isValid = await Task.Run(() => _appLockService.VerifyPasswordAsync(password));
            if (!isValid)
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
            ShowError("AppLockSaveFailed", ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void PasswordInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        ContinueButton_Click(sender, new RoutedEventArgs());
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _isBusy)
            return;

        e.Handled = true;
        DialogResult = false;
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
                this,
                $"{LocalizationService.Instance.GetString("SettingsWhatsAppOpenFailed")} {ex.Message}",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;
        ContinueButton.IsEnabled = !isBusy;
        PasswordInput.IsEnabled = !isBusy;
        ConfirmPasswordInput.IsEnabled = !isBusy;
        ContinueButton.Content = LocalizationService.Instance.GetString(
            isBusy
                ? "AppLockWorking"
                : _isFirstRun ? "AppLockSetupButton" : "AppLockLoginButton");
    }

    private void ShowError(string resourceKey, string? details = null)
    {
        var message = LocalizationService.Instance.GetString(resourceKey);
        ErrorText.Text = string.IsNullOrWhiteSpace(details) ? message : $"{message} {details}";
        ErrorPanel.Visibility = Visibility.Visible;
    }

    private void HideError()
    {
        ErrorText.Text = string.Empty;
        ErrorPanel.Visibility = Visibility.Collapsed;
    }
}
