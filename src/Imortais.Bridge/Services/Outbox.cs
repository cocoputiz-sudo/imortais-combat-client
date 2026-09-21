using System.IO;
using System.Text.Json;
using Imortais.Bridge.Models;
namespace Imortais.Bridge.Services;
public sealed class Outbox
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1,1);
    public Outbox()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IMORTAIS", "CombatClient");
        Directory.CreateDirectory(dir); _path = Path.Combine(dir, "outbox.ndjson");
    }
    public async Task AddAsync(TelemetryEvent ev)
    {
        await _gate.WaitAsync(); try { await File.AppendAllTextAsync(_path, JsonSerializer.Serialize(ev) + Environment.NewLine); } finally { _gate.Release(); }
    }
    public async Task<List<TelemetryEvent>> PeekAsync(int max=100)
    {
        await _gate.WaitAsync(); try {
            if (!File.Exists(_path)) return [];
            var list = new List<TelemetryEvent>();
            foreach (var line in File.ReadLines(_path).Where(x=>!string.IsNullOrWhiteSpace(x)).Take(max))
                try { var ev=JsonSerializer.Deserialize<TelemetryEvent>(line); if(ev!=null) list.Add(ev); } catch { }
            return list;
        } finally { _gate.Release(); }
    }
    public async Task AckAsync(HashSet<string> ids)
    {
        await _gate.WaitAsync(); try {
            if (!File.Exists(_path)) return;
            var keep = new List<string>();
            foreach(var line in File.ReadLines(_path)) {
                try { var ev=JsonSerializer.Deserialize<TelemetryEvent>(line); if(ev==null || !ids.Contains(ev.EventId)) keep.Add(line); }
                catch { keep.Add(line); }
            }
            await File.WriteAllLinesAsync(_path, keep);
        } finally { _gate.Release(); }
    }
    public int Count { get { try { return File.Exists(_path) ? File.ReadLines(_path).Count(x=>!string.IsNullOrWhiteSpace(x)) : 0; } catch { return 0; } } }
}

