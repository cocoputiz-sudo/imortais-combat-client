using Serilog;
using StatisticsAnalysisTool.Common;
using StatisticsAnalysisTool.Common.UserSettings;
using StatisticsAnalysisTool.Network.Manager;
using StatisticsAnalysisTool.ViewModels;
using StatisticsAnalysisTool.Imortais;
using StatisticsAnalysisTool.Imortais.Highlights;
using StatisticsAnalysisTool.Updater;
using System;
using System.IO;
using System.Linq;
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
    private readonly HighlightRecorderService _highlightRecorder;

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
        _highlightRecorder = new HighlightRecorderService();
        _highlightRecorder.StatusChanged += HighlightRecorder_OnStatusChanged;
        _mainWindowViewModel = mainWindowViewModel;
        DataContext = _mainWindowViewModel;
        Loaded += MainWindow_OnLoaded;
        UpdateApplicationUptime();
        ImortaisEventBridge.Start();
        UpdateImortaisStatus();
        UpdateImortaisBackgroundRecordingUi();
        UpdateImortaisGuildDumpUi();
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
        _highlightRecorder.StatusChanged -= HighlightRecorder_OnStatusChanged;
        _highlightRecorder.Dispose();
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
        ImortaisHighlightFpsCombo.SelectedIndex = NormalizeHighlightFps(settings.ImortaisHighlightFps) == 30 ? 0 : 1;
        _albionGameProcessMonitor.SetMonitoringEnabled(AlbionGameProcessMonitor.IsMonitoringRequired(settings));
        if (settings.IsImortaisBackgroundRecordingEnabled)
        {
            _highlightRecorder.Enable(
                NormalizeHighlightFps(settings.ImortaisHighlightFps),
                settings.IsImortaisAutoSaveAbatesEnabled);
        }
        UpdateImortaisBackgroundRecordingUi();

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
        if (settings.IsImortaisBackgroundRecordingEnabled)
        {
            _highlightRecorder.OnAlbionStarted(
                NormalizeHighlightFps(settings.ImortaisHighlightFps),
                settings.IsImortaisAutoSaveAbatesEnabled);
        }
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
        if (settings.IsImortaisBackgroundRecordingEnabled)
        {
            _highlightRecorder.OnAlbionStopped();
        }
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
        var settings=SettingsController.CurrentSettings;
        if(settings.IsImortaisBackgroundRecordingEnabled)
        {
            _highlightRecorder.Tick(NormalizeHighlightFps(settings.ImortaisHighlightFps));
        }
        UpdateImortaisStatus();
        UpdateImortaisBackgroundRecordingUi();
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
        ImortaisEventBridge.SetGameDetected(gameDetected);
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

        ImortaisDiagnosticsHeartbeatText.Text = status.LastHeartbeatEnqueuedUtc.HasValue
            ? $"{FormatRelativeTime(status.LastHeartbeatEnqueuedUtc.Value)} · {status.LastHeartbeatEnqueuedUtc.Value.ToLocalTime():HH:mm:ss}"
            : "aguardando primeiro heartbeat";
        ImortaisDiagnosticsHeartbeatText.Foreground = status.LastHeartbeatEnqueuedUtc.HasValue
            && DateTime.UtcNow - status.LastHeartbeatEnqueuedUtc.Value <= TimeSpan.FromSeconds(30)
                ? Brushes.LimeGreen
                : Brushes.Gold;

        ImortaisDiagnosticsPartyText.Text = status.LastPartySnapshotAtUtc.HasValue
            ? $"{status.LastPartyMemberCount} membro{(status.LastPartyMemberCount == 1 ? string.Empty : "s")} · {FormatRelativeTime(status.LastPartySnapshotAtUtc.Value)} · {status.PartySnapshotDeduplicatedCount} repetido{(status.PartySnapshotDeduplicatedCount == 1 ? string.Empty : "s")} ignorado{(status.PartySnapshotDeduplicatedCount == 1 ? string.Empty : "s")}"
            : $"nenhum snapshot · {status.PartySnapshotDeduplicatedCount} repetido{(status.PartySnapshotDeduplicatedCount == 1 ? string.Empty : "s")} ignorado{(status.PartySnapshotDeduplicatedCount == 1 ? string.Empty : "s")}";

        ImortaisDiagnosticsGuildText.Text = status.LastGuildPresenceProbeAtUtc.HasValue
            ? $"{status.GuildPresenceDistinctPlayers} jogador{(status.GuildPresenceDistinctPlayers == 1 ? string.Empty : "es")} distintos · {status.GuildPresenceProbeCount} eventos · último {FormatRelativeTime(status.LastGuildPresenceProbeAtUtc.Value)}"
            : "aguardando Guild Presence";

        ImortaisDiagnosticsGameText.Text = status.GameDetected switch
        {
            true => "SIM · dados do Albion detectados",
            false => "NÃO · aguardando dados do Albion",
            _ => "aguardando detecção"
        };
        ImortaisDiagnosticsGameText.Foreground = status.GameDetected == true ? Brushes.LimeGreen : Brushes.Gold;

        ImortaisDiagnosticsActivityText.Text = status.RecentActivity.Count > 0
            ? string.Join(Environment.NewLine, status.RecentActivity.Reverse())
            : "Nenhuma atividade registrada nesta sessão.";

        ImortaisDiagnosticsErrorText.Text = string.IsNullOrWhiteSpace(status.LastError)
            ? "nenhum"
            : status.LastError;
    }

    private static int NormalizeHighlightFps(int fps) => fps == 30 ? 30 : 60;

    private void HighlightRecorder_OnStatusChanged(object sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(UpdateImortaisBackgroundRecordingUi);
            return;
        }
        UpdateImortaisBackgroundRecordingUi();
    }

    private void UpdateImortaisBackgroundRecordingUi()
    {
        if (ImortaisBackgroundRecordingButton == null) return;

        var requested = SettingsController.CurrentSettings.IsImortaisBackgroundRecordingEnabled;
        var status = _highlightRecorder.Snapshot();

        if (!requested || status.State == HighlightRecorderService.RecorderState.Disabled)
        {
            ImortaisBackgroundRecordingButton.Content = "GRAVAÇÃO: NÃO";
            ImortaisBackgroundRecordingButton.Foreground = Brushes.IndianRed;
        }
        else if (status.State == HighlightRecorderService.RecorderState.Unavailable)
        {
            ImortaisBackgroundRecordingButton.Content = "HIGHLIGHTS INDISPONÍVEIS";
            ImortaisBackgroundRecordingButton.Foreground = Brushes.IndianRed;
        }
        else
        {
            ImortaisBackgroundRecordingButton.Content = status.State switch
            {
                HighlightRecorderService.RecorderState.Running => "GRAVAÇÃO: SIM",
                HighlightRecorderService.RecorderState.Starting => "GRAVAÇÃO: INICIANDO",
                _ => "GRAVAÇÃO: SIM · AGUARDANDO"
            };
            ImortaisBackgroundRecordingButton.Foreground =
                status.State == HighlightRecorderService.RecorderState.Running ? Brushes.LimeGreen : Brushes.Gold;
        }

        ImortaisBackgroundRecordingButton.ToolTip = status.Message ??
            (requested ? "Replay buffer habilitado." : "Nenhuma captura ou encoder de highlights está ativo.");

        if(ImortaisAutoSaveAbatesCheckBox!=null)
        {
            ImortaisAutoSaveAbatesCheckBox.IsChecked=SettingsController.CurrentSettings.IsImortaisAutoSaveAbatesEnabled;
            ImortaisAutoSaveAbatesCheckBox.IsEnabled=requested;
        }

        if (ImortaisHighlightStatusText != null)
        {
            var border = status.State == HighlightRecorderService.RecorderState.Running
                ? status.BorderlessRequestAccepted
                    ? " · pedido sem borda aceito pela API (confirme visualmente)"
                    : " · a borda de captura pode permanecer"
                : string.Empty;
            var frameError = status.State == HighlightRecorderService.RecorderState.Running
                             && !string.IsNullOrWhiteSpace(status.LastCaptureError)
                ? $" · ERRO [{status.LastCaptureErrorStage ?? "FRAME"}]: {status.LastCaptureError}"
                : string.Empty;
            var audio = status.State == HighlightRecorderService.RecorderState.Running
                ? $" · {status.AudioStatus}" +
                  (string.IsNullOrWhiteSpace(status.AudioError) ? string.Empty : $" (erro: {status.AudioError})")
                : string.Empty;
            var recentlySaved=status.LastAutoSavedUtc.HasValue
                && DateTime.UtcNow-status.LastAutoSavedUtc.Value<TimeSpan.FromSeconds(10);
            var savedNotice=recentlySaved
                ? $" · Highlight salvo · {Path.GetFileName(status.LastAutoSavedPath)}"
                : string.Empty;
            var autoError=string.IsNullOrWhiteSpace(status.LastAutoSaveError)
                ? string.Empty
                : $" · auto-highlight: {status.LastAutoSaveError}";
            ImortaisHighlightStatusText.Text = (status.Message ?? "Gravação em segundo plano desligada.") + savedNotice + border + audio + frameError + autoError;
            ImortaisHighlightStatusText.Foreground = !string.IsNullOrEmpty(frameError)
                ? Brushes.IndianRed
                : status.State switch
                {
                    HighlightRecorderService.RecorderState.Running => Brushes.LightGreen,
                    HighlightRecorderService.RecorderState.Unavailable => Brushes.IndianRed,
                    HighlightRecorderService.RecorderState.Disabled => Brushes.LightSlateGray,
                    _ => Brushes.Gold
                };
        }

        if (ImortaisHighlightMetricsText != null)
        {
            var mib = status.BufferBytes / 1024d / 1024d;
            var encoder = string.IsNullOrWhiteSpace(status.EncoderName) ? "encoder parado" : status.EncoderName;
            var format = status.Width.HasValue
                ? $"{status.Width}x{status.Height} · {status.Fps} FPS"
                : $"{NormalizeHighlightFps(SettingsController.CurrentSettings.ImortaisHighlightFps)} FPS";
            var audioMib = status.AudioBytes / 1024d / 1024d;
            ImortaisHighlightMetricsText.Text =
                $"Buffer: {status.BufferedDuration.TotalSeconds:0.0}s / 150s · {mib:0.0} MiB / 192 MiB · {format} · {encoder} · frames {status.FramesAccepted} / descartados {status.FramesSkipped} · keyframes {status.CleanPoints} · áudio {status.AudioBufferedDuration.TotalSeconds:0.0}s / {audioMib:0.0} MiB";
        }

        if(ImortaisHighlightMetricsText!=null)
        {
            ImortaisHighlightMetricsText.Text +=
                $" · auto gatilhos {status.AutoTriggersReceived} / salvos {status.AutoClipsSaved} / fila {status.AutoQueueCurrent}";
        }

        if (ImortaisSaveTestReplayButton != null)
        {
            ImortaisSaveTestReplayButton.IsEnabled = status.State == HighlightRecorderService.RecorderState.Running
                                                     && status.BufferedDuration >= TimeSpan.FromSeconds(2);
        }
    }

    private async void ImortaisBackgroundRecording_Click(object sender, RoutedEventArgs e)
    {
        if (ImortaisBackgroundRecordingButton == null) return;

        ImortaisBackgroundRecordingButton.IsEnabled = false;
        try
        {
            var settings = SettingsController.CurrentSettings;
            settings.IsImortaisBackgroundRecordingEnabled = !settings.IsImortaisBackgroundRecordingEnabled;

            if (settings.IsImortaisBackgroundRecordingEnabled)
            {
                _albionGameProcessMonitor.SetMonitoringEnabled(true);
                _highlightRecorder.Enable(
                    NormalizeHighlightFps(settings.ImortaisHighlightFps),
                    settings.IsImortaisAutoSaveAbatesEnabled);
            }
            else
            {
                _highlightRecorder.Disable();
            }

            await SettingsController.SaveSettingsAsync();
            _albionGameProcessMonitor.SetMonitoringEnabled(AlbionGameProcessMonitor.IsMonitoringRequired(settings));
        }
        finally
        {
            UpdateImortaisBackgroundRecordingUi();
            ImortaisBackgroundRecordingButton.IsEnabled = true;
        }
    }

    private async void ImortaisAutoSaveOption_Click(object sender,RoutedEventArgs e)
    {
        var settings=SettingsController.CurrentSettings;
        settings.IsImortaisAutoSaveAbatesEnabled=ImortaisAutoSaveAbatesCheckBox?.IsChecked!=false;
        _highlightRecorder.UpdateTriggerOptions(settings.IsImortaisAutoSaveAbatesEnabled);
        await SettingsController.SaveSettingsAsync();
        UpdateImortaisBackgroundRecordingUi();
    }

    private async void ImortaisHighlightFps_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_highlightRecorder == null || ImortaisHighlightFpsCombo?.SelectedItem is not System.Windows.Controls.ComboBoxItem item)
        {
            return;
        }

        if (!int.TryParse(item.Tag?.ToString(), out var fps)) return;
        fps = NormalizeHighlightFps(fps);
        var settings = SettingsController.CurrentSettings;
        if (NormalizeHighlightFps(settings.ImortaisHighlightFps) == fps) return;

        settings.ImortaisHighlightFps = fps;
        await SettingsController.SaveSettingsAsync();

        if (settings.IsImortaisBackgroundRecordingEnabled)
        {
            _highlightRecorder.Disable();
            _highlightRecorder.Enable(
                fps,
                settings.IsImortaisAutoSaveAbatesEnabled);
        }
        UpdateImortaisBackgroundRecordingUi();
    }

    private async void ImortaisSaveTestReplay_Click(object sender, RoutedEventArgs e)
    {
        if (ImortaisSaveTestReplayButton == null) return;
        ImortaisSaveTestReplayButton.IsEnabled = false;
        try
        {
            ImortaisHighlightStatusText.Text = "Salvando replay de teste…";
            ImortaisHighlightStatusText.Foreground = Brushes.Gold;
            var path = await _highlightRecorder.SaveTestReplayAsync();
            ImortaisHighlightStatusText.Text = "Replay salvo: " + path;
            ImortaisHighlightStatusText.Foreground = Brushes.LimeGreen;
        }
        catch (Exception ex)
        {
            ImortaisHighlightStatusText.Text = "Falha ao salvar replay: " + ex.Message;
            ImortaisHighlightStatusText.Foreground = Brushes.IndianRed;
        }
        finally
        {
            UpdateImortaisBackgroundRecordingUi();
        }
    }

    private async void ImortaisPairing_Click(object sender, RoutedEventArgs e)
    {
        var code = ImortaisPairingCodeTextBox?.Text?.Trim() ?? string.Empty;
        if (code.Length != 6 || !int.TryParse(code, out _))
        {
            ImortaisPairingStatusText.Text = "Informe o código de 6 dígitos.";
            ImortaisPairingStatusText.Foreground = Brushes.IndianRed;
            return;
        }

        ImortaisPairingButton.IsEnabled = false;
        ImortaisPairingStatusText.Text = "Ativando...";
        ImortaisPairingStatusText.Foreground = Brushes.Gold;
        try
        {
            var playerName = _mainWindowViewModel?.UserTrackingBindings?.Username;
            var result = await ImortaisEventBridge.PairAsync(code, playerName);
            ImortaisPairingStatusText.Text = result.Message;
            ImortaisPairingStatusText.Foreground = result.Success ? Brushes.LimeGreen : Brushes.IndianRed;
            if (result.Success)
            {
                ImortaisPairingCodeTextBox.Clear();
                UpdateImortaisStatus();
            }
        }
        finally
        {
            ImortaisPairingButton.IsEnabled = true;
        }
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

    private void UpdateImortaisGuildDumpUi()
    {
        if (ImortaisGuildDumpToggleButton == null) return;
        var enabled = ImortaisEventBridge.IsGuildProbeLocalDiagnosticsEnabled;
        ImortaisGuildDumpToggleButton.Content = enabled
            ? "GUILD DUMPS: LIGADO"
            : "GUILD DUMPS: DESLIGADO";
        ImortaisGuildDumpToggleButton.Foreground = enabled ? Brushes.LightGreen : Brushes.Gold;
    }

    private void ImortaisGuildDumpToggle_Click(object sender, RoutedEventArgs e)
    {
        var enable = !ImortaisEventBridge.IsGuildProbeLocalDiagnosticsEnabled;
        if (enable)
        {
            var choice = MessageBox.Show(
                "Ativar diagnóstico LOCAL dos rankings Guild Challenge e Guild Might?\n\n" +
                "Esses três tipos de pacotes serão escritos somente no disco, sem envio ao War Room. " +
                "Os arquivos podem conter nomes e identificadores de jogadores.\n\n" +
                "Clique em ABRIR PASTA DE DUMPS para localizar os arquivos.",
                "IMORTAIS - diagnóstico de Guilda",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (choice != MessageBoxResult.Yes) return;
        }

        try
        {
            ImortaisEventBridge.SetGuildProbeLocalDiagnosticsEnabled(enable);
            UpdateImortaisGuildDumpUi();
        }
        catch (Exception error)
        {
            MessageBox.Show("Não foi possível salvar a opção de diagnóstico: " + error.Message,
                "IMORTAIS Combat Client", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ImortaisOpenGuildDumps_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var directory = Path.Combine(ImortaisTelemetryConfig.DirectoryPath, "Diagnostics");
            Directory.CreateDirectory(directory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = directory,
                UseShellExecute = true
            });
        }
        catch (Exception error)
        {
            MessageBox.Show("Não foi possível abrir a pasta de dumps: " + error.Message,
                "IMORTAIS Combat Client", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CopyImortaisDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        var status = ImortaisEventBridge.GetStatus();
        var partyCount = Math.Max(0, _mainWindowViewModel?.PartyMemberNumber ?? 0);
        var builder = new StringBuilder();

        builder.AppendLine("IMORTAIS COMBAT CLIENT - DIAGNÓSTICO");
        builder.AppendLine($"Guild dumps locais: {(ImortaisEventBridge.IsGuildProbeLocalDiagnosticsEnabled ? "ATIVADOS" : "DESLIGADOS")}");
        builder.AppendLine($"Versão: v{GetCombatClientVersion()}");
        builder.AppendLine($"Updater: {AutoUpdateController.LastUpdateCheckStatus}");
        builder.AppendLine($"Última checagem: {(AutoUpdateController.LastUpdateCheckUtc.HasValue ? AutoUpdateController.LastUpdateCheckUtc.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") : "não realizada")}");
        builder.AppendLine($"War Room: {(status.WarRoomConnected ? "CONECTADO" : "DESCONECTADO")}");
        builder.AppendLine($"Jogador: {(string.IsNullOrWhiteSpace(status.PlayerName) ? "não identificado" : status.PlayerName)}");
        builder.AppendLine($"CTA: {(!string.IsNullOrWhiteSpace(status.CtaTime) ? status.CtaTime : !string.IsNullOrWhiteSpace(status.CtaEventId) ? status.CtaEventId : "nenhum")}");
        builder.AppendLine($"Party atual detectada: {partyCount}");
        builder.AppendLine($"Último party snapshot: {(status.LastPartySnapshotAtUtc.HasValue ? status.LastPartySnapshotAtUtc.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") : "nenhum")} / {status.LastPartyMemberCount} membros");
        builder.AppendLine($"Party snapshots repetidos ignorados: {status.PartySnapshotDeduplicatedCount}");
        builder.AppendLine($"Último heartbeat enfileirado: {(status.LastHeartbeatEnqueuedUtc.HasValue ? status.LastHeartbeatEnqueuedUtc.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") : "nenhum")}");
        builder.AppendLine($"Albion detectado: {(status.GameDetected == true ? "SIM" : status.GameDetected == false ? "NÃO" : "INDEFINIDO")}");
        builder.AppendLine($"Gravação em segundo plano: {(SettingsController.CurrentSettings.IsImortaisBackgroundRecordingEnabled ? "SIM" : "NÃO")}");
        var highlight = _highlightRecorder.Snapshot();
        builder.AppendLine($"Highlights: {highlight.State} · {highlight.BufferedDuration.TotalSeconds:0.0}s · {highlight.BufferBytes / 1024d / 1024d:0.0} MiB · {highlight.Width}x{highlight.Height} · {highlight.Fps} FPS");
        var gopEfetivo=highlight.EffectiveGopFrames>0?$"{highlight.EffectiveGopFrames} frames/keyframe":"aguardando 2 keyframes";
        builder.AppendLine($"Encoder highlights: {highlight.EncoderName ?? "nenhum"} · frames {highlight.FramesAccepted} / descartados {highlight.FramesSkipped} · keyframes {highlight.CleanPoints} · GOP efetivo {gopEfetivo} · alvo {highlight.TargetGopFrames?.ToString() ?? "n/d"} · NV12 em uso {highlight.Nv12SurfacesInUse}/{highlight.Nv12PoolCapacity}");
        var retention=highlight.WgcReceived>0?100d*highlight.EncoderSubmitted/highlight.WgcReceived:0;
        builder.AppendLine($"Pipeline highlights: WGC recebidos {highlight.WgcReceived} · pacer rejeitou {highlight.PacerRejected} · FrameBusy {highlight.FrameBusyCallbacks} · tamanho mudou {highlight.ContentSizeChanged} · NV12 pool cheio {highlight.Nv12PoolFull} · enviados encoder {highlight.EncoderSubmitted} · codificados {highlight.EncodedSamples}");
        builder.AppendLine($"FPS highlights (10s): WGC {highlight.WgcFps10s:0.0} · submit {highlight.SubmitFps10s:0.0} · encoded {highlight.EncodedFps10s:0.0} · retenção submit/WGC {retention:0.0}%");
        builder.AppendLine($"Intervalo WGC (10s): mín {highlight.WgcIntervalMinMs:0.00} ms · médio {highlight.WgcIntervalAverageMs:0.00} ms · máx {highlight.WgcIntervalMaxMs:0.00} ms");
        builder.AppendLine($"Erros pipeline: WGC_SURFACE {highlight.WgcSurfaceErrors} · BGRA_TO_NV12 {highlight.BgraToNv12Errors} · H264_WRITE_SAMPLE {highlight.H264WriteErrors} · FRAME_PIPELINE {highlight.FramePipelineErrors}");
        builder.AppendLine($"Auto highlights: gatilhos {highlight.AutoTriggersReceived} · coalescidos {highlight.AutoTriggersCoalesced} · dedup {highlight.AutoTriggersDeduplicated} · pressão desc {highlight.AutoTriggersDroppedPressure} · salvos {highlight.AutoClipsSaved} · falhos {highlight.AutoClipsFailed} · fila atual {highlight.AutoQueueCurrent}");
        builder.AppendLine($"Pressão highlights: inbox {highlight.TriggerInboxCurrent}/{MaxTriggerInboxForDiagnostics()} (pico {highlight.TriggerInboxPeak}) · snapshots ativos sessão {highlight.ActiveSnapshotOperations} · adiados {highlight.SnapshotPressureDeferrals} · memória snapshots {highlight.DetachedSnapshotBytes/1024d/1024d:0.0} MiB (pico {highlight.DetachedSnapshotPeakBytes/1024d/1024d:0.0} MiB)");
        builder.AppendLine($"Disco highlights: {highlight.HighlightFolderBytes/1024d/1024d/1024d:0.00} GiB / {highlight.HighlightQuotaBytes/1024d/1024d/1024d:0.00} GiB · tempos saves {highlight.RecentSaveDurations}");
        builder.AppendLine($"Impacto último save: snapshot {highlight.LastSnapshotMilliseconds:0.0} ms · FrameBusy Δ{highlight.LastSaveFrameBusyDelta} · WGC máx antes {highlight.LastSaveWgcMaxBeforeMs:0.00} ms / depois {highlight.LastSaveWgcMaxAfterMs:0.00} ms");
        builder.AppendLine($"Áudio highlights: {highlight.AudioStatus} · {highlight.AudioBufferedDuration.TotalSeconds:0.0}s · {highlight.AudioBytes / 1024d / 1024d:0.0} MiB · pacotes {highlight.AudioPackets} · lacunas detectadas {highlight.AudioGapsDetected}" +
                           (string.IsNullOrWhiteSpace(highlight.AudioError) ? string.Empty : $" · erro {highlight.AudioError}"));
        builder.AppendLine($"Borda highlights: API={(highlight.BorderPropertyAvailable ? "SIM" : "NÃO")} · pedido sem borda={(highlight.BorderlessRequestAccepted ? "ACEITO" : "NÃO/INDISPONÍVEL")}");
        builder.AppendLine($"Último replay: {highlight.LastSavedPath ?? "nenhum"}");
        builder.AppendLine($"Último erro highlights: {(highlight.LastCaptureError is null ? "nenhum" : $"[{highlight.LastCaptureErrorStage ?? "FRAME"}] {highlight.LastCaptureError}")}");
        builder.AppendLine($"Guild Presence: {status.GuildPresenceProbeCount} eventos / {status.GuildPresenceDistinctPlayers} jogadores distintos / último {(status.LastGuildPresenceProbeAtUtc.HasValue ? status.LastGuildPresenceProbeAtUtc.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") : "nenhum")}");
        builder.AppendLine($"Guild Might Probe: {status.GuildMightProbeCount} eventos / {status.GuildMightOperationCount} operações / último {(status.LastGuildMightProbeAtUtc.HasValue ? status.LastGuildMightProbeAtUtc.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") : "nenhum")}");
        builder.AppendLine($"Último contato: {(status.LastSuccessfulContactUtc.HasValue ? status.LastSuccessfulContactUtc.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") : "nenhum")}");
        builder.AppendLine($"Outbox: {status.PendingEvents} eventos / {FormatByteCount(status.PendingBytes)}");
        builder.AppendLine($"Último erro: {(string.IsNullOrWhiteSpace(status.LastError) ? "nenhum" : status.LastError)}");
        builder.AppendLine("Atividade recente:");
        foreach (var line in status.RecentActivity.Reverse())
        {
            builder.AppendLine("  " + line);
        }

        Clipboard.SetText(builder.ToString());

        _ = MessageBox.Show(
            "Diagnóstico copiado para a área de transferência.",
            "IMORTAIS Combat Client",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private static int MaxTriggerInboxForDiagnostics()=>512;

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