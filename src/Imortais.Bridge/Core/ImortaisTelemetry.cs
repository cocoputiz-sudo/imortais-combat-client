using Imortais.Bridge.Models;
using Imortais.Bridge.Services;
namespace Imortais.Bridge.Core;
public sealed class ImortaisTelemetry
{
    private readonly Outbox _outbox;
    public event Action<TelemetryEvent>? EventCaptured;
    public ImortaisTelemetry(Outbox outbox)=>_outbox=outbox;
    private async Task Emit(string type,string? player,object payload) { var ev=TelemetryEvent.Create(type,player,payload); await _outbox.AddAsync(ev); EventCaptured?.Invoke(ev); }
    public Task Heartbeat()=>Emit("heartbeat",null,new{});
    public Task PartySnapshot(IEnumerable<string> members)=>Emit("party_snapshot",null,new{members=members.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()});
    public Task PartyJoined(string player)=>Emit("party_member_joined",player,new{player});
    public Task PartyLeft(string player)=>Emit("party_member_left",player,new{player});
    public Task FightStart(string? map=null)=>Emit("fight_start",null,new{map});
    public Task FightEnd()=>Emit("fight_end",null,new{});
    public Task Damage(string player,long amount,string? ability=null,string? target=null)=>Emit("damage",player,new{player,amount,ability,target});
    public Task Healing(string player,long amount,string? ability=null,string? target=null)=>Emit("healing",player,new{player,amount,ability,target});
    public Task Death(string player,string? killer=null)=>Emit("death",player,new{player,killer});
    public Task Loot(string looter,string itemId,int quantity=1,string? sourceKind=null,string? sourceName=null,long? estimatedValue=null)=>
        Emit("loot",looter,new{player=looter,itemId,quantity,sourceKind,sourceName,estimatedValue});
}
