using StatisticsAnalysisTool.Network;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace StatisticsAnalysisTool.Imortais;

/// <summary>
/// Coleta local de METADADOS dos eventos Photon. Não guarda valores, nomes,
/// GUIDs, IDs de entidades nem payloads. Nunca envia dados ao servidor.
/// </summary>
public sealed class PhotonEventDiagnosticsCollector
{
    private const int MaxCodes = 1024;
    private const int MaxParameters = 96;
    private readonly object _sync = new();
    private readonly Dictionary<short, EventCount> _events = new();
    private readonly DateTime _startedAtUtc = DateTime.UtcNow;
    private long _untrackedCodes;
    private long _totalEvents;

    private sealed class EventCount
    {
        public long Count;
        public DateTime FirstSeenAtUtc;
        public SortedDictionary<string, string> FirstSignature = new(StringComparer.Ordinal);
    }

    public void Observe(short code, Dictionary<byte, object> parameters)
    {
        if (parameters is null) return;
        lock (_sync)
        {
            _totalEvents++;
            if (_events.TryGetValue(code, out var existing))
            {
                existing.Count++;
                return;
            }
            if (_events.Count >= MaxCodes)
            {
                _untrackedCodes++;
                return;
            }

            // Somente a primeira assinatura de cada código; nenhum valor é copiado.
            var signature = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in parameters.OrderBy(x => x.Key).Take(MaxParameters))
                signature[entry.Key.ToString(CultureInfo.InvariantCulture)] = DescribeShape(entry.Value);
            _events.Add(code, new EventCount
            {
                Count = 1, FirstSeenAtUtc = DateTime.UtcNow,
                FirstSignature = signature
            });
        }
    }

    public long TotalEvents { get { lock (_sync) return _totalEvents; } }
    public int DistinctCodes { get { lock (_sync) return _events.Count; } }

    private sealed record ExpectedProfile(string Name, short Code, string Signature);
    private static readonly ExpectedProfile[] Expected =
    {
        new("PartyJoined", (short)EventCodes.PartyJoined,
            "chaves 4:array de GUIDs e 5:array de strings (ou campos 8/9 da versão anterior)"),
        new("PartyPlayerJoined", (short)EventCodes.PartyPlayerJoined,
            "chaves 0..7; 1:byte[16]; 2:string"),
        new("PartyPlayerLeft", (short)EventCodes.PartyPlayerLeft,
            "chaves 0 e 1; 1:byte[16]"),
        new("PartyDisbanded", (short)EventCodes.PartyDisbanded,
            "chave 1:número"),
        new("OtherGrabbedLoot", (short)EventCodes.OtherGrabbedLoot,
            "item: 0:número,1:string,2:string,4:número,5:número; prata: 0:número,2:string,3:boolean,5:número")
    };

    /// <summary>Exportação atômica sob lock; contagens e assinaturas, nunca dados de jogo.</summary>
    public string ExportJson()
    {
        lock (_sync)
        {
            var expected = Expected.Select(p =>
            {
                _events.TryGetValue(p.Code, out var entry);
                return new
                {
                    eventName = p.Name,
                    expectedEnumCode = p.Code,
                    expectedShape = p.Signature,
                    receivedCountAtExpectedCode = entry?.Count ?? 0,
                    firstSampleMatchesExpectedShape = entry is null
                        ? (bool?)null : Matches(p.Name, entry.FirstSignature)
                };
            }).ToArray();

            var observed = _events.OrderBy(x => x.Key).Select(entry => new
            {
                code = entry.Key,
                count = entry.Value.Count,
                firstSeenUtc = entry.Value.FirstSeenAtUtc,
                firstExample = new
                {
                    parameterKeys = entry.Value.FirstSignature.Keys.Select(int.Parse).ToArray(),
                    valueShapes = entry.Value.FirstSignature
                },
                expectedEnumNames = Expected.Where(x => x.Code == entry.Key).Select(x => x.Name).ToArray(),
                matchesProfiles = Expected.Where(x => Matches(x.Name, entry.Value.FirstSignature))
                    .Select(x => x.Name).ToArray()
            }).ToArray();

            return JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                purpose = "local Photon event-code diagnostics; metadata only; no player names or IDs",
                generatedAtUtc = DateTime.UtcNow,
                captureStartedUtc = _startedAtUtc,
                source = "AlbionParser.OnEvent before handler dispatch; Photon parameter 252",
                totalEvents = _totalEvents,
                distinctCodes = _events.Count,
                omittedDistinctCodes = _untrackedCodes,
                expected,
                observed
            }, new JsonSerializerOptions { WriteIndented = true });
        }
    }

    // Tipo e tamanho apenas. Não converter o conteúdo de string/byte[]/GUID em texto.
    public static string DescribeShape(object? value)
    {
        return value switch
        {
            null => "null",
            string text => $"string({text.Length})",
            byte[] bytes => $"byte[{bytes.Length}]",
            bool => "boolean",
            sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal => "number",
            Guid => "guid",
            Array array => DescribeArray(array),
            ICollection collection => $"array[{collection.Count}]",
            _ => "object"
        };
    }

    private static string DescribeArray(Array values)
    {
        var length = values.Length;
        if (length == 0) return "array[0]";
        // Amostra apenas os tipos, nunca os valores nem os conteúdos de arrays grandes.
        var elementTypes = new HashSet<string>(StringComparer.Ordinal);
        var limit = Math.Min(length, 3);
        for (var i = 0; i < limit; i++)
        {
            var element = values.GetValue(i);
            elementTypes.Add(element switch
            {
                byte[] bytes => $"byte[{bytes.Length}]",
                string => "string",
                bool => "boolean",
                sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal => "number",
                Guid => "guid",
                Array => "array",
                null => "null",
                _ => "object"
            });
        }
        return $"array[{length}]<{string.Join("|", elementTypes.OrderBy(x => x, StringComparer.Ordinal))}>";
    }

    private static bool Contains(IDictionary<string, string> s, int key, Func<string, bool> test)
        => s.TryGetValue(key.ToString(CultureInfo.InvariantCulture), out var v) && test(v);

    private static bool Number(string s) => s == "number";
    private static bool String(string s) => s.StartsWith("string(", StringComparison.Ordinal);
    private static bool GuidBytes(string s) => s == "byte[16]" || s == "guid"
        || s == "array[16]<number>";
    private static bool GuidArray(string s) => s.StartsWith("array[", StringComparison.Ordinal)
        && (s.EndsWith("<byte[16]>", StringComparison.Ordinal) || s.EndsWith("<guid>", StringComparison.Ordinal)
            || s.EndsWith("<array>", StringComparison.Ordinal));
    private static bool StringArray(string s) => s.StartsWith("array[", StringComparison.Ordinal)
        && s.EndsWith("<string>", StringComparison.Ordinal);

    public static bool Matches(string profileName, IDictionary<string, string> shape)
    {
        bool Has(int key, Func<string, bool> test) => Contains(shape, key, test);
        return profileName switch
        {
            "PartyJoined" =>
                (Has(4, GuidArray) && Has(5, StringArray))
                || (Has(8, GuidArray) && Has(9, StringArray)),
            "PartyPlayerJoined" => Enumerable.Range(0, 8).All(x => shape.ContainsKey(x.ToString()))
                && Has(0, Number) && Has(1, GuidBytes) && Has(2, String),
            "PartyPlayerLeft" => Has(0, Number) && Has(1, GuidBytes),
            "PartyDisbanded" => Has(1, Number) && !shape.ContainsKey("2"),
            "OtherGrabbedLoot" =>
                (Has(0, Number) && Has(1, String) && Has(2, String)
                    && Has(4, Number) && Has(5, Number))
                || (Has(0, Number) && Has(2, String)
                    && Has(3, x => x == "boolean") && Has(5, Number)),
            _ => false
        };
    }
}

/// <summary>Instância única ligada à recepção Photon e à exportação pela interface.</summary>
public static class ImortaisPhotonEventDiagnostics
{
    private static readonly PhotonEventDiagnosticsCollector Collector = new();

    public static void Observe(short code, Dictionary<byte, object> parameters)
        => Collector.Observe(code, parameters);

    public static string ExportJson() => Collector.ExportJson();
    public static long TotalEvents => Collector.TotalEvents;
    public static int DistinctCodes => Collector.DistinctCodes;
}
