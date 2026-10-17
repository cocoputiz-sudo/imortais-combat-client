using System.Text.Json;
using Imortais.LinuxClient.Models;

namespace Imortais.LinuxClient.Services;

public sealed class Outbox
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;

    public Outbox()
    {
        Directory.CreateDirectory(LinuxPaths.StateDirectory);
        _path = Path.Combine(LinuxPaths.StateDirectory, "outbox.ndjson");
    }

    public async Task AddAsync(TelemetryEvent evt)
    {
        await _gate.WaitAsync();
        try
        {
            await File.AppendAllTextAsync(_path, JsonSerializer.Serialize(evt) + Environment.NewLine);
        }
        finally { _gate.Release(); }
    }

    public async Task<List<TelemetryEvent>> PeekAsync(int max = 200)
    {
        await _gate.WaitAsync();
        try
        {
            if (!File.Exists(_path)) return [];
            var result = new List<TelemetryEvent>();
            foreach (var line in File.ReadLines(_path).Where(x => !string.IsNullOrWhiteSpace(x)).Take(max))
            {
                try
                {
                    var evt = JsonSerializer.Deserialize<TelemetryEvent>(line);
                    if (evt is not null) result.Add(evt);
                }
                catch { }
            }
            return result;
        }
        finally { _gate.Release(); }
    }

    public async Task AckAsync(HashSet<string> ids)
    {
        await _gate.WaitAsync();
        try
        {
            if (!File.Exists(_path)) return;
            var keep = new List<string>();
            foreach (var line in File.ReadLines(_path))
            {
                try
                {
                    var evt = JsonSerializer.Deserialize<TelemetryEvent>(line);
                    if (evt is null || !ids.Contains(evt.EventId)) keep.Add(line);
                }
                catch { keep.Add(line); }
            }
            await File.WriteAllLinesAsync(_path, keep);
        }
        finally { _gate.Release(); }
    }

    public int Count
    {
        get
        {
            try { return File.Exists(_path) ? File.ReadLines(_path).Count(x => !string.IsNullOrWhiteSpace(x)) : 0; }
            catch { return 0; }
        }
    }
}
