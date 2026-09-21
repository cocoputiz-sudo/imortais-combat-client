using System.Windows;
using Imortais.Bridge.Core;
using Imortais.Bridge.Models;
using Imortais.Bridge.Services;
namespace Imortais.Bridge;
public partial class MainWindow : Window
{
    readonly SettingsStore _store=new(); readonly Outbox _outbox=new(); readonly TelemetrySender _sender; readonly ImortaisTelemetry _telemetry; BridgeSettings _s;
    int _party,_loot,_damage,_deaths;
    public MainWindow(){ InitializeComponent(); _sender=new(_outbox); _telemetry=new(_outbox); _telemetry.EventCaptured += OnEvent; _s=_store.Load(); LoadUi(); var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(5)}; timer.Tick+=async(_,__)=>await Flush(false); timer.Start(); }
    void LoadUi(){UrlBox.Text=_s.RailwayBaseUrl;KeyBox.Text=_s.ApiKey;PlayerBox.Text=_s.PlayerName;DeviceBox.Text=_s.DeviceId;CtaBox.Text=_s.CtaEventId?.ToString()??"";RefreshCounters();}
    void PullUi(){_s.RailwayBaseUrl=UrlBox.Text.Trim();_s.ApiKey=KeyBox.Text.Trim();_s.PlayerName=PlayerBox.Text.Trim();_s.DeviceId=DeviceBox.Text.Trim();_s.CtaEventId=long.TryParse(CtaBox.Text,out var x)?x:null;}
    void Save_Click(object s,RoutedEventArgs e){PullUi();_store.Save(_s);StatusText.Text="● configuração salva";StatusText.Foreground=System.Windows.Media.Brushes.LightGreen;}
    async void Test_Click(object s,RoutedEventArgs e){PullUi();await _telemetry.Heartbeat();await Flush(true);}
    async void Demo_Click(object s,RoutedEventArgs e){PullUi();await _telemetry.PartySnapshot([_s.PlayerName,"Ragnaldokhun","Mitrius","Isahel","Tarzan05"]);await _telemetry.FightStart("Demo");await _telemetry.Damage(_s.PlayerName,38420,"demo");await _telemetry.Healing("Isahel",21450,"demo");await _telemetry.Death("Mitrius");await _telemetry.Loot("NegaumBlack","T8_2H_DUALSWORD",1,"player_corpse","DemoVictim",1240000);await _telemetry.FightEnd();await Flush(true);}
    async Task Flush(bool show){var r=await _sender.FlushAsync(_s);if(show||!r.Ok){StatusText.Text=(r.Ok?"● ":"● ERRO: ")+r.Message;StatusText.Foreground=r.Ok?System.Windows.Media.Brushes.LightGreen:System.Windows.Media.Brushes.IndianRed;}RefreshCounters();}
    void OnEvent(TelemetryEvent ev){Dispatcher.Invoke(()=>{EventsList.Items.Insert(0,$"{ev.OccurredAt.ToLocalTime():HH:mm:ss}  {ev.Type,-22} {ev.PlayerName}");while(EventsList.Items.Count>150)EventsList.Items.RemoveAt(EventsList.Items.Count-1);if(ev.Type=="party_snapshot")_party++;if(ev.Type=="loot")_loot++;if(ev.Type=="damage")_damage++;if(ev.Type=="death")_deaths++;RefreshCounters();});}
    void RefreshCounters(){QueueText.Text=$"Fila: {_outbox.Count}";PartyText.Text=$"Party snapshots: {_party}";LootText.Text=$"Loot observado: {_loot}";DamageText.Text=$"Damage events: {_damage}";DeathText.Text=$"Mortes: {_deaths}";}
}
