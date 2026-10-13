using System.Collections;
using System.Globalization;
using System.Text.Json;

namespace Imortais.LinuxClient.Network;

/// <summary>Anonymous local-only Photon event signature sampling. Never copy parameter values.</summary>
public sealed class LinuxPhotonDiagnostics
{
    private sealed class Sample
    {
        public long Count;
        public DateTime FirstSeenUtc;
        public SortedDictionary<string, string> Signature = new(StringComparer.Ordinal);
    }

    private readonly object _gate = new();
    private readonly SortedDictionary<int, Sample> _observed = new();
    private readonly DateTime _captureStartedUtc = DateTime.UtcNow;
    private long _total;

    public void Observe(int code, Dictionary<byte, object> parameters)
    {
        if (code < 0 || parameters is null) return;
        lock (_gate)
        {
            _total++;
            if (_observed.TryGetValue(code, out var found))
            {
                found.Count++;
                return;
            }
            if (_observed.Count >= 1024) return;
            var shape = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var p in parameters.OrderBy(x => x.Key).Take(96))
                shape[p.Key.ToString(CultureInfo.InvariantCulture)] = Shape(p.Value);
            _observed[code] = new Sample
            {
                Count = 1,
                FirstSeenUtc = DateTime.UtcNow,
                Signature = shape
            };
        }
    }

    public string Export()
    {
        lock (_gate)
        {
            return JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                platform = "linux",
                metadataOnly = true,
                generatedAtUtc = DateTime.UtcNow,
                captureStartedUtc = _captureStartedUtc,
                totalEvents = _total,
                distinctCodes = _observed.Count,
                expected = new[]
                {
                    new { name = "PartyJoined", code = LinuxPhotonEventCodes.PartyJoined },
                    new { name = "PartyDisbanded", code = LinuxPhotonEventCodes.PartyDisbanded },
                    new { name = "PartyPlayerJoined", code = LinuxPhotonEventCodes.PartyPlayerJoined },
                    new { name = "PartyPlayerLeft", code = LinuxPhotonEventCodes.PartyPlayerLeft },
                    new { name = "OtherGrabbedLoot", code = LinuxPhotonEventCodes.OtherGrabbedLoot },
                    new { name = "KilledPlayer", code = LinuxPhotonEventCodes.KilledPlayer },
                    new { name = "Died", code = LinuxPhotonEventCodes.Died }
                },
                observed = _observed.Select(x => new
                {
                    code = x.Key,
                    count = x.Value.Count,
                    firstSeenUtc = x.Value.FirstSeenUtc,
                    valueShapes = x.Value.Signature
                }).ToArray()
            }, new JsonSerializerOptions { WriteIndented = true });
        }
    }

    private static string Shape(object? value)
    {
        if (value is null) return "null";
        if (value is byte[] b) return $"byte[{b.Length}]";
        if (value is string s) return $"string({s.Length})";
        if (value is bool) return "boolean";
        if (value is Guid) return "guid";
        if (value is Array array)
        {
            if (array.Length == 0) return "array[0]";
            var first = array.GetValue(0);
            var type = first switch
            {
                byte[] bytes => $"byte[{bytes.Length}]",
                string => "string",
                bool => "boolean",
                Guid => "guid",
                null => "null",
                Array => "array",
                _ => "number-or-object"
            };
            return $"array[{array.Length}]<{type}>";
        }
        if (value is ICollection collection) return $"array[{collection.Count}]";
        return value is IConvertible ? "number" : "object";
    }
}
