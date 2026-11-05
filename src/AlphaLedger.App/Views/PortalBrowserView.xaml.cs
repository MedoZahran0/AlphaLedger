using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AlphaLedger.App.Localization;
using Microsoft.Web.WebView2.Core;

namespace AlphaLedger.App.Views;

public partial class PortalBrowserView : UserControl
{
    public static readonly DependencyProperty PortalUrlProperty = DependencyProperty.Register(
        nameof(PortalUrl), typeof(string), typeof(PortalBrowserView), new PropertyMetadata(string.Empty, OnPortalUrlChanged));

    public static readonly DependencyProperty PortalNameProperty = DependencyProperty.Register(
        nameof(PortalName), typeof(string), typeof(PortalBrowserView), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty PortalUsernameProperty = DependencyProperty.Register(
        nameof(PortalUsername), typeof(string), typeof(PortalBrowserView), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty PortalPasswordProperty = DependencyProperty.Register(
        nameof(PortalPassword), typeof(string), typeof(PortalBrowserView), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty CompanyNameProperty = DependencyProperty.Register(
        nameof(CompanyName), typeof(string), typeof(PortalBrowserView), new PropertyMetadata(string.Empty));

    private bool _isInitialized;
    private bool _runtimeUnavailable;
    private bool _initializationStarted;
    private bool _eventsAttached;
    private Uri? _homeUri;

    private static Task<CoreWebView2Environment>? _environmentTask;
    private static readonly object _environmentLock = new();

    public PortalBrowserView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public string PortalUrl
    {
        get => (string)GetValue(PortalUrlProperty);
        set => SetValue(PortalUrlProperty, value);
    }

    public string PortalName
    {
        get => (string)GetValue(PortalNameProperty);
        set => SetValue(PortalNameProperty, value);
    }

    public string PortalUsername
    {
        get => (string)GetValue(PortalUsernameProperty);
        set => SetValue(PortalUsernameProperty, value);
    }

    public string PortalPassword
    {
        get => (string)GetValue(PortalPasswordProperty);
        set => SetValue(PortalPasswordProperty, value);
    }

    public string CompanyName
    {
        get => (string)GetValue(CompanyNameProperty);
        set => SetValue(CompanyNameProperty, value);
    }

    private static void OnPortalUrlChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        var view = (PortalBrowserView)dependencyObject;
        if (view._isInitialized)
            view.NavigateToConfiguredPortal();
    }

    private async void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        if (_runtimeUnavailable)
            return;

        if (_isInitialized)
            return;

        // Prevent re-entrant initialization (e.g. Loaded firing again while the
        // first initialization is still awaiting). The first call will finish
        // and set _isInitialized.
        if (_initializationStarted)
            return;

        _initializationStarted = true;

        try
        {
            var availableVersion = CoreWebView2Environment.GetAvailableBrowserVersionString();
            if (string.IsNullOrWhiteSpace(availableVersion))
            {
                _runtimeUnavailable = true;
                RuntimeUnavailablePanel.Visibility = Visibility.Visible;
                Browser.Visibility = Visibility.Collapsed;
                RuntimeUnavailableText.Text = LocalizationService.Instance.GetString("PortalRuntimeUnavailable");
                StatusText.Text = LocalizationService.Instance.GetString("PortalRuntimeUnavailable");
                _initializationStarted = false;
                return;
            }

            if (Browser.CoreWebView2 is null)
            {
                var environment = await GetSharedEnvironmentAsync();
                // Re-check after await: another path may have initialized it.
                if (Browser.CoreWebView2 is null)
                    await Browser.EnsureCoreWebView2Async(environment);
            }

            AttachCoreWebView2Events();

            _isInitialized = true;
            NavigateToConfiguredPortal();
        }
        catch (Exception ex)
        {
            // WebView2 may already be initialized (possibly with a different
            // environment instance). In that case CoreWebView2 is usable.
            if (Browser.CoreWebView2 is not null)
            {
                AttachCoreWebView2Events();
                _isInitialized = true;
                NavigateToConfiguredPortal();
                return;
            }

            _initializationStarted = false;
            _runtimeUnavailable = true;
            RuntimeUnavailablePanel.Visibility = Visibility.Visible;
            Browser.Visibility = Visibility.Collapsed;
            var message = $"WebView2 initialization failed: {ex.GetType().Name}: {ex.Message}";
            StatusText.Text = message;
            RuntimeUnavailableText.Text = LocalizationService.Instance.GetString("PortalRuntimeUnavailable") + " " + ex.Message;
            try
            {
                var logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AlphaLedger",
                    "webview2-error.log");
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, $"[{DateTime.Now:O}] {ex}\n");
            }
            catch
            {
            }
            System.Diagnostics.Debug.WriteLine(message);
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    private void AttachCoreWebView2Events()
    {
        if (_eventsAttached || Browser.CoreWebView2 is null)
            return;

        Browser.CoreWebView2.NavigationStarting -= OnNavigationStarting;
        Browser.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
        Browser.CoreWebView2.ProcessFailed -= OnProcessFailed;
        Browser.CoreWebView2.NavigationStarting += OnNavigationStarting;
        Browser.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
        Browser.CoreWebView2.ProcessFailed += OnProcessFailed;
        _eventsAttached = true;
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        try
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AlphaLedger",
                "webview2-error.log");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.AppendAllText(logPath, $"[{DateTime.Now:O}] WebView2 process failed: {e.ProcessFailedKind} {e.Reason}\n");
        }
        catch
        {
        }
        StatusText.Text = LocalizationService.Instance.GetString("PortalNavigationFailed");
    }

    private static async Task<CoreWebView2Environment> GetSharedEnvironmentAsync()
    {
        Task<CoreWebView2Environment>? task;
        lock (_environmentLock)
        {
            if (_environmentTask is null)
            {
                var userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AlphaLedger",
                    "WebView2");
                Directory.CreateDirectory(userDataFolder);
                _environmentTask = CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: userDataFolder);
            }
            task = _environmentTask;
        }

        try
        {
            return await task;
        }
        catch
        {
            lock (_environmentLock)
            {
                _environmentTask = null;
            }
            throw;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs eventArgs)
    {
        // Keep the WebView2 instance and its event handlers intact for the
        // lifetime of this control; detaching here would leave the browser
        // without navigation status updates after a reload.
    }

    private void NavigateToConfiguredPortal()
    {
        if (string.IsNullOrWhiteSpace(PortalUrl))
        {
            StatusText.Text = LocalizationService.Instance.GetString("PortalSelectAccount");
            _homeUri = null;
            return;
        }

        if (!TryGetHttpsUri(PortalUrl, out var uri))
        {
            StatusText.Text = LocalizationService.Instance.GetString("PortalInvalidHttpsUrl");
            _homeUri = null;
            return;
        }

        _homeUri = uri;
        Browser.CoreWebView2?.Navigate(uri.AbsoluteUri);
    }

    private void Back_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (Browser.CoreWebView2?.CanGoBack == true)
            Browser.CoreWebView2.GoBack();
    }

    private void Forward_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (Browser.CoreWebView2?.CanGoForward == true)
            Browser.CoreWebView2.GoForward();
    }

    private void Refresh_Click(object sender, RoutedEventArgs eventArgs) => Browser.CoreWebView2?.Reload();

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs eventArgs)
    {
        if (!Uri.TryCreate(eventArgs.Uri, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            eventArgs.Cancel = true;
            StatusText.Text = LocalizationService.Instance.GetString("PortalBlockedScheme");
            return;
        }

        StatusText.Text = LocalizationService.Instance.GetString("PortalManualSignIn");
    }

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs eventArgs)
    {
        StatusText.Text = eventArgs.IsSuccess
            ? LocalizationService.Instance.GetString("PortalManualSignIn")
            : LocalizationService.Instance.GetString("PortalNavigationFailed");

        if (eventArgs.IsSuccess && !string.IsNullOrWhiteSpace(PortalUsername))
        {
            try
            {
                var user = PortalUsername.Replace("\\", "\\\\").Replace("\"", "\\\"");
                var pass = PortalPassword.Replace("\\", "\\\\").Replace("\"", "\\\"");
                var script = $@"
(function(){{
  try {{
    var inputs = document.querySelectorAll('input');
    var u = null, p = null;
    for (var i=0;i<inputs.length;i++){{
      var t = (inputs[i].type||'').toLowerCase();
      var n = (inputs[i].name||inputs[i].id||'').toLowerCase();
      if (!u && (t==='text' || t==='email' || n.indexOf('user')>=0 || n.indexOf('login')>=0 || n.indexOf('email')>=0)) u = inputs[i];
      if (!p && t==='password') p = inputs[i];
    }}
    if (u) {{ u.value = ""{user}""; u.dispatchEvent(new Event('input',{{bubbles:true}})); }}
    if (p) {{ p.value = ""{pass}""; p.dispatchEvent(new Event('input',{{bubbles:true}})); }}
  }} catch(e) {{}}
}})();";
                await Browser.CoreWebView2.ExecuteScriptAsync(script);
                await TryClickSubmitAsync();
            }
            catch
            {
            }
        }
    }

    private async Task TryClickSubmitAsync()
    {
        if (string.IsNullOrWhiteSpace(PortalUsername) || string.IsNullOrWhiteSpace(PortalPassword))
            return;

        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(500);
            if (Browser.CoreWebView2 is null)
                return;

            try
            {
                const string script = @"
(function(){
  try {
    var btn = null;
    var candidates = document.querySelectorAll('button,input[type=""submit""],input[type=""button""]');
    for (var i=0;i<candidates.length;i++) {
      var el = candidates[i];
      var txt = (el.innerText||el.value||'').toLowerCase();
      if (txt.indexOf('login')>=0 || txt.indexOf('sign')>=0 || txt.indexOf('دخول')>=0 || txt.indexOf('تسجيل')>=0 || txt.indexOf('submit')>=0) { btn = el; break; }
    }
    if (!btn) {
      var forms = document.querySelectorAll('form');
      if (forms.length > 0) {
        var fs = forms[0].querySelectorAll('button,input[type=""submit""]');
        if (fs.length > 0) btn = fs[0];
      }
    }
    if (btn) { btn.click(); return 'CLICKED'; }
    return 'NOBUTTON';
  } catch(e) { return 'ERROR:' + e.message; }
})();";
                var result = await Browser.CoreWebView2.ExecuteScriptAsync(script);
                if (result is not null && result.Contains("CLICKED"))
                    return;
            }
            catch
            {
            }
        }
    }

    private static bool TryGetHttpsUri(string address, out Uri uri)
    {
        return Uri.TryCreate(address, UriKind.Absolute, out uri!) && uri.Scheme == Uri.UriSchemeHttps;
    }
}