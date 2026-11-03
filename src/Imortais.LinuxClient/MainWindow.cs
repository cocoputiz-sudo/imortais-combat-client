using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Imortais.LinuxClient.Models;
using Imortais.LinuxClient.Network;
using Imortais.LinuxClient.Services;
using StatisticsAnalysisTool.Imortais;

namespace Imortais.LinuxClient;

public sealed class MainWindow : Window
{
    public const string Version = "1.0.0";

    private static readonly IBrush Bg = Brush("#080B10");
    private static readonly IBrush Panel = Brush("#10151D");
    private static readonly IBrush BorderColor = Brush("#243040");
    private static readonly IBrush Text = Brush("#EDF2F7");
    private static readonly IBrush Muted = Brush("#8190A5");
    private static readonly IBrush Gold = Brush("#D9AA52");
    private static readonly IBrush Green = Brush("#73D99B");
    private static readonly IBrush Red = Brush("#EF7F86");

    private readonly SettingsStore _store = new();
    private readonly CombatState _meter = new();
    private readonly GameReceiver _receiver;
    private readonly LinuxCaptureService _capture;
    private readonly DispatcherTimer _timer;
    private readonly LinuxUpdateService _updateService = new();
    private ClientSettings _settings;
    private LinuxReleaseUpdate? _availableUpdate;
    private bool _updateInstalled;
    private string _lastCaptureHealth = string.Empty;

    private readonly ObservableCollection<string> _logs = [];
    private readonly ObservableCollection<string> _damageRows = [];
    private readonly ObservableCollection<string> _partyRows = [];

    // GUILD tab
    private readonly ComboBox _guildCombo = new() { MinWidth = 260 };
    private readonly TextBox _customServer = Input("ou endereço https do War Room de outra guilda");
    private readonly TextBox _pairCode = Input("Código de 6 números");
    private readonly Avalonia.Controls.Button _pairButton = PrimaryButton("ATIVAR TELEMETRIA");
    private readonly TextBlock _pairStatus = Value("Verificando estado do pareamento...", Muted);
    private readonly TextBlock _guildLink = Value("AGUARDANDO O JOGO INFORMAR SUA GUILDA", Muted);
    private readonly TextBlock _pairings = Value("", Muted);

    // Home
    private readonly TextBlock _albion = Value("● AGUARDANDO", Gold);
    private readonly TextBlock _captureState = Value("● PARADA", Gold);
    private readonly TextBlock _captureDetail = Value("0 pacotes · 0 Photon", Muted);
    private readonly TextBlock _warRoom = Value("● NÃO ATIVADO", Gold);
    private readonly TextBlock _character = Value("-", Text);
    private readonly TextBlock _gameGuild = Value("-", Text);
    private readonly TextBlock _cta = Value("-", Text);
    private readonly TextBlock _map = Value("Mapa desconhecido", Text);
    private readonly TextBlock _partyCount = Value("0", Text);
    private readonly TextBlock _nearby = Value("0", Text);
    private readonly TextBlock _queue = Value("0", Text);

    // Settings
    private readonly CheckBox _autoCapture = new() { Content = "Iniciar a captura automaticamente", Foreground = Text };
    private readonly CheckBox _autostart = new() { Content = "Abrir o Combat Client ao entrar no Ubuntu", Foreground = Text };
    private readonly TextBlock _updateStatus = Value("Verificação de versão disponível", Muted);
    private readonly Avalonia.Controls.Button _updateButton = Button("VERIFICAR ATUALIZAÇÕES");
    private readonly TextBlock _diagStatus = Value("Diagnóstico: ainda não exportado", Muted);

    public MainWindow()
    {
        Title = "Guild Combat Client · Linux";
        Width = 1180; Height = 780; MinWidth = 940; MinHeight = 640;
        Background = Bg; Foreground = Text;

        // Migrate the alpha pairing BEFORE the core loads its configuration.
        _settings = _store.Load();
        string? migrated = null;
        try { migrated = LegacyAlphaMigration.Run(_settings); }
        catch (Exception ex) { migrated = "Migração da alpha não concluída: " + ex.Message; }

        _receiver = new GameReceiver(_meter);
        _capture = new LinuxCaptureService(_receiver);
        _receiver.Log += AddLog;
        _receiver.Changed += () => Dispatcher.UIThread.Post(RefreshAll);
        _capture.StatusChanged += m => { AddLog("CAPTURA " + m); Dispatcher.UIThread.Post(RefreshHome); };

        Content = BuildUi();
        _autoCapture.IsChecked = _settings.StartCaptureAutomatically;
        _autostart.IsChecked = _settings.StartWithDesktop;
        foreach (var server in GuildIdentity.KnownServers) _guildCombo.Items.Add(server.Name);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => { RefreshAll(); ReportCaptureHealth(); };

        Opened += (_, _) =>
        {
            AddLog($"SISTEMA Guild Combat Client Linux v{Version} iniciado");
            if (migrated is not null) AddLog("SISTEMA " + migrated);
            ImortaisEventBridge.Start();
            SelectCurrentGuild();
            if (_settings.StartCaptureAutomatically) StartCapture();
            _timer.Start();
            RefreshAll();
            _ = CheckUpdatesAsync();
        };
        Closing += (_, _) => { _timer.Stop(); _capture.Dispose(); };
    }

    private Control BuildUi()
    {
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Margin = new Thickness(22) };
        var title = new StackPanel { Spacing = 2 };
        title.Children.Add(new TextBlock { Text = "GUILD COMBAT CLIENT", FontSize = 26, FontWeight = FontWeight.Bold, Foreground = Text });
        title.Children.Add(new TextBlock { Text = $"LINUX v{Version} · mesmo núcleo de envio do Windows", FontSize = 12, Foreground = Gold });
        root.Children.Add(title);
        var tabs = new TabControl
        {
            Margin = new Thickness(0, 18, 0, 0), Background = Bg, Foreground = Text,
            Items =
            {
                new TabItem { Header = "GUILD", Content = BuildGuildTab() },
                new TabItem { Header = "Início", Content = BuildHomeTab() },
                new TabItem { Header = "Medidor", Content = BuildDamageTab() },
                new TabItem { Header = "Party", Content = BuildPartyTab() },
                new TabItem { Header = "Registro", Content = BuildLogTab() },
                new TabItem { Header = "Configurações", Content = BuildSettingsTab() },
            }
        };
        Grid.SetRow(tabs, 1);
        root.Children.Add(tabs);
        return root;
    }

    private Control BuildGuildTab()
    {
        var card = Card("ATIVAÇÃO NA GUILDA");
        var body = (StackPanel)card.Child!;
        body.Children.Add(Wrap("Escolha a guilda (ou cole o endereço do War Room dela), gere um código de 6 dígitos no War Room e informe abaixo. " +
            "Tem personagens em mais de uma guilda? Ative uma vez em cada: o client envia para a guilda do personagem logado.", Muted));
        var row = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        row.Children.Add(Field("GUILDA", _guildCombo, 280));
        _pairCode.MaxLength = 6; _pairCode.Width = 150;
        row.Children.Add(Field("CÓDIGO", _pairCode, 170));
        _pairButton.VerticalAlignment = VerticalAlignment.Bottom;
        _pairButton.Margin = new Thickness(0, 0, 0, 12);
        _pairButton.Click += async (_, _) => await PairAsync();
        row.Children.Add(_pairButton);
        body.Children.Add(row);
        body.Children.Add(_customServer);
        body.Children.Add(_pairStatus);
        _guildLink.FontWeight = FontWeight.SemiBold;
        body.Children.Add(_guildLink);
        body.Children.Add(_pairings);
        return card;
    }

    private Control BuildHomeTab()
    {
        var card = Card("STATUS DO OBSERVADOR");
        var body = (StackPanel)card.Child!;
        body.Children.Add(Line("Albion Online", _albion));
        body.Children.Add(Line("Captura", _captureState));
        body.Children.Add(Line("Rede / Photon", _captureDetail));
        body.Children.Add(Line("War Room", _warRoom));
        body.Children.Add(Line("Personagem", _character));
        body.Children.Add(Line("Guilda no jogo", _gameGuild));
        body.Children.Add(Line("CTA atual", _cta));
        body.Children.Add(Line("Mapa", _map));
        body.Children.Add(Line("Jogadores na party", _partyCount));
        body.Children.Add(Line("Jogadores por perto", _nearby));
        body.Children.Add(Line("Fila para o War Room", _queue));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 16, 0, 0) };
        var start = PrimaryButton("INICIAR CAPTURA");
        start.Click += (_, _) => StartCapture();
        var stop = Button("PARAR CAPTURA");
        stop.Click += (_, _) => { _capture.Stop(); RefreshHome(); };
        actions.Children.Add(start); actions.Children.Add(stop);
        body.Children.Add(actions);
        return card;
    }

    private Control BuildDamageTab()
    {
        var card = Card("MEDIDOR (SUA PARTY)");
        var body = (StackPanel)card.Child!;
        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        top.Children.Add(Wrap("Dano e cura da sua party desde a última troca de mapa ou reset. Os mesmos números vão para o War Room.", Muted));
        var reset = Button("RESETAR");
        reset.Click += (_, _) => { _meter.ResetCombat(); RefreshDamage(); };
        top.Children.Add(reset);
        body.Children.Add(top);
        body.Children.Add(new TextBlock { Text = "#   Jogador                         Dano          Cura        K   M", Foreground = Gold, FontFamily = FontFamily.Parse("monospace"), Margin = new Thickness(0, 14, 0, 6) });
        body.Children.Add(new ListBox { ItemsSource = _damageRows, Background = Bg, Foreground = Text, MinHeight = 460, FontFamily = FontFamily.Parse("monospace") });
        return card;
    }

    private Control BuildPartyTab()
    {
        var card = Card("PARTY ATUAL");
        var body = (StackPanel)card.Child!;
        body.Children.Add(new ListBox { ItemsSource = _partyRows, Background = Bg, Foreground = Text, MinHeight = 480, FontSize = 15 });
        return card;
    }

    private Control BuildLogTab()
    {
        var card = Card("REGISTRO");
        var body = (StackPanel)card.Child!;
        var export = Button("EXPORTAR DIAGNÓSTICO (JSON, sem nomes)");
        export.Click += async (_, _) => await ExportDiagnosticsAsync();
        body.Children.Add(export);
        _diagStatus.TextWrapping = TextWrapping.Wrap;
        body.Children.Add(_diagStatus);
        body.Children.Add(new ListBox { ItemsSource = _logs, Background = Bg, Foreground = Text, MinHeight = 480, FontFamily = FontFamily.Parse("monospace") });
        return card;
    }

    private Control BuildSettingsTab()
    {
        var card = Card("CONFIGURAÇÕES");
        var body = (StackPanel)card.Child!;
        body.Children.Add(_autoCapture);
        body.Children.Add(_autostart);
        var save = PrimaryButton("SALVAR");
        save.Click += (_, _) =>
        {
            _settings.StartCaptureAutomatically = _autoCapture.IsChecked == true;
            _settings.StartWithDesktop = _autostart.IsChecked == true;
            _store.Save(_settings);
            AddLog("CONFIG  preferências salvas");
        };
        body.Children.Add(save);
        var updates = Card("ATUALIZAÇÕES");
        var ub = (StackPanel)updates.Child!;
        ub.Children.Add(Wrap("Baixa a versão nova do GitHub oficial, confere o SHA-256 e pede sua autorização pela janela do Ubuntu. Pareamentos e configurações são mantidos.", Muted));
        _updateStatus.TextWrapping = TextWrapping.Wrap;
        ub.Children.Add(_updateStatus);
        _updateButton.Click += async (_, _) => await OnUpdateClickAsync();
        ub.Children.Add(_updateButton);
        updates.Margin = new Thickness(0, 18, 0, 0);
        body.Children.Add(updates);
        body.Children.Add(Wrap("Pareamentos e fila ficam em ~/.local/share/IMORTAIS Combat Client (mesmo formato do Windows).", Muted));
        return card;
    }

    // ---- actions ------------------------------------------------------------------------

    private void SelectCurrentGuild()
    {
        var label = GuildIdentity.LabelForServer(ImortaisEventBridge.CurrentServerUrl);
        var index = GuildIdentity.KnownServers.ToList().FindIndex(s => s.Name == label);
        if (index >= 0) _guildCombo.SelectedIndex = index;
        else if (_guildCombo.ItemCount > 0) _guildCombo.SelectedIndex = 0;
    }

    private async Task PairAsync()
    {
        var code = (_pairCode.Text ?? string.Empty).Trim();
        if (code.Length != 6 || !code.All(char.IsDigit)) { SetPair("Informe o código de 6 dígitos.", Red); return; }
        var typed = (_customServer.Text ?? string.Empty).Trim();
        var serverUrl = GuildIdentity.ResolveServerUrl(typed.Length > 0 ? typed : _guildCombo.SelectedItem as string);
        if (serverUrl is null) { SetPair("Escolha a guilda ou informe o endereço https do War Room dela.", Red); return; }
        _pairButton.IsEnabled = false;
        SetPair("Ativando...", Gold);
        try
        {
            var name = _receiver.LocalName;
            var result = await ImortaisEventBridge.PairAsync(code, string.IsNullOrWhiteSpace(name) ? null : name, serverUrl);
            SetPair(result.Message, result.Success ? Green : Red);
            AddLog("PAREAR  " + result.Message);
            if (result.Success) { _pairCode.Text = string.Empty; _customServer.Text = string.Empty; }
        }
        finally { _pairButton.IsEnabled = true; RefreshAll(); }
    }

    private void SetPair(string text, IBrush color) { _pairStatus.Text = text; _pairStatus.Foreground = color; }

    private void StartCapture()
    {
        var result = _capture.Start();
        AddLog(result.Ok ? "CAPTURA " + result.Message : "CAPTURA ERRO · " + result.Message);
        RefreshHome();
    }

    private async Task ExportDiagnosticsAsync()
    {
        var json = _receiver.Diagnostics.Export();
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Exportar diagnóstico (sem nomes ou IDs)",
                SuggestedFileName = $"guild-combat-client-linux-{DateTime.Now:yyyyMMdd-HHmmss}.json",
                DefaultExtension = "json"
            });
            if (file is null) return;
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(json);
            _diagStatus.Text = "Diagnóstico exportado para: " + file.Path.LocalPath;
        }
        catch
        {
            // The Ubuntu file portal may refuse a process with capture permissions.
            try { _diagStatus.Text = "DIAGNÓSTICO SALVO: " + await LinuxDiagnosticExport.SaveFallbackAsync(json); _diagStatus.Foreground = Green; }
            catch (Exception ex) { _diagStatus.Text = "Não foi possível salvar: " + ex.Message; _diagStatus.Foreground = Red; }
        }
    }

    private async Task OnUpdateClickAsync()
    {
        if (_updateInstalled)
        {
            try { Process.Start(new ProcessStartInfo("/usr/bin/imortais-combat-client") { UseShellExecute = false }); Close(); }
            catch (Exception ex) { _updateStatus.Text = "Instalado, mas não foi possível reabrir: " + ex.Message; }
            return;
        }
        if (_availableUpdate is null) { await CheckUpdatesAsync(); return; }
        _updateButton.IsEnabled = false;
        string? package = null;
        try
        {
            _updateStatus.Text = "Baixando e conferindo SHA-256...";
            package = await _updateService.DownloadVerifiedAsync(_availableUpdate);
            _updateStatus.Text = "Pacote conferido. Autorize a instalação na janela do Ubuntu.";
            await LinuxUpdateService.InstallWithAuthorizationAsync(package);
            _updateStatus.Text = $"Versão {_availableUpdate.Version} instalada. Reabra o Combat Client.";
            _updateInstalled = true; _availableUpdate = null;
            _updateButton.Content = "REABRIR AGORA";
        }
        catch (Exception ex)
        {
            _updateStatus.Text = "Instalação não concluída: " + ex.Message + (package is null ? "" : "\nPacote conferido em: " + package);
            _updateButton.Content = "TENTAR NOVAMENTE";
        }
        finally { _updateButton.IsEnabled = true; }
    }

    private async Task CheckUpdatesAsync()
    {
        _updateButton.IsEnabled = false;
        _updateStatus.Text = "Verificando atualizações no GitHub...";
        try
        {
            _availableUpdate = await _updateService.CheckAsync();
            _updateStatus.Text = _availableUpdate is null ? $"Você está na versão mais recente (v{Version})." : $"Nova versão {_availableUpdate.Version} disponível.";
            _updateButton.Content = _availableUpdate is null ? "VERIFICAR NOVAMENTE" : "BAIXAR E INSTALAR";
        }
        catch (Exception ex) { _updateStatus.Text = "Não foi possível verificar: " + ex.Message; _updateButton.Content = "TENTAR NOVAMENTE"; }
        finally { _updateButton.IsEnabled = true; }
    }

    // ---- refresh ------------------------------------------------------------------------

    private void RefreshAll() { RefreshGuild(); RefreshHome(); RefreshParty(); RefreshDamage(); }

    private void RefreshGuild()
    {
        var link = ImortaisEventBridge.CurrentGuildLink;
        _guildLink.Text = link.Text;
        _guildLink.Foreground = link.State switch { GuildLinkState.Match => Green, GuildLinkState.Mismatch => Red, _ => Muted };
        var labels = ImortaisEventBridge.PairedGuildLabels;
        _pairings.Text = labels.Count == 0 ? "Nenhuma guilda pareada neste PC." : "Pareado com: " + string.Join(", ", labels);
        if (_pairStatus.Text == "Verificando estado do pareamento...")
            SetPair(ImortaisEventBridge.IsPaired ? "Este PC está ativado. Para outra guilda, escolha e informe outro código." : "Este PC ainda não está ativado.",
                ImortaisEventBridge.IsPaired ? Green : Gold);
    }

    private void RefreshHome()
    {
        var status = ImortaisEventBridge.GetStatus();
        var game = status.GameDetected == true;
        _albion.Text = game ? "● DETECTADO" : "● AGUARDANDO"; _albion.Foreground = game ? Green : Gold;
        var cap = _capture.Snapshot();
        _captureState.Text = _capture.IsRunning ? $"● ATIVA · {cap.OpenedDevices} interface(s)" : "● PARADA";
        _captureState.Foreground = _capture.IsRunning ? Green : Gold;
        _captureDetail.Text = $"{cap.FramesSeen:N0} pacotes · {cap.PhotonDatagrams:N0} Photon";
        _captureDetail.Foreground = cap.PhotonDatagrams > 0 ? Green : cap.FramesSeen > 0 ? Gold : Muted;
        _warRoom.Text = !ImortaisEventBridge.IsPaired ? "● NÃO ATIVADO"
            : status.WarRoomConnected ? "● CONECTADO · " + ImortaisEventBridge.CurrentGuildName
            : "● SEM CONTATO" + (string.IsNullOrWhiteSpace(status.LastError) ? "" : " · " + status.LastError);
        _warRoom.Foreground = !ImortaisEventBridge.IsPaired ? Gold : status.WarRoomConnected ? Green : Red;
        _character.Text = string.IsNullOrWhiteSpace(_receiver.LocalName) ? "-" : _receiver.LocalName;
        _gameGuild.Text = string.IsNullOrWhiteSpace(_receiver.LocalGuild) ? "-" : _receiver.LocalGuild;
        _cta.Text = string.IsNullOrWhiteSpace(status.CtaEventId) ? "Sem CTA ativo" : $"{status.CtaTime ?? "CTA"} · #{status.CtaEventId}";
        _map.Text = string.IsNullOrWhiteSpace(_receiver.ClusterName) ? "Mapa desconhecido" : _receiver.ClusterName;
        _partyCount.Text = _receiver.Party.Count.ToString();
        _nearby.Text = _receiver.NearbyPlayers.ToString();
        _queue.Text = status.PendingEvents.ToString();
    }

    private void RefreshParty()
    {
        var party = _receiver.Party;
        _partyRows.Clear();
        for (var i = 0; i < party.Count; i++) _partyRows.Add($"{i + 1:00}   {party[i]}");
        if (party.Count == 0) _partyRows.Add("Sem party (ou o jogo ainda não enviou a lista).");
    }

    private void RefreshDamage()
    {
        var rows = _meter.SnapshotPlayers();
        _damageRows.Clear();
        var i = 1;
        foreach (var r in rows.Take(100))
            _damageRows.Add($"{i++,2}  {r.Name,-28} {r.Damage,12:N0}  {r.Healing,12:N0}  {r.Kills,3} {r.Deaths,3}");
        if (rows.Count == 0) _damageRows.Add("Aguardando combate...");
    }

    private void ReportCaptureHealth()
    {
        var c = _capture.Snapshot();
        var (state, message) = !c.IsRunning ? ("stopped", "captura parada")
            : c.OpenedDevices == 0 ? ("no-interface", "ERRO · nenhuma interface de rede aberta")
            : c.PhotonDatagrams > 0 ? ("photon", $"OK · recebendo dados do Albion ({c.PhotonDatagrams:N0})")
            : c.FramesSeen > 0 ? ("network", "rede ativa, aguardando o Albion")
            : ("waiting", $"aguardando tráfego em {c.OpenedDevices} interface(s)");
        if (state == _lastCaptureHealth) return;
        _lastCaptureHealth = state;
        AddLog("DIAG    " + message);
    }

    private void AddLog(string message) => Dispatcher.UIThread.Post(() =>
    {
        _logs.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");
        while (_logs.Count > 500) _logs.RemoveAt(_logs.Count - 1);
    });

    // ---- ui helpers ---------------------------------------------------------------------

    private static Border Card(string title)
    {
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(new TextBlock { Text = title, Foreground = Gold, FontWeight = FontWeight.Bold, FontSize = 14 });
        return new Border { Background = Panel, BorderBrush = BorderColor, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(16), Child = body };
    }

    private static Control Line(string label, TextBlock value)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 3) };
        grid.Children.Add(new TextBlock { Text = label, Foreground = Muted });
        Grid.SetColumn(value, 1);
        grid.Children.Add(value);
        return grid;
    }

    private static Control Field(string label, Control input, double width)
    {
        var stack = new StackPanel { Spacing = 5, Margin = new Thickness(0, 0, 12, 12), Width = width };
        stack.Children.Add(new TextBlock { Text = label, Foreground = Muted });
        stack.Children.Add(input);
        return stack;
    }

    private static TextBlock Wrap(string text, IBrush color) => new() { Text = text, Foreground = color, TextWrapping = TextWrapping.Wrap };
    private static TextBox Input(string? watermark = null) => new() { Watermark = watermark, Background = Bg, Foreground = Text, BorderBrush = BorderColor, Padding = new Thickness(10) };
    private static Avalonia.Controls.Button Button(string text) => new() { Content = text, Background = Brush("#151C26"), Foreground = Text, BorderBrush = Brush("#33425A"), Padding = new Thickness(14, 9) };
    private static Avalonia.Controls.Button PrimaryButton(string text) { var b = Button(text); b.Background = Brush("#A92631"); return b; }
    private static TextBlock Value(string text, IBrush color) => new() { Text = text, Foreground = color, FontWeight = FontWeight.SemiBold };
    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));
}
