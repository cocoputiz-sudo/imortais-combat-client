using Serilog;
using StatisticsAnalysisTool.Common;
using StatisticsAnalysisTool.Common.UserSettings;
using StatisticsAnalysisTool.Network.Manager;
using StatisticsAnalysisTool.ViewModels;
using StatisticsAnalysisTool.Imortais;
using StatisticsAnalysisTool.Updater;
using System;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace StatisticsAnalysisTool.Views;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow
{
    private readonly MainWindowViewModel _mainWindowViewModel;
    private readonly WindowChromeController _windowChromeController;
    private readonly DispatcherTimer _applicationUptimeTimer;
    private readonly DispatcherTimer _imortaisStatusTimer;
    private readonly SystemTrayService _systemTrayService;
    private readonly AlbionGameProcessMonitor _albionGameProcessMonitor;

    public MainWindow(MainWindowViewModel mainWindowViewModel)
    {
        InitializeComponent();
        _applicationUptimeTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _applicationUptimeTimer.Tick += ApplicationUptimeTimer_OnTick;
        _imortaisStatusTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _imortaisStatusTimer.Tick += ImortaisStatusTimer_OnTick;
        _windowChromeController = new WindowChromeController(
            this,
            MaximizedButton,
            ResizeMode.CanResizeWithGrip,
            ResizeMode.NoResize);
        InitWindow();
        _systemTrayService = new SystemTrayService(this, mainWindowViewModel);
        _albionGameProcessMonitor = new AlbionGameProcessMonitor();
        ServiceLocator.Register<AlbionGameProcessMonitor>(_albionGameProcessMonitor);
        _albionGameProcessMonitor.GameStarted += AlbionGameProcessMonitor_OnGameStarted;
        _albionGameProcessMonitor.GameStopped += AlbionGameProcessMonitor_OnGameStopped;
        _mainWindowViewModel = mainWindowViewModel;
        DataContext = _mainWindowViewModel;
        Loaded += MainWindow_OnLoaded;
        UpdateApplicationUptime();
        ImortaisEventBridge.Start();
        UpdateImortaisStatus();
        _applicationUptimeTimer.Start();
        _imortaisStatusTimer.Start();
    }

    public void InitWindow()
    {
        Height = SettingsController.CurrentSettings.MainWindowHeight;
        Width = SettingsController.CurrentSettings.MainWindowWidth;
        Left = SettingsController.CurrentSettings.MainWindowLeftPosition;
        Top = SettingsController.CurrentSettings.MainWindowTopPosition;
        if (SettingsController.CurrentSettings.MainWindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }

        if (SettingsController.CurrentSettings.MainWindowLeftPosition == 0 && SettingsController.CurrentSettings.MainWindowLeftPosition == 0)
        {
            Utilities.CenterWindowOnScreen(this);
        }
    }

    private void Hotbar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _windowChromeController.DragMoveOnMouseDown(e);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Application.Current?.Shutdown();
    }

    private void MainWindow_OnClosing(object sender, EventArgs eventArgs)
    {
        var windowStateForPersistence = _systemTrayService.WindowStateForPersistence;
        Loaded -= MainWindow_OnLoaded;
        _applicationUptimeTimer.Stop();
        _imortaisStatusTimer.Stop();
        _albionGameProcessMonitor.GameStarted -= AlbionGameProcessMonitor_OnGameStarted;
        _albionGameProcessMonitor.GameStopped -= AlbionGameProcessMonitor_OnGameStopped;
        _albionGameProcessMonitor.Dispose();
        _systemTrayService.Dispose();
        _mainWindowViewModel.DisposeItemDetails();
        _mainWindowViewModel.CraftingBindings.DisposeLossExplorer();
        SettingsController.SetWindowSettings(windowStateForPersistence, Height, Width, Left, Top);
    }

    private void MainWindow_OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        Loaded -= MainWindow_OnLoaded;

        var settings = SettingsController.CurrentSettings;
        _albionGameProcessMonitor.SetMonitoringEnabled(AlbionGameProcessMonitor.IsMonitoringRequired(settings));
        var shouldRemainVisibleForRunningGame = settings.IsOpenWithGameActive
                                                && _albionGameProcessMonitor.IsGameRunning;
        if (settings.IsStartInSystemTrayActive && !shouldRemainVisibleForRunningGame)
        {
            _systemTrayService.HideWindowInSystemTray(false);
        }
    }

    private async void AlbionGameProcessMonitor_OnGameStarted(object sender, EventArgs eventArgs)
    {
        var settings = SettingsController.CurrentSettings;
        if (settings.IsOpenWithGameActive)
        {
            _systemTrayService.RestoreWindowFromSystemTray();
        }

        if (!settings.IsStartTrackingWithGameActive
            || _mainWindowViewModel.IsTrackingActive
            || !ServiceLocator.IsServiceInDictionary<TrackingController>())
        {
            return;
        }

        try
        {
            await ServiceLocator.Resolve<TrackingController>().StartTrackingAsync();
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Tracking could not be started when Albion Online started");
        }
    }

    private void AlbionGameProcessMonitor_OnGameStopped(object sender, EventArgs eventArgs)
    {
        var settings = SettingsController.CurrentSettings;
        if (settings.IsStopTrackingWithGameActive
            && _mainWindowViewModel.IsTrackingActive
            && ServiceLocator.IsServiceInDictionary<TrackingController>())
        {
            try
            {
                ServiceLocator.Resolve<TrackingController>().StopTracking();
            }
            catch (Exception exception)
            {
                Log.Error(exception, "Tracking could not be stopped when Albion Online closed");
            }
        }

        if (settings.IsHideWithGameActive)
        {
            _systemTrayService.HideWindowInSystemTray(false);
        }
    }

    private void ApplicationUptimeTimer_OnTick(object sender, EventArgs e)
    {
        UpdateApplicationUptime();
    }

    private void UpdateApplicationUptime()
    {
        ApplicationUptimeLabel.Content = App.ApplicationUptime.ToTimerString();
    }

    private void ImortaisStatusTimer_OnTick(object sender, EventArgs e)
    {
        UpdateImortaisStatus();
    }

    private void UpdateImortaisStatus()
    {
        if (_mainWindowViewModel == null) return;

        var status = ImortaisEventBridge.GetStatus();

        if (!status.Enabled)
        {
            SetImortaisStatus(ImortaisHomeWarRoomStatusText, "● DESATIVADO", Brushes.Gray);
            SetImortaisStatus(ImortaisHomeTelemetryStatusText, "● DESATIVADA", Brushes.Gray);
        }
        else if (!status.Configured)
        {
            SetImortaisStatus(ImortaisHomeWarRoomStatusText, "● NÃO CONFIGURADO", Brushes.IndianRed);
            SetImortaisStatus(ImortaisHomeTelemetryStatusText, "● SEM CHAVE", Brushes.IndianRed);
        }
        else if (status.WarRoomConnected)
        {
            SetImortaisStatus(ImortaisHomeWarRoomStatusText, "● CONECTADO", Brushes.LimeGreen);
            var recent = status.LastSuccessfulContactUtc.HasValue
                         && DateTime.UtcNow - status.LastSuccessfulContactUtc.Value < TimeSpan.FromSeconds(15);
            SetImortaisStatus(
                ImortaisHomeTelemetryStatusText,
                recent ? "● ENVIANDO / SINCRONIZADA" : "● CONECTADA",
                recent ? Brushes.LimeGreen : Brushes.Gold);
        }
        else
        {
            SetImortaisStatus(ImortaisHomeWarRoomStatusText, "● RECONECTANDO...", Brushes.Gold);
            var detail = string.IsNullOrWhiteSpace(status.LastError) ? "AGUARDANDO SERVIDOR" : status.LastError;
            if (detail.Length > 42) detail = detail[..42] + "...";
            SetImortaisStatus(ImortaisHomeTelemetryStatusText, "● " + detail.ToUpperInvariant(), Brushes.Gold);
        }

        var gameDetected = _mainWindowViewModel.MainStatusBindings?.IsGameDataDetected == true;
        SetImortaisStatus(
            ImortaisHomeAlbionStatusText,
            gameDetected ? "● CAPTURANDO" : "● AGUARDANDO DADOS...",
            gameDetected ? Brushes.LimeGreen : Brushes.Gold);

        var ctaText = !string.IsNullOrWhiteSpace(status.CtaTime)
            ? $"{status.CtaTime} · VINCULADO"
            : !string.IsNullOrWhiteSpace(status.CtaEventId)
                ? $"#{status.CtaEventId} · VINCULADO"
                : "NENHUM CTA ATIVO";
        SetImortaisStatus(
            ImortaisHomeCtaStatusText,
            ctaText,
            !string.IsNullOrWhiteSpace(status.CtaEventId) ? Brushes.LimeGreen : Brushes.LightSlateGray);

        var partyCount = Math.Max(0, _mainWindowViewModel.PartyMemberNumber);
        SetImortaisStatus(
            ImortaisHomePartyStatusText,
            $"{partyCount} detectado{(partyCount == 1 ? string.Empty : "s")}",
            partyCount > 0 ? Brushes.LightGreen : Brushes.LightSlateGray);

        UpdateImortaisDiagnostics(status);
    }

    private void UpdateImortaisDiagnostics(ImortaisEventBridge.BridgeStatus status)
    {
        if (ImortaisDiagnosticsVersionText == null) return;

        ImortaisDiagnosticsVersionText.Text = $"v{GetCombatClientVersion()}";

        var updaterStatus = AutoUpdateController.LastUpdateCheckStatus;
        ImortaisDiagnosticsUpdaterText.Text = updaterStatus;
        ImortaisDiagnosticsUpdaterText.Foreground = updaterStatus switch
        {
            "ATUALIZADO" => Brushes.LimeGreen,
            "ATUALIZAÇÃO DISPONÍVEL" => Brushes.Gold,
            "FALHA NA VERIFICAÇÃO" => Brushes.IndianRed,
            "VERIFICANDO..." => Brushes.DeepSkyBlue,
            _ => Brushes.LightSlateGray
        };

        ImortaisDiagnosticsLastCheckText.Text = AutoUpdateController.LastUpdateCheckUtc.HasValue
            ? AutoUpdateController.LastUpdateCheckUtc.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss")
            : "ainda não realizada";

        ImortaisDiagnosticsLastContactText.Text = status.LastSuccessfulContactUtc.HasValue
            ? $"{FormatRelativeTime(status.LastSuccessfulContactUtc.Value)} · {status.LastSuccessfulContactUtc.Value.ToLocalTime():HH:mm:ss}"
            : "aguardando primeiro contato";

        ImortaisDiagnosticsOutboxText.Text =
            $"{status.PendingEvents} evento{(status.PendingEvents == 1 ? string.Empty : "s")} · {FormatByteCount(status.PendingBytes)}";

        ImortaisDiagnosticsErrorText.Text = string.IsNullOrWhiteSpace(status.LastError)
            ? "nenhum"
            : status.LastError;
    }

    private async void ImortaisCheckForUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (AutoUpdateController.IsUpdateCheckRunning)
        {
            _ = MessageBox.Show(
                "Já existe uma verificação de atualização em andamento.",
                "IMORTAIS Combat Client",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var button = sender as System.Windows.Controls.Button;
        if (button != null)
        {
            button.IsEnabled = false;
        }

        try
        {
            await AutoUpdateController.CheckForUpdatesAsync();
        }
        finally
        {
            if (button != null)
            {
                button.IsEnabled = true;
            }

            UpdateImortaisStatus();
        }
    }

    private void CopyImortaisDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        var status = ImortaisEventBridge.GetStatus();
        var partyCount = Math.Max(0, _mainWindowViewModel?.PartyMemberNumber ?? 0);
        var builder = new StringBuilder();

        builder.AppendLine("IMORTAIS COMBAT CLIENT - DIAGNÓSTICO");
        builder.AppendLine($"Versão: v{GetCombatClientVersion()}");
        builder.AppendLine($"Updater: {AutoUpdateController.LastUpdateCheckStatus}");
        builder.AppendLine($"Última checagem: {(AutoUpdateController.LastUpdateCheckUtc.HasValue ? AutoUpdateController.LastUpdateCheckUtc.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") : "não realizada")}");
        builder.AppendLine($"War Room: {(status.WarRoomConnected ? "CONECTADO" : "DESCONECTADO")}");
        builder.AppendLine($"Jogador: {(string.IsNullOrWhiteSpace(status.PlayerName) ? "não identificado" : status.PlayerName)}");
        builder.AppendLine($"CTA: {(!string.IsNullOrWhiteSpace(status.CtaTime) ? status.CtaTime : !string.IsNullOrWhiteSpace(status.CtaEventId) ? status.CtaEventId : "nenhum")}");
        builder.AppendLine($"Party: {partyCount}");
        builder.AppendLine($"Último contato: {(status.LastSuccessfulContactUtc.HasValue ? status.LastSuccessfulContactUtc.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") : "nenhum")}");
        builder.AppendLine($"Outbox: {status.PendingEvents} eventos / {FormatByteCount(status.PendingBytes)}");
        builder.AppendLine($"Último erro: {(string.IsNullOrWhiteSpace(status.LastError) ? "nenhum" : status.LastError)}");

        Clipboard.SetText(builder.ToString());

        _ = MessageBox.Show(
            "Diagnóstico copiado para a área de transferência.",
            "IMORTAIS Combat Client",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private static string GetCombatClientVersion()
    {
        var informationalVersion = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion.Split('+')[0].TrimStart('v', 'V');
        }

        return Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
    }

    private static string FormatRelativeTime(DateTime utc)
    {
        var elapsed = DateTime.UtcNow - utc;
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;

        if (elapsed.TotalSeconds < 60) return $"há {Math.Max(0, (int)elapsed.TotalSeconds)}s";
        if (elapsed.TotalMinutes < 60) return $"há {(int)elapsed.TotalMinutes}min";
        if (elapsed.TotalHours < 24) return $"há {(int)elapsed.TotalHours}h";
        return $"há {(int)elapsed.TotalDays}d";
    }

    private static string FormatByteCount(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024d:0.0} KB";
        return $"{bytes / (1024d * 1024d):0.0} MB";
    }

    private static void SetImortaisStatus(System.Windows.Controls.TextBlock target, string text, Brush color)
    {
        if (target == null) return;
        target.Text = text;
        target.Foreground = color;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        _windowChromeController.ToggleMaximize();
    }

    private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _windowChromeController.ToggleMaximizeOnDoubleClick(e);
    }

    private void CopyPartyToClipboard_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var trackingController = ServiceLocator.Resolve<TrackingController>();
        trackingController?.EntityController?.CopyPartyToClipboard();
    }

    private void TatsDropDownOpenClose_PreviewMouseDown(object sender, RoutedEventArgs e)
    {
        _mainWindowViewModel?.SwitchStatsDropDownState();
    }
}