using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Imortais.LinuxClient.Models;
using Imortais.LinuxClient.Network;
using Imortais.LinuxClient.Services;

namespace Imortais.LinuxClient;

public sealed class MainWindow : Window
{
    private static readonly IBrush Bg = Brush("#080B10");
    private static readonly IBrush Panel = Brush("#10151D");
    private static readonly IBrush BorderColor = Brush("#243040");
    private static readonly IBrush Text = Brush("#EDF2F7");
    private static readonly IBrush Muted = Brush("#8190A5");
    private static readonly IBrush Gold = Brush("#D9AA52");
    private static readonly IBrush Green = Brush("#73D99B");
    private static readonly IBrush Red = Brush("#EF7F86");

    private readonly SettingsStore _store = new();
    private readonly Outbox _outbox = new();
    private readonly CombatState _combat = new();
    private readonly TelemetrySender _sender;
    private readonly LinuxPhotonReceiver _receiver;
    private readonly LinuxCaptureService _capture;
    private readonly DispatcherTimer _timer;

    private ClientSettings _settings;
    private bool _gameDetected;
    private bool _tickBusy;
    private int _tick;
    private string? _ctaTime;

    private readonly ObservableCollection<string> _logs = [];
    private readonly ObservableCollection<string> _damageRows = [];
    private readonly ObservableCollection<string> _partyRows = [];

    private readonly TextBlock _warRoomStatus = LabelValue("● NÃO ATIVADO", Gold);
    private readonly TextBlock _albionStatus = LabelValue("● AGUARDANDO", Gold);
    private readonly TextBlock _captureStatus = LabelValue("● PARADA", Gold);
    private readonly TextBlock _playerStatus = LabelValue("-", Text);
    private readonly TextBlock _ctaStatus = LabelValue("-", Text);
    private readonly TextBlock _queueStatus = LabelValue("0", Text);
    private readonly TextBlock _clusterStatus = LabelValue("Mapa desconhecido", Text);
    private readonly TextBlock _partyCount = LabelValue("0", Text);

    private readonly TextBox _pairCode = Input("Código de 6 números");
    private readonly TextBox _serverBox = Input();
    private readonly TextBox _playerBox = Input();
    private readonly TextBox _deviceBox = Input();
    private readonly CheckBox _autoCapture = new() { Content = "Iniciar captura automaticamente", Foreground = Text };
    private readonly CheckBox _autostart = new() { Content = "Iniciar o Combat Client ao entrar no Ubuntu", Foreground = Text };

    public MainWindow()
    {
        Title = "IMORTAIS Combat Client · Linux";
        Width = 1180;
        Height = 760;
        MinWidth = 940;
        MinHeight = 640;
        Background = Bg;
        Foreground = Text;

        _settings = _store.Load();
        _sender = new TelemetrySender(_outbox);
        _receiver = new LinuxPhotonReceiver(_combat, _outbox, () => _settings);
        _capture = new LinuxCaptureService(_receiver);

        _receiver.Log += AddLog;
        _receiver.GameDataDetected += () =>
        {
            _gameDetected = true;
            Dispatcher.UIThread.Post(RefreshHeader);
        };
        _receiver.PartyChanged += () => Dispatcher.UIThread.Post(RefreshParty);
        _receiver.CombatChanged += () => Dispatcher.UIThread.Post(RefreshDamage);
        _capture.StatusChanged += message =>
        {
            AddLog("CAPTURE " + message);
            Dispatcher.UIThread.Post(RefreshHeader);
        };

        Content = BuildUi();
        LoadSettingsIntoUi();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += async (_, _) => await TickAsync();
        _timer.Start();

        Opened += async (_, _) =>
        {
            AddLog("SYSTEM  IMORTAIS Combat Client Linux v0.1.0 iniciado");
            if (_settings.StartCaptureAutomatically) StartCapture();
            await RefreshContextAsync();
            RefreshAll();
        };

        Closing += (_, _) =>
        {
            _timer.Stop();
            _capture.Dispose();
            _sender.Dispose();
        };
    }

    private Control BuildUi()
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Margin = new Thickness(22)
        };

        var title = new StackPanel { Spacing = 2 };
        title.Children.Add(new TextBlock { Text = "IMORTAIS", FontSize = 28, FontWeight = FontWeight.Bold, Foreground = Text });
        title.Children.Add(new TextBlock { Text = "COMBAT CLIENT · LINUX v0.1.0", FontSize = 12, Foreground = Gold });
        Grid.SetRow(title, 0);
        root.Children.Add(title);

        var tabs = new TabControl
        {
            Margin = new Thickness(0, 18, 0, 0),
            Background = Bg,
            Foreground = Text,
            Items =
            {
                new TabItem { Header = "IMORTAIS / Início", Content = BuildHomeTab() },
                new TabItem { Header = "Registro", Content = BuildLogTab() },
                new TabItem { Header = "Medidor de dano", Content = BuildDamageTab() },
                new TabItem { Header = "Party", Content = BuildPartyTab() },
                new TabItem { Header = "Configurações", Content = BuildSettingsTab() }
            }
        };
        Grid.SetRow(tabs, 1);
        root.Children.Add(tabs);
        return root;
    }

    private Control BuildHomeTab()
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("3*,2*"),
            Margin = new Thickness(0, 14, 0, 0)
        };

        var status = Card("STATUS DO OBSERVER");
        var statusBody = (StackPanel)status.Child!;
        statusBody.Children.Add(StatusLine("Albion Online", _albionStatus));
        statusBody.Children.Add(StatusLine("War Room", _warRoomStatus));
        statusBody.Children.Add(StatusLine("Captura", _captureStatus));
        statusBody.Children.Add(StatusLine("Jogador", _playerStatus));
        statusBody.Children.Add(StatusLine("CTA atual", _ctaStatus));
        statusBody.Children.Add(StatusLine("Mapa/cluster", _clusterStatus));
        statusBody.Children.Add(StatusLine("Party", _partyCount));
        statusBody.Children.Add(StatusLine("Outbox", _queueStatus));

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 18, 0, 0) };
        var start = PrimaryButton("INICIAR CAPTURA");
        start.Click += (_, _) => StartCapture();
        var stop = Button("PARAR CAPTURA");
        stop.Click += (_, _) => { _capture.Stop(); RefreshHeader(); };
        var sync = Button("SINCRONIZAR");
        sync.Click += async (_, _) => { await FlushAsync(true); await RefreshContextAsync(); };
        actions.Children.Add(start);
        actions.Children.Add(stop);
        actions.Children.Add(sync);
        statusBody.Children.Add(actions);

        var activation = Card("ATIVAÇÃO NO WAR ROOM");
        var activationBody = (StackPanel)activation.Child!;
        activationBody.Children.Add(new TextBlock
        {
            Text = "Use o mesmo código de 6 números gerado no War Room para ativar este computador.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Muted
        });
        _pairCode.MaxLength = 6;
        _pairCode.Margin = new Thickness(0, 12, 0, 8);
        activationBody.Children.Add(_pairCode);
        var activate = PrimaryButton("ATIVAR ESTE COMPUTADOR");
        activate.Click += async (_, _) => await PairAsync();
        activationBody.Children.Add(activate);

        grid.Children.Add(status);
        Grid.SetColumn(activation, 1);
        activation.Margin = new Thickness(14, 0, 0, 0);
        grid.Children.Add(activation);
        return grid;
    }

    private Control BuildLogTab()
    {
        var panel = Card("REGISTRO AO VIVO");
        var body = (StackPanel)panel.Child!;
        body.Children.Add(new TextBlock
        {
            Text = "Eventos do parser, party, mortes, mapa, captura e comunicação com o War Room.",
            Foreground = Muted,
            Margin = new Thickness(0, 0, 0, 10)
        });
        body.Children.Add(new ListBox
        {
            ItemsSource = _logs,
            Background = Bg,
            Foreground = Text,
            MinHeight = 500,
            FontFamily = FontFamily.Parse("monospace")
        });
        return panel;
    }

    private Control BuildDamageTab()
    {
        var panel = Card("MEDIDOR DE DANO");
        var body = (StackPanel)panel.Child!;

        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        top.Children.Add(new TextBlock
        {
            Text = "Dano e cura capturados de HealthUpdate/HealthUpdates. Os deltas também são enviados ao War Room.",
            Foreground = Muted,
            VerticalAlignment = VerticalAlignment.Center
        });
        var reset = Button("RESETAR");
        reset.Click += (_, _) => { _combat.ResetCombat(); RefreshDamage(); AddLog("COMBAT  medidor resetado"); };
        top.Children.Add(reset);
        body.Children.Add(top);

        body.Children.Add(new TextBlock
        {
            Text = "#   Jogador                         Dano          Cura        K   M",
            Foreground = Gold,
            FontFamily = FontFamily.Parse("monospace"),
            Margin = new Thickness(0, 16, 0, 6)
        });
        body.Children.Add(new ListBox
        {
            ItemsSource = _damageRows,
            Background = Bg,
            Foreground = Text,
            MinHeight = 480,
            FontFamily = FontFamily.Parse("monospace")
        });
        return panel;
    }

    private Control BuildPartyTab()
    {
        var panel = Card("PARTY ATUAL");
        var body = (StackPanel)panel.Child!;
        body.Children.Add(new TextBlock
        {
            Text = "A ordem abaixo vem dos eventos reais PartyJoined / PartyPlayerJoined / PartyPlayerLeft.",
            Foreground = Muted,
            Margin = new Thickness(0, 0, 0, 12)
        });
        body.Children.Add(new ListBox
        {
            ItemsSource = _partyRows,
            Background = Bg,
            Foreground = Text,
            MinHeight = 500,
            FontSize = 15
        });
        return panel;
    }

    private Control BuildSettingsTab()
    {
        var panel = Card("CONFIGURAÇÕES");
        var body = (StackPanel)panel.Child!;

        body.Children.Add(Field("Servidor do War Room", _serverBox));
        body.Children.Add(Field("Nome do jogador", _playerBox));
        body.Children.Add(Field("Device ID", _deviceBox));
        body.Children.Add(_autoCapture);
        body.Children.Add(_autostart);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 18, 0, 0) };
        var save = PrimaryButton("SALVAR");
        save.Click += (_, _) => SaveSettingsFromUi();
        var context = Button("ATUALIZAR CTA");
        context.Click += async (_, _) => await RefreshContextAsync();
        buttons.Children.Add(save);
        buttons.Children.Add(context);
        body.Children.Add(buttons);

        body.Children.Add(new TextBlock
        {
            Text = "A chave do agente é armazenada em ~/.config/imortais-combat-client/settings.json. O outbox fica em ~/.local/state/imortais-combat-client/.",
            Foreground = Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 18, 0, 0)
        });
        return panel;
    }

    private async Task TickAsync()
    {
        if (_tickBusy) return;
        _tickBusy = true;
        try
        {
            _tick++;

            foreach (var delta in _combat.DrainPending())
            {
                await _outbox.AddAsync(TelemetryEvent.Create(
                    "combat_delta",
                    delta.Player,
                    new Dictionary<string, object?>
                    {
                        ["player"] = delta.Player,
                        ["damage"] = delta.Damage,
                        ["healing"] = delta.Healing,
                        ["cluster"] = _combat.CurrentCluster
                    }));
            }

            if (_tick % 15 == 0)
            {
                await _outbox.AddAsync(TelemetryEvent.Create(
                    "client_heartbeat",
                    _settings.PlayerName,
                    new Dictionary<string, object?>
                    {
                        ["version"] = TelemetrySender.ClientVersion,
                        ["gameDetected"] = _gameDetected,
                        ["captureRunning"] = _capture.IsRunning,
                        ["currentCtaId"] = _settings.CurrentCtaId,
                        ["cluster"] = _combat.CurrentCluster,
                        ["partyMembers"] = _combat.SnapshotParty().Count
                    }));
                await RefreshContextAsync();
            }

            if (_tick % 3 == 0) await FlushAsync(false);
            RefreshAll();
        }
        finally { _tickBusy = false; }
    }

    private async Task PairAsync()
    {
        var code = (_pairCode.Text ?? string.Empty).Trim();
        var player = (_playerBox.Text ?? _settings.PlayerName).Trim();
        if (code.Length != 6 || !code.All(char.IsDigit))
        {
            AddLog("PAIR    código deve conter 6 números");
            return;
        }

        SaveSettingsFromUi();
        var result = await _sender.PairAsync(_settings, code, player);
        if (!result.Ok || string.IsNullOrWhiteSpace(result.AgentKey))
        {
            AddLog("PAIR    ERRO · " + result.Message);
            RefreshHeader();
            return;
        }

        _settings.AgentKey = result.AgentKey;
        if (!string.IsNullOrWhiteSpace(result.PlayerName)) _settings.PlayerName = result.PlayerName!;
        _store.Save(_settings);
        _playerBox.Text = _settings.PlayerName;
        _pairCode.Text = string.Empty;
        AddLog("PAIR    computador ativado no War Room");
        await RefreshContextAsync();
        await FlushAsync(true);
        RefreshHeader();
    }

    private void StartCapture()
    {
        var result = _capture.Start();
        AddLog(result.Ok
            ? $"CAPTURE ativa · {result.Devices} interface(s)"
            : "CAPTURE ERRO · " + result.Message);
        RefreshHeader();
    }

    private async Task FlushAsync(bool logSuccess)
    {
        var result = await _sender.FlushAsync(_settings);
        if (!result.Ok) AddLog("WARROOM ERRO · " + result.Message);
        else if (logSuccess && result.Message != "Fila vazia") AddLog("WARROOM " + result.Message);
        RefreshHeader();
    }

    private async Task RefreshContextAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.AgentKey)) return;
        var result = await _sender.RefreshContextAsync(_settings);
        if (!result.Ok)
        {
            AddLog("CTA     contexto · " + result.Message);
            return;
        }

        _settings.CurrentCtaId = result.CtaId;
        _ctaTime = result.CtaTime;
        _store.Save(_settings);
        RefreshHeader();
    }

    private void SaveSettingsFromUi()
    {
        _settings.ServerUrl = (_serverBox.Text ?? string.Empty).Trim();
        _settings.PlayerName = (_playerBox.Text ?? string.Empty).Trim();
        _settings.DeviceId = (_deviceBox.Text ?? string.Empty).Trim();
        _settings.StartCaptureAutomatically = _autoCapture.IsChecked == true;
        _settings.StartWithDesktop = _autostart.IsChecked == true;
        _store.Save(_settings);
        AddLog("CONFIG  configuração salva");
        RefreshHeader();
    }

    private void LoadSettingsIntoUi()
    {
        _serverBox.Text = _settings.ServerUrl;
        _playerBox.Text = _settings.PlayerName;
        _deviceBox.Text = _settings.DeviceId;
        _autoCapture.IsChecked = _settings.StartCaptureAutomatically;
        _autostart.IsChecked = _settings.StartWithDesktop;
    }

    private void RefreshAll()
    {
        RefreshHeader();
        RefreshParty();
        RefreshDamage();
    }

    private void RefreshHeader()
    {
        _albionStatus.Text = _gameDetected ? "● DETECTADO" : "● AGUARDANDO";
        _albionStatus.Foreground = _gameDetected ? Green : Gold;

        _captureStatus.Text = _capture.IsRunning ? "● ATIVA" : "● PARADA";
        _captureStatus.Foreground = _capture.IsRunning ? Green : Gold;

        var activated = !string.IsNullOrWhiteSpace(_settings.AgentKey);
        _warRoomStatus.Text = activated ? "● ATIVADO" : "● NÃO ATIVADO";
        _warRoomStatus.Foreground = activated ? Green : Gold;

        _playerStatus.Text = string.IsNullOrWhiteSpace(_settings.PlayerName) ? "-" : _settings.PlayerName;
        _ctaStatus.Text = string.IsNullOrWhiteSpace(_settings.CurrentCtaId)
            ? "Sem CTA ativo"
            : $"{_ctaTime ?? "CTA"} · #{_settings.CurrentCtaId}";
        _clusterStatus.Text = _combat.CurrentCluster;
        _partyCount.Text = _combat.SnapshotParty().Count.ToString();
        _queueStatus.Text = _outbox.Count.ToString();
    }

    private void RefreshParty()
    {
        var party = _combat.SnapshotParty();
        _partyRows.Clear();
        for (var i = 0; i < party.Count; i++) _partyRows.Add($"{i + 1:00}   {party[i]}");
        if (party.Count == 0) _partyRows.Add("Nenhuma party detectada.");
    }

    private void RefreshDamage()
    {
        var rows = _combat.SnapshotPlayers();
        _damageRows.Clear();
        var i = 1;
        foreach (var row in rows.Take(100))
        {
            _damageRows.Add($"{i++,2}  {row.Name,-28} {row.Damage,12:N0}  {row.Healing,12:N0}  {row.Kills,3} {row.Deaths,3}");
        }
        if (rows.Count == 0) _damageRows.Add("Aguardando eventos de combate...");
    }

    private void AddLog(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _logs.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");
            while (_logs.Count > 500) _logs.RemoveAt(_logs.Count - 1);
        });
    }

    private static Border Card(string title)
    {
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(new TextBlock { Text = title, Foreground = Gold, FontWeight = FontWeight.Bold, FontSize = 14 });
        return new Border
        {
            Background = Panel,
            BorderBrush = BorderColor,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16),
            Child = body
        };
    }

    private static Control StatusLine(string label, TextBlock value)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 4) };
        grid.Children.Add(new TextBlock { Text = label, Foreground = Muted });
        Grid.SetColumn(value, 1);
        grid.Children.Add(value);
        return grid;
    }

    private static Control Field(string label, Control input)
    {
        var stack = new StackPanel { Spacing = 5, Margin = new Thickness(0, 0, 0, 12) };
        stack.Children.Add(new TextBlock { Text = label, Foreground = Muted });
        stack.Children.Add(input);
        return stack;
    }

    private static TextBox Input(string? watermark = null) => new()
    {
        Watermark = watermark,
        Background = Bg,
        Foreground = Text,
        BorderBrush = BorderColor,
        Padding = new Thickness(10)
    };

    private static Avalonia.Controls.Button Button(string text) => new()
    {
        Content = text,
        Background = Brush("#151C26"),
        Foreground = Text,
        BorderBrush = Brush("#33425A"),
        Padding = new Thickness(14, 9)
    };

    private static Avalonia.Controls.Button PrimaryButton(string text)
    {
        var button = Button(text);
        button.Background = Brush("#A92631");
        return button;
    }

    private static TextBlock LabelValue(string text, IBrush color) => new()
    {
        Text = text,
        Foreground = color,
        FontWeight = FontWeight.SemiBold
    };

    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));
}
