using System.IO;
using System.Windows;
using AlphaLedger.App.Localization;
using AlphaLedger.App.Services;
using AlphaLedger.Core;
using AlphaLedger.Core.Abstractions;
using AlphaLedger.Core.Exceptions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AlphaLedger.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly ICompanyFolderService _folders;
    private readonly WindowsFolderPicker _picker;
    private readonly WindowsDialogService _dialogs;
    private readonly IAppPaths _paths;
    private readonly AppDataRelocationService _relocation;
    private readonly ApplicationUpdateService _applicationUpdater;

    public SettingsViewModel(
        ISettingsService settings,
        ICompanyFolderService folders,
        WindowsFolderPicker picker,
        WindowsDialogService dialogs,
        IAppPaths paths,
        AppDataRelocationService relocation,
        ApplicationUpdateService applicationUpdater)
    {
        _settings = settings;
        _folders = folders;
        _picker = picker;
        _dialogs = dialogs;
        _paths = paths;
        _relocation = relocation;
        _applicationUpdater = applicationUpdater;
        DatabasePath = _paths.DatabasePath;
        AppDataPath = _paths.DataDirectory;
        LocalizationService.Instance.CultureChanged += OnCultureChanged;

        SelectedLanguage = LocalizationService.Instance.CurrentCulture.Name;
    }

    [ObservableProperty] private string? _documentRoot;
    [ObservableProperty] private string _databasePath = string.Empty;
    [ObservableProperty] private string _appDataPath = string.Empty;
    [ObservableProperty] private string _selectedLanguage;
    [ObservableProperty] private string _currentUserName = string.Empty;
    [ObservableProperty] private string _currentUserRole = string.Empty;
    [ObservableProperty] private string? _status;
    [ObservableProperty] private string _updateStatus = string.Empty;
    [ObservableProperty] private bool _isCheckingForUpdates;

    public event Action? IdentityChanged;

    private bool CanCheckForUpdates => !IsCheckingForUpdates;

    [RelayCommand(CanExecute = nameof(CanCheckForUpdates))]
    public async Task CheckForUpdatesAsync()
    {
        IsCheckingForUpdates = true;
        UpdateStatus = LocalizationService.Instance.GetString("SettingsUpdateChecking");
        try
        {
            var outcome = await _applicationUpdater.CheckForUpdatesAsync(
                Application.Current.MainWindow,
                showUpToDateMessage: true);
            UpdateStatus = outcome switch
            {
                UpdateCheckOutcome.UpToDate => LocalizationService.Instance.GetString("SettingsUpdateNoUpdates"),
                UpdateCheckOutcome.UpdateDeclined => LocalizationService.Instance.GetString("SettingsUpdateAvailable"),
                UpdateCheckOutcome.UpdateDownloaded => LocalizationService.Instance.GetString("SettingsUpdateDownloaded"),
                _ => string.Empty
            };
        }
        catch (Exception ex)
        {
            UpdateStatus = LocalizationService.Instance.GetString("SettingsUpdateFailed");
            _dialogs.Error($"{LocalizationService.Instance.GetString("UpdateCheckFailedMessage")}\n\n{ex.GetBaseException().Message}");
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        DocumentRoot = _paths.IsPortableMode ? _paths.ApplicationRoot : string.Empty;

        var savedLang = await _settings.GetAsync(SettingKeys.SelectedLanguage);
        SelectedLanguage = LocalizationService.NormalizeCultureName(savedLang);

        CurrentUserName = await _settings.GetAsync(SettingKeys.CurrentUserName) ?? Environment.UserName;
        CurrentUserRole = await _settings.GetAsync(SettingKeys.CurrentUserRole)
            ?? LocalizationService.Instance.GetString("CurrentUserRole");

        Status = LocalizationService.Instance.GetString(
            string.IsNullOrWhiteSpace(DocumentRoot) ? "SettingsDocumentsNotSelected" : "SettingsSettingsLoaded");
    }

    [RelayCommand]
    public void Browse()
    {
        var selected = _picker.PickFolder(LocalizationService.Instance.GetString("SettingsPickDocumentFolder"), DocumentRoot);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            DocumentRoot = selected;
        }
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(DocumentRoot))
            {
                throw new BusinessException("Please select a document storage folder first.");
            }

            _folders.EnsureDocumentRoot(DocumentRoot);
            await _settings.SetAsync(SettingKeys.SelectedLanguage, LocalizationService.NormalizeCultureName(SelectedLanguage));
            await _settings.SetAsync(SettingKeys.CurrentUserName,
                string.IsNullOrWhiteSpace(CurrentUserName) ? null : CurrentUserName.Trim());
            await _settings.SetAsync(SettingKeys.CurrentUserRole,
                string.IsNullOrWhiteSpace(CurrentUserRole) ? null : CurrentUserRole.Trim());
            var databasePath = await _relocation.RelocateAsync(DocumentRoot);

            LocalizationService.Instance.SetLanguage(SelectedLanguage);
            IdentityChanged?.Invoke();

            Status = LocalizationService.Instance.GetString("SettingsSettingsSaved");
            _dialogs.Info($"تم نقل الأرشيف وقاعدة البيانات إلى المجلد المحدد. أعد تشغيل البرنامج لاستخدام المسار الجديد.\nقاعدة البيانات: {databasePath}");
        }
        catch (Exception ex)
        {
            _dialogs.Error(ex.Message);
        }
    }

    partial void OnSelectedLanguageChanged(string value)
    {
        LocalizationService.Instance.SetLanguage(value);
    }

    partial void OnDocumentRootChanged(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        AppDataPath = Path.Combine(value, "System", "Data");
        DatabasePath = Path.Combine(AppDataPath, "alpha-ledger.db");
    }

    partial void OnIsCheckingForUpdatesChanged(bool value)
        => CheckForUpdatesCommand.NotifyCanExecuteChanged();

    private void OnCultureChanged()
    {
        if (Status is not null)
            Status = LocalizationService.Instance.TranslateMessage(Status);
    }
}
