using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

using Microsoft.Extensions.Configuration;
using Microsoft.Web.WebView2.Core;

namespace FinanceFlow.Desktop;

/// <summary>
/// The window of the desktop application of BarsiG.
/// It shows the web interface of the simulator inside a control of WebView2, starts
/// the server of BarsiG inside the same process and draws the title bar of the window.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>The path of the endpoint that the window uses to check the server.</summary>
    private const string HealthPath = "/health";

    /// <summary>How often the window checks that the server is alive.</summary>
    private static readonly TimeSpan HealthInterval = TimeSpan.FromSeconds(5);

    private ServerHost? _host;
    private CoreWebView2? _coreWebView;
    private string? _userDataFolder;
    private HttpClient? _healthClient;
    private DispatcherTimer? _healthTimer;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private Task? _resourceDisposal;
    private BarsigTheme _theme = BarsigTheme.Light;
    private bool _themeApplied;
    private string _serverOrigin = string.Empty;
    private bool _serverOnline;
    private bool _startupInProgress;
    private bool _startupComplete;
    private bool _isClosing;
    private bool _shutdownInProgress;
    private bool _allowClose;

    /// <summary>Creates the window.</summary>
    public MainWindow()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
        SourceInitialized += OnSourceInitialized;
        StateChanged += OnStateChanged;
        PreviewKeyDown += OnWindowPreviewKeyDown;
    }

    /// <summary>The colour scheme that is currently shown by the window.</summary>
    public BarsigTheme Theme => _theme;

    /// <summary>The origin of the server that the window talks to, without a trailing slash.</summary>
    public string ServerOrigin => _serverOrigin;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        if (_startupInProgress || _startupComplete || _isClosing)
        {
            return;
        }

        _startupInProgress = true;

        try
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .AddEnvironmentVariables()
                .Build();

            _host = await ServerHost.StartAsync(configuration, _lifetimeCancellation.Token);

            // The origin is the part of the address that a browser can open: the scheme,
            // the name of the host and the port. The path of the request is never a part
            // of it, so the address can be copied and opened without any change.
            _serverOrigin = _host.Address.GetLeftPart(UriPartial.Authority).TrimEnd('/');

            _healthClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

            string applicationFolder = new DirectoryInfo(
                    AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                .Name;

            // Keep the experimental WebView2 profile separate from the one used by any
            // other installation of BarsiG. The name of this local publish directory
            // distinguishes the UI demo from the isolated desktop E2E test build.
            _userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BarsiG",
                "UI-Demo-Local",
                applicationFolder,
                "WebView");

            string userDataFolder = _userDataFolder;

            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataFolder);

            await Browser.EnsureCoreWebView2Async(environment);

            if (_isClosing)
            {
                return;
            }

            _coreWebView = Browser.CoreWebView2;
            ConfigureBrowser(_coreWebView);

            _coreWebView.NavigationCompleted += OnNavigationCompleted;

            Browser.Source = new Uri(_serverOrigin + "/");

            LoadingPanel.Visibility = Visibility.Collapsed;

            StartHealthTimer();
            _startupComplete = true;
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            // Closing while startup is in progress is a normal cancellation.
        }
        catch (Exception exception)
        {
            await DisposeHostedResourcesAsync();

            if (!_isClosing)
            {
                ShowError(exception);
            }
        }
        finally
        {
            _startupInProgress = false;
        }
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;

        if (_shutdownInProgress)
        {
            return;
        }

        _shutdownInProgress = true;
        _isClosing = true;
        _lifetimeCancellation.Cancel();

        await DisposeHostedResourcesAsync();

        _allowClose = true;
        Close();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _lifetimeCancellation.Dispose();
    }

    private async Task DisposeHostedResourcesAsync()
    {
        _resourceDisposal ??= DisposeHostedResourcesCoreAsync();
        await _resourceDisposal;
    }

    private async Task DisposeHostedResourcesCoreAsync()
    {
        _healthTimer?.Stop();
        _healthTimer = null;

        _healthClient?.Dispose();
        _healthClient = null;

        if (_coreWebView is { } core)
        {
            core.NavigationCompleted -= OnNavigationCompleted;
            core.NewWindowRequested -= OnNewWindowRequested;
            core.WebMessageReceived -= OnWebMessageReceived;
            _coreWebView = null;
        }

        ServerHost? host = _host;
        _host = null;

        if (host is not null)
        {
            await host.DisposeAsync();
        }
    }

    private async void OnRetry(object sender, RoutedEventArgs e)
    {
        ErrorPanel.Visibility = Visibility.Collapsed;
        LoadingPanel.Visibility = Visibility.Visible;
        _resourceDisposal = null;
        _startupComplete = false;
        await InitializeAsync();
    }

    /// <summary>
    /// Makes the browser of the window safe and quiet: no developer tools, no context menu,
    /// no status bar and no navigation away from the server of the simulation.
    /// </summary>
    private void ConfigureBrowser(CoreWebView2 core)
    {
        CoreWebView2Settings settings = core.Settings;

        settings.AreDevToolsEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsZoomControlEnabled = false;
        settings.AreHostObjectsAllowed = false;
        settings.IsGeneralAutofillEnabled = false;

        core.Settings.UserAgent += " BarsiGDesktop/1.0";

        // The page of BarsiG may only tell the window which colour scheme it uses.
        // The bridge of the window accepts nothing else.
        core.WebMessageReceived += OnWebMessageReceived;
        core.NewWindowRequested += OnNewWindowRequested;
    }

    /// <summary>
    /// Keeps links that request a new window inside the one existing desktop window.
    /// Only the local application origin is allowed to navigate the WebView.
    /// </summary>
    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;

        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri? requested)
            && Uri.TryCreate(_serverOrigin, UriKind.Absolute, out Uri? localOrigin)
            && string.Equals(
                requested.GetLeftPart(UriPartial.Authority),
                localOrigin.GetLeftPart(UriPartial.Authority),
                StringComparison.OrdinalIgnoreCase))
        {
            _coreWebView?.Navigate(requested.ToString());
        }
    }

    // ------------------------------------------------------------------ title bar

    /// <summary>
    /// Drags the window with the pointer and maximizes it with a double click,
    /// exactly like the title bar of Windows does.
    /// </summary>
    private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // The pointer was released before the drag started, nothing has to be done.
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// The right button of the maximize button opens the menu of the system.
    /// Windows 11 shows the layouts of the windows in that menu, so the user can
    /// put the window into half of the screen without any extra code.
    /// </summary>
    private void OnMaximizeButtonMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        try
        {
            NativeMethods.SendMessage(
                new WindowInteropHelper(this).Handle,
                NativeMethods.WM_SYSCOMMAND,
                new IntPtr(NativeMethods.SC_KEYMENU),
                IntPtr.Zero);
        }
        catch (InvalidOperationException)
        {
            // The menu of the system is not available, nothing has to be done.
        }
    }

    /// <summary>Switches the window between the normal and the maximized state.</summary>
    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    /// <summary>Keeps the glyph of the maximize button in step with the state of the window.</summary>
    private void OnStateChanged(object? sender, EventArgs e)
    {
        bool maximized = WindowState == WindowState.Maximized;

        MaximizeGlyph.Data = Geometry.Parse(maximized
            ? "M 2.5,2.5 V 0.5 H 10.5 V 8.5 H 8.5 M 0.5,2.5 H 8.5 V 10.5 H 0.5 Z"
            : "M 0.5,0.5 H 10.5 V 10.5 H 0.5 Z");

        AutomationProperties.SetName(
            MaximizeButton,
            maximized ? "Restore" : "Maximize");    }

    // ------------------------------------------------------------- native window

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        HwndSource? source = PresentationSource.FromVisual(this) as HwndSource;

        source?.AddHook(WindowHook);
    }

    /// <summary>
    /// Keeps a maximized window inside the work area of the monitor and answers
    /// the questions of Windows about the parts of the frame of the window.
    /// </summary>
    private IntPtr WindowHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_GETMINMAXINFO)
        {
            NativeMethods.ApplyMaximizedBounds(hwnd, lParam);
        }

        return IntPtr.Zero;
    }

    // ---------------------------------------------------------------- the theme

    /// <summary>
    /// The web application is the only place where the colour scheme is chosen.
    /// The page tells the window about every change, so both halves of the application
    /// always show the same colour scheme.
    /// </summary>
    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(e.WebMessageAsJson);

            JsonElement root = document.RootElement;

            if (!root.TryGetProperty("type", out JsonElement type))
            {
                return;
            }

            if (!string.Equals(type.GetString(), "theme", StringComparison.Ordinal))
            {
                return;
            }

            if (root.TryGetProperty("theme", out JsonElement theme))
            {
                ApplyTheme(ThemePalette.Parse(theme.GetString()));
            }
        }
        catch (JsonException)
        {
            // A message that is not understood is simply ignored.
        }
    }

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        await SyncThemeFromPageAsync();
    }

    /// <summary>Reads the colour scheme of the page after it was loaded.</summary>
    private async Task SyncThemeFromPageAsync()
    {
        try
        {
            string result = await _coreWebView!.ExecuteScriptAsync(
                "window.financeFlow && window.financeFlow.getCurrentTheme " +
                "? window.financeFlow.getCurrentTheme() : 'light';");

            ApplyTheme(ThemePalette.Parse(result.Trim('"')));
        }
        catch (Exception exception) when (exception is InvalidOperationException or COMException)
        {
            // The page is not ready yet, the light colour scheme stays active.
        }
    }

    /// <summary>Paints the window with the colours of the given colour scheme.</summary>
    private void ApplyTheme(BarsigTheme theme)
    {
        if (_theme == theme && _themeApplied)
        {
            return;
        }

        _theme = theme;

        ThemeColors colors = ThemePalette.For(theme);

        ThemeBrush("TitleBarBackgroundBrush").Color = colors.Chrome;
        ThemeBrush("TitleBarTextBrush").Color = colors.Text;
        ThemeBrush("TitleBarMutedBrush").Color = colors.TextMuted;
        ThemeBrush("TitleBarBorderBrush").Color = colors.Border;
        ThemeBrush("TitleBarHoverBrush").Color = colors.Hover;
        ThemeBrush("TitleBarAccentBrush").Color = colors.Accent;

        ThemeBrush("PopupBackgroundBrush").Color = colors.Popup;
        ThemeBrush("PopupBorderBrush").Color = colors.Border;

        ThemeBrush("PanelBackgroundBrush").Color = colors.Background;
        ThemeBrush("PanelTextBrush").Color = colors.Text;
        ThemeBrush("PanelMutedBrush").Color = colors.TextMuted;

        // The scrollbars of the native controls live in the resources of the application.
        ApplicationResources("BarsiGScrollThumbBrush").Color = colors.ScrollThumb;
        ApplicationResources("BarsiGScrollThumbHoverBrush").Color = colors.ScrollThumbHover;

        _themeApplied = true;
    }

    /// <summary>Returns the brush of the window that carries the given key.</summary>
    private SolidColorBrush ThemeBrush(string key) =>
        (SolidColorBrush)Resources[key];

    /// <summary>Returns the brush of the application that carries the given key.</summary>
    private static SolidColorBrush ApplicationResources(string key) =>
        (SolidColorBrush)System.Windows.Application.Current.Resources[key];

    // --------------------------------------------------------------- the server

    /// <summary>Opens the small window that shows the address of the server.</summary>
    private void OnServerButtonClick(object sender, RoutedEventArgs e)
    {
        ServerAddressText.Text = string.IsNullOrEmpty(_serverOrigin)
            ? "The server is not running."
            : _serverOrigin;

        ServerStateText.Text = _serverOnline
            ? "The server answers, the address leads to the same bank."
            : "The server does not answer at the moment.";

        ServerPopup.IsOpen = true;

        ServerPopup.Opened += OnServerPopupOpened;
    }

    private void OnServerPopupOpened(object? sender, EventArgs e)
    {
        ServerPopup.Opened -= OnServerPopupOpened;

        CopyAddressButton.Focus();
    }

    /// <summary>Copies the address of the server, and nothing else, into the clipboard.</summary>
    private void OnCopyAddressClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_serverOrigin))
        {
            return;
        }

        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Clipboard.SetText(_serverOrigin);
                ServerStateText.Text = "The address is in the clipboard.";
                return;
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // Another program holds the clipboard for a moment, the copy is repeated.
                Thread.Sleep(80);
            }
        }

        ServerStateText.Text = "The clipboard is busy, the address could not be copied.";
    }

    /// <summary>Opens the address of the server in the default browser of the user.</summary>
    private void OnOpenInBrowserClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_serverOrigin))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _serverOrigin,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            ServerStateText.Text = $"The browser could not be started: {exception.Message}";
        }
    }

    /// <summary>Closes the small window of the server when the user presses Escape.</summary>
    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && ServerPopup.IsOpen)
        {
            ServerPopup.IsOpen = false;
            ServerButton.Focus();
            e.Handled = true;
        }
    }

    /// <summary>Checks from time to time that the server of the simulator is alive.</summary>
    private void StartHealthTimer()
    {
        _healthTimer = new DispatcherTimer { Interval = HealthInterval };

        _healthTimer.Tick += async (_, _) => await CheckServerAsync();

        _healthTimer.Start();

        _ = CheckServerAsync();
    }

    private async Task CheckServerAsync()
    {
        if (_healthClient is null || string.IsNullOrEmpty(_serverOrigin))
        {
            return;
        }

        bool online;

        try
        {
            using HttpResponseMessage response = await _healthClient.GetAsync(
                new Uri(_serverOrigin + HealthPath));

            online = response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            online = false;
        }
        catch (TaskCanceledException)
        {
            online = false;
        }

        if (online == _serverOnline)
        {
            return;
        }

        _serverOnline = online;

        StatusDot.Fill = online
            ? ThemeBrush("StatusOnlineBrush")
            : ThemeBrush("StatusOfflineBrush");

        if (ServerPopup.IsOpen)
        {
            ServerStateText.Text = online
                ? "The server answers, the address leads to the same bank."
                : "The server does not answer at the moment.";
        }
    }

    private void ShowError(Exception exception)
    {
        LoadingPanel.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Visible;

        ErrorText.Text =
            $"{exception.Message}{Environment.NewLine}{Environment.NewLine}" +
            "Check that the WebView2 runtime of Microsoft Edge is installed and that the " +
            "port of the local server is free.";

        WriteLog(exception);
    }

    /// <summary>Writes the problem into the log file of the application, next to the executable.</summary>
    private static void WriteLog(Exception exception)
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "barsig-desktop.log");

            File.AppendAllText(
                path,
                $"[{DateTimeOffset.Now:u}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // The log is only a help for the user, it never stops the application.
        }
    }
}
