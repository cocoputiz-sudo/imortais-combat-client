#nullable enable
using System;
using System.Buffers.Binary;

namespace StatisticsAnalysisTool.Network.PacketProviders;

/// <summary>
/// Reads the network layer (IPv4/IPv6) out of a captured frame according to the
/// adapter's link type. Before v0.7.4 every frame was parsed as Ethernet, so
/// adapters that deliver IP without an Ethernet header (WireGuard/Wintun,
/// Cloudflare WARP, NordLynx and the Npcap loopback adapter) were silently
/// dropped as malformed even when the game traffic was there in clear text.
/// Pure function: no Npcap dependency, testable on any OS.
/// </summary>
internal static class CaptureLinkLayer
{
    // Values as defined by libpcap (pcap/dlt.h).
    public const int DltNull = 0;
    public const int DltEthernet = 1;
    public const int DltRaw = 12;
    public const int DltRawOpenBsd = 14;
    public const int DltLoop = 108;
    public const int DltLinuxSll = 113;
    public const int DltIPv4 = 228;
    public const int DltIPv6 = 229;
    public const int DltLinuxSll2 = 276;

    public const ushort EtherTypeIPv4 = 0x0800;
    public const ushort EtherTypeIPv6 = 0x86DD;
    private const ushort EtherTypeVlan = 0x8100;
    private const ushort EtherTypeQinQ = 0x88A8;
    private const int EthernetHeaderLength = 14;
    private const int VlanTagLength = 4;
    private const int MaxVlanTags = 2;

    public static bool IsSupported(int linkType) => linkType is
        DltNull or DltEthernet or DltRaw or DltRawOpenBsd or DltLoop
        or DltLinuxSll or DltIPv4 or DltIPv6 or DltLinuxSll2;

    public static string Describe(int linkType) => linkType switch
    {
        DltEthernet => "Ethernet",
        DltRaw or DltRawOpenBsd or DltIPv4 or DltIPv6 => "IP sem Ethernet",
        DltNull or DltLoop => "loopback",
        DltLinuxSll or DltLinuxSll2 => "Linux cooked",
        _ => $"tipo {linkType} não suportado"
    };

    /// <summary>
    /// Returns false for frames that are not IPv4/IPv6 or are too short.
    /// <paramref name="etherType"/> is normalised to 0x0800 / 0x86DD.
    /// </summary>
    public static bool TryGetNetworkLayer(int linkType, ReadOnlySpan<byte> frame, out ushort etherType, out ReadOnlySpan<byte> l3)
    {
        etherType = 0;
        l3 = default;

        switch (linkType)
        {
            case DltEthernet:
                return TryReadEthernet(frame, out etherType, out l3);

            case DltRaw:
            case DltRawOpenBsd:
            case DltIPv4:
            case DltIPv6:
                return TryReadByIpVersion(frame, out etherType, out l3);

            case DltNull:
            case DltLoop:
                // 4-byte address family header (host order for NULL, network
                // order for LOOP; AF_INET6 differs per OS). The IP version
                // nibble is unambiguous, so use it instead of the family.
                return frame.Length > 4 && TryReadByIpVersion(frame[4..], out etherType, out l3);

            case DltLinuxSll:
                if (frame.Length < 16) return false;
                return TryAcceptEtherType(BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(14, 2)), frame[16..], out etherType, out l3);

            case DltLinuxSll2:
                if (frame.Length < 20) return false;
                return TryAcceptEtherType(BinaryPrimitives.ReadUInt16BigEndian(frame[..2]), frame[20..], out etherType, out l3);

            default:
                return false;
        }
    }

    private static bool TryReadEthernet(ReadOnlySpan<byte> frame, out ushort etherType, out ReadOnlySpan<byte> l3)
    {
        etherType = 0;
        l3 = default;
        if (frame.Length < EthernetHeaderLength) return false;

        var offset = 12;
        var type = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(offset, 2));
        for (var tags = 0; (type == EtherTypeVlan || type == EtherTypeQinQ) && tags < MaxVlanTags; tags++)
        {
            offset += VlanTagLength;
            if (frame.Length < offset + 2) return false;
            type = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(offset, 2));
        }

        return TryAcceptEtherType(type, frame[(offset + 2)..], out etherType, out l3);
    }

    private static bool TryReadByIpVersion(ReadOnlySpan<byte> packet, out ushort etherType, out ReadOnlySpan<byte> l3)
    {
        etherType = 0;
        l3 = default;
        if (packet.IsEmpty) return false;

        switch (packet[0] >> 4)
        {
            case 4:
                etherType = EtherTypeIPv4;
                l3 = packet;
                return true;
            case 6:
                etherType = EtherTypeIPv6;
                l3 = packet;
                return true;
            default:
                return false;
        }
    }

    private static bool TryAcceptEtherType(ushort type, ReadOnlySpan<byte> payload, out ushort etherType, out ReadOnlySpan<byte> l3)
    {
        etherType = 0;
        l3 = default;
        if (type is not (EtherTypeIPv4 or EtherTypeIPv6) || payload.IsEmpty) return false;
        etherType = type;
        l3 = payload;
        return true;
    }
}

/// <summary>
/// Strict structural check of a Photon datagram, used only for traffic seen on
/// the loopback adapter (where ports cannot identify the game because a local
/// accelerator may relay it). Every command header must be consistent and the
/// commands must fill the datagram exactly; random local UDP almost never does.
/// Encrypted Photon (flags = 1) is rejected: it cannot be verified.
/// </summary>
internal static class PhotonDatagramSignature
{
    private const int HeaderLength = 12;
    private const int CrcLength = 4;
    private const int CommandHeaderLength = 12;
    private const int MaxCommands = 64;
    private const byte MaxCommandType = 12;

    public static bool IsWellFormed(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < HeaderLength + CommandHeaderLength) return false;

        var flags = payload[2];
        if (flags is not (0x00 or 0xCC)) return false;

        int commandCount = payload[3];
        if (commandCount is 0 or > MaxCommands) return false;

        var offset = HeaderLength + (flags == 0xCC ? CrcLength : 0);
        for (var i = 0; i < commandCount; i++)
        {
            if (offset > payload.Length - CommandHeaderLength) return false;

            var commandType = payload[offset];
            if (commandType is 0 or > MaxCommandType) return false;

            var commandLength = BinaryPrimitives.ReadInt32BigEndian(payload.Slice(offset + 4, 4));
            if (commandLength < CommandHeaderLength || commandLength > payload.Length - offset) return false;

            offset += commandLength;
        }

        return offset == payload.Length;
    }
}
