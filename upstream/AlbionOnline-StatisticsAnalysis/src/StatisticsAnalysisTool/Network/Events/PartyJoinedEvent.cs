using StatisticsAnalysisTool.Common;
using StatisticsAnalysisTool.Diagnostics;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;

namespace StatisticsAnalysisTool.Network.Events;

public sealed class PartyJoinedEvent
{
    private const int GuidByteLength = 16;

    public Dictionary<Guid, string> PartyUsers { get; } = new();

    // Um evento de party cuja lista não foi decodificada NÃO pode limpar
    // a composição anterior. PartyDisbanded/PartyPlayerLeft fazem a saída real.
    public bool HasCompleteRoster { get; private set; }
    // Somente formato e tamanho dos campos Photon, sem nomes/IDs dos jogadores.
    public string DecodeDiagnostics { get; private set; } = "not-decoded";

    public PartyJoinedEvent(Dictionary<byte, object> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        DecodeDiagnostics = string.Join(", ", parameters.OrderBy(x => x.Key).Take(32)
            .Select(x => $"{x.Key}:{DescribeParameter(x.Value)}"));

        try
        {
            // Há dois layouts documentados no histórico do projeto.
            // Tentar um par só se ambos os campos existirem e o roster fechar.
            if (parameters.TryGetValue(8, out var guids89)
                && parameters.TryGetValue(9, out var names89))
            {
                AddPartyUsers(guids89, names89, "8/9");
                if (HasCompleteRoster) return;
            }

            if (parameters.TryGetValue(4, out var guids45)
                && parameters.TryGetValue(5, out var names45))
            {
                AddPartyUsers(guids45, names45, "4/5");
            }
        }
        catch (Exception exception)
        {
            DebugConsole.WriteError(
                MethodBase.GetCurrentMethod()?.DeclaringType,
                exception);
        }
    }

    private void AddPartyUsers(object partyUserGuidsParameter, object partyUserNamesParameter, string layout)
    {
        PartyUsers.Clear();
        HasCompleteRoster = false;
        var partyUserGuids = GetPartyUserGuids(partyUserGuidsParameter);
        var partyUserNames = GetPartyUserNames(partyUserNamesParameter);
        var partyUserCount = partyUserGuids.Count;
        DecodeDiagnostics += $"; layout={layout}, parsedGuids={partyUserGuids.Count}, parsedNames={partyUserNames.Count}";

        // Listas parciais ou não reconhecidas não são um estado autoritativo.
        // A própria entrada na party nunca deve apagar uma PT por falha de parser.
        if (partyUserCount == 0 || partyUserCount != partyUserNames.Count
            || partyUserGuids.Any(x => x == Guid.Empty)
            || partyUserNames.Any(string.IsNullOrWhiteSpace))
        {
            return;
        }

        for (var index = 0; index < partyUserCount; index++)
        {
            var partyUserGuid = partyUserGuids[index];
            var partyUserName = partyUserNames[index];

            if (partyUserGuid == Guid.Empty)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(partyUserName))
            {
                continue;
            }

            PartyUsers[partyUserGuid] = partyUserName;
        }

        HasCompleteRoster = PartyUsers.Count == partyUserCount;
    }

    private static string DescribeParameter(object value) => value switch
    {
        byte[] bytes => $"bytes({bytes.Length})",
        string[] names => $"strings({names.Length})",
        object[] objects => $"objects({objects.Length})",
        _ => value?.GetType().Name ?? "null"
    };

    private static List<Guid> GetPartyUserGuids(object parameter)
    {
        return parameter switch
        {
            byte[] guidBytes => GetPartyUserGuidsFromBytes(guidBytes),
            object[] guidObjects => GetPartyUserGuidsFromObjects(guidObjects),
            _ => []
        };
    }

    private static List<Guid> GetPartyUserGuidsFromBytes(byte[] guidBytes)
    {
        if (guidBytes.Length == 0 || guidBytes.Length % GuidByteLength != 0)
        {
            return [];
        }

        var partyUserCount = guidBytes.Length / GuidByteLength;
        var partyUserGuids = new List<Guid>(partyUserCount);

        for (var index = 0; index < partyUserCount; index++)
        {
            var guidByteOffset = index * GuidByteLength;
            var partyUserGuid = new Guid(
                guidBytes.AsSpan(guidByteOffset, GuidByteLength));

            partyUserGuids.Add(partyUserGuid);
        }

        return partyUserGuids;
    }

    private static List<Guid> GetPartyUserGuidsFromObjects(object[] guidObjects)
    {
        var partyUserGuids = new List<Guid>(guidObjects.Length);

        foreach (var guidObject in guidObjects)
        {
            Guid? partyUserGuid;
            if (guidObject is Guid directGuid)
                partyUserGuid = directGuid;
            else if (guidObject is string text && Guid.TryParse(text, out var parsedGuid))
                partyUserGuid = parsedGuid;
            else
                partyUserGuid = guidObject?.ObjectToGuid();

            // Não desloque índices dos nomes quando um GUID for inválido.
            partyUserGuids.Add(partyUserGuid ?? Guid.Empty);
        }

        return partyUserGuids;
    }

    private static List<string> GetPartyUserNames(object parameter)
    {
        if (parameter is string[] partyUserNames)
        {
            return [.. partyUserNames];
        }

        if (parameter is not object[] partyUserNameObjects)
        {
            return [];
        }

        var partyUserNamesFromObjects =
            new List<string>(partyUserNameObjects.Length);

        foreach (var partyUserNameObject in partyUserNameObjects)
        {
            partyUserNamesFromObjects.Add(
                partyUserNameObject?.ToString() ?? string.Empty);
        }

        return partyUserNamesFromObjects;
    }
}