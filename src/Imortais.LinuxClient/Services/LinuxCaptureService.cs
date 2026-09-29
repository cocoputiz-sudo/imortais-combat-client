using System.Buffers.Binary;
using System.Net.Sockets;
using Imortais.LinuxClient.Network;
using Libpcap;

namespace Imortais.LinuxClient.Services;

public sealed class LinuxCaptureService : IDisposable
{
    public const string PacketFilter =
        "((ip and ((udp and (port 5055 or port 5056 or port 5058)) or (ip[6:2] & 0x3fff != 0))) or (ip6 and (udp and (port 5055 or port 5056 or port 5058))))";

    private const int MaxIpv4PayloadLength = 65535;
    private const int MaxPendingIpv4Fragments = 256;
    private static readonly TimeSpan Ipv4FragmentTimeout = TimeSpan.FromSeconds(15);

    private readonly LinuxPhotonReceiver _receiver;
    private readonly object _gate = new();
    private readonly object _fragmentGate = new();
    private readonly Dictionary<Ipv4FragmentKey, Ipv4FragmentBuffer> _ipv4Fragments = new();
    private readonly List<string> _openedDeviceNames = [];

    private PcapDispatcher? _dispatcher;
    private CancellationTokenSource? _cts;
    private Thread? _thread;

    private long _framesSeen;
    private long _photonDatagrams;
    private long _ipv4FragmentPackets;
    private long _ipv4Reassemblies;
    private long _malformedFrames;

    public event Action<string>? StatusChanged;
    public bool IsRunning => _thread?.IsAlive == true;
    public int OpenedDeviceCount
    {
        get
        {
            lock (_gate) return _openedDeviceNames.Count;
        }
    }

    public LinuxCaptureService(LinuxPhotonReceiver receiver) => _receiver = receiver;

    public CaptureDiagnosticsSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new CaptureDiagnosticsSnapshot(
                IsRunning,
                _openedDeviceNames.Count,
                [.. _openedDeviceNames],
                Interlocked.Read(ref _framesSeen),
                Interlocked.Read(ref _photonDatagrams),
                Interlocked.Read(ref _ipv4FragmentPackets),
                Interlocked.Read(ref _ipv4Reassemblies),
                Interlocked.Read(ref _malformedFrames));
        }
    }

    public (bool Ok, string Message, int Devices) Start()
    {
        lock (_gate)
        {
            if (IsRunning)
            {
                var alreadyOpened = _openedDeviceNames.Count;
                return (true, $"Captura já ativa em {alreadyOpened} interface(s)", alreadyOpened);
            }

            PcapDispatcher? dispatcher = null;
            try
            {
                ResetCounters();
                _openedDeviceNames.Clear();
                ClearIpv4Fragments();

                var devices = Pcap.ListDevices();
                if (devices.Count == 0)
                    return (false, "libpcap não encontrou interfaces de rede.", 0);

                dispatcher = new PcapDispatcher(Dispatch);

                int OpenPass(bool requireUpFlag)
                {
                    var openedInPass = 0;

                    foreach (var device in devices)
                    {
                        if (device.Flags.HasFlag(PcapDeviceFlags.Loopback)) continue;
                        if (requireUpFlag && !device.Flags.HasFlag(PcapDeviceFlags.Up)) continue;
                        if (_openedDeviceNames.Contains(device.Name, StringComparer.OrdinalIgnoreCase)) continue;

                        try
                        {
                            dispatcher.OpenDevice(device, pcap => pcap.NonBlocking = true);
                            _openedDeviceNames.Add(device.Name);
                            openedInPass++;
                        }
                        catch (Exception ex)
                        {
                            StatusChanged?.Invoke($"Interface ignorada: {device.Name} · {ex.Message}");
                        }
                    }

                    return openedInPass;
                }

                var opened = OpenPass(requireUpFlag: true);

                // Alguns drivers/libpcap no Linux não expõem a flag UP de forma confiável.
                // Se a primeira tentativa não abriu nada, tentamos qualquer interface não-loopback.
                if (opened == 0)
                    opened = OpenPass(requireUpFlag: false);

                if (opened == 0)
                {
                    dispatcher.Dispose();
                    return (false,
                        "Nenhuma interface de rede pôde ser aberta. A instalação do pacote deve conceder automaticamente as permissões de captura.",
                        0);
                }

                dispatcher.Filter = PacketFilter;

                _dispatcher = dispatcher;
                _cts = new CancellationTokenSource();
                _thread = new Thread(Worker)
                {
                    IsBackground = true,
                    Name = "IMORTAIS-LinuxCapture"
                };
                _thread.Start();

                var deviceText = string.Join(", ", _openedDeviceNames);
                StatusChanged?.Invoke($"Captura ativa em {opened} interface(s): {deviceText}");
                return (true, $"Captura iniciada em {opened} interface(s)", opened);
            }
            catch (Exception ex)
            {
                dispatcher?.Dispose();
                _dispatcher?.Dispose();
                _dispatcher = null;
                _openedDeviceNames.Clear();
                return (false, "Falha ao iniciar captura: " + ex.Message, 0);
            }
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _cts?.Cancel();
            _dispatcher?.Dispose();
            _dispatcher = null;
        }

        if (_thread?.IsAlive == true) _thread.Join(TimeSpan.FromSeconds(2));
        _thread = null;
        _cts?.Dispose();
        _cts = null;
        ClearIpv4Fragments();
        StatusChanged?.Invoke("Captura parada");
    }

    private void Worker()
    {
        var errors = 0;
        while (_cts is { IsCancellationRequested: false })
        {
            try
            {
                var dispatcher = _dispatcher;
                if (dispatcher is null) break;

                var count = dispatcher.Dispatch(50);
                errors = 0;
                if (count <= 0) _cts.Token.WaitHandle.WaitOne(20);
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                errors++;
                if (errors == 1 || errors % 20 == 0)
                    StatusChanged?.Invoke("Erro libpcap: " + ex.Message);
                _cts?.Token.WaitHandle.WaitOne(100);
            }
        }
    }

    private void Dispatch(Pcap pcap, ref Packet packet)
    {
        Interlocked.Increment(ref _framesSeen);

        try
        {
            if (packet.CapturedLength < packet.DeclaredLength)
            {
                Interlocked.Increment(ref _malformedFrames);
                return;
            }

            var frame = packet.Data;
            if (!TryGetIpPayload(frame, out var etherType, out var ip))
            {
                Interlocked.Increment(ref _malformedFrames);
                return;
            }

            if (etherType == 0x0800) HandleIpv4(ip);
            else if (etherType == 0x86DD) HandleIpv6(ip);
        }
        catch
        {
            Interlocked.Increment(ref _malformedFrames);
        }
    }

    private static bool TryGetIpPayload(ReadOnlySpan<byte> frame, out ushort etherType, out ReadOnlySpan<byte> ip)
    {
        etherType = 0;
        ip = default;

        // Raw IP.
        if (frame.Length >= 20)
        {
            var version = frame[0] >> 4;
            if (version == 4)
            {
                etherType = 0x0800;
                ip = frame;
                return true;
            }

            if (version == 6 && frame.Length >= 40)
            {
                etherType = 0x86DD;
                ip = frame;
                return true;
            }
        }

        // Ethernet II.
        if (frame.Length >= 14)
        {
            var type = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(12, 2));
            if (type is 0x0800 or 0x86DD)
            {
                etherType = type;
                ip = frame.Slice(14);
                return true;
            }

            // 802.1Q / QinQ com um cabeçalho VLAN.
            if (type is 0x8100 or 0x88A8 && frame.Length >= 18)
            {
                var innerType = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(16, 2));
                if (innerType is 0x0800 or 0x86DD)
                {
                    etherType = innerType;
                    ip = frame.Slice(18);
                    return true;
                }
            }
        }

        // Linux cooked capture v1 (SLL): protocol em 14..15, header de 16 bytes.
        if (frame.Length >= 16)
        {
            var sllType = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(14, 2));
            if (sllType is 0x0800 or 0x86DD)
            {
                etherType = sllType;
                ip = frame.Slice(16);
                return true;
            }
        }

        // Linux cooked capture v2 (SLL2): protocol em 0..1, header de 20 bytes.
        if (frame.Length >= 20)
        {
            var sll2Type = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(0, 2));
            if (sll2Type is 0x0800 or 0x86DD)
            {
                etherType = sll2Type;
                ip = frame.Slice(20);
                return true;
            }
        }

        return false;
    }

    private void HandleIpv4(ReadOnlySpan<byte> ip)
    {
        if (!TryReadIpv4Header(ip, out var header))
        {
            Interlocked.Increment(ref _malformedFrames);
            return;
        }

        if (header.IsFragmented)
        {
            Interlocked.Increment(ref _ipv4FragmentPackets);

            var fragmentPayload = ip.Slice(header.HeaderLength, header.PayloadLength);
            var source = BinaryPrimitives.ReadUInt32BigEndian(ip.Slice(12, 4));
            var destination = BinaryPrimitives.ReadUInt32BigEndian(ip.Slice(16, 4));

            var key = new Ipv4FragmentKey(source, destination, header.Identification, header.Protocol);
            var reassembled = AddIpv4Fragment(key, header, fragmentPayload);
            if (reassembled is null) return;

            Interlocked.Increment(ref _ipv4Reassemblies);
            if ((ProtocolType)header.Protocol == ProtocolType.Udp)
                HandleUdp(reassembled);

            return;
        }

        if ((ProtocolType)header.Protocol != ProtocolType.Udp) return;
        HandleUdp(ip.Slice(header.HeaderLength, header.PayloadLength));
    }

    private static bool TryReadIpv4Header(ReadOnlySpan<byte> ip, out Ipv4Header header)
    {
        header = default;

        if (ip.Length < 20 || (ip[0] >> 4) != 4) return false;

        var headerLength = (ip[0] & 0x0F) * 4;
        if (headerLength < 20 || ip.Length < headerLength) return false;

        var totalLength = BinaryPrimitives.ReadUInt16BigEndian(ip.Slice(2, 2));
        if (totalLength < headerLength || totalLength > ip.Length) return false;

        var flagsOffset = BinaryPrimitives.ReadUInt16BigEndian(ip.Slice(6, 2));
        var moreFragments = (flagsOffset & 0x2000) != 0;
        var fragmentOffset = (flagsOffset & 0x1FFF) * 8;

        header = new Ipv4Header(
            ip[9],
            BinaryPrimitives.ReadUInt16BigEndian(ip.Slice(4, 2)),
            headerLength,
            totalLength - headerLength,
            moreFragments,
            fragmentOffset);

        return true;
    }

    private byte[]? AddIpv4Fragment(Ipv4FragmentKey key, Ipv4Header header, ReadOnlySpan<byte> fragmentPayload)
    {
        if (fragmentPayload.Length <= 0 ||
            header.FragmentOffset < 0 ||
            header.FragmentOffset + fragmentPayload.Length > MaxIpv4PayloadLength)
        {
            Interlocked.Increment(ref _malformedFrames);
            return null;
        }

        lock (_fragmentGate)
        {
            RemoveExpiredFragments();

            if (!_ipv4Fragments.TryGetValue(key, out var buffer))
            {
                buffer = new Ipv4FragmentBuffer();
                _ipv4Fragments[key] = buffer;
            }

            buffer.LastSeenUtc = DateTime.UtcNow;

            if (!header.MoreFragments)
                buffer.TotalLength = header.FragmentOffset + fragmentPayload.Length;

            fragmentPayload.CopyTo(buffer.Payload.AsSpan(header.FragmentOffset));

            var end = header.FragmentOffset + fragmentPayload.Length;
            for (var i = header.FragmentOffset; i < end; i++)
            {
                if (buffer.Received[i]) continue;
                buffer.Received[i] = true;
                buffer.ReceivedCount++;
            }

            if (buffer.TotalLength is not { } total || buffer.ReceivedCount < total)
            {
                TrimFragmentCache();
                return null;
            }

            var payload = buffer.Payload.AsSpan(0, total).ToArray();
            _ipv4Fragments.Remove(key);
            return payload;
        }
    }

    private void RemoveExpiredFragments()
    {
        var cutoff = DateTime.UtcNow - Ipv4FragmentTimeout;
        var expired = _ipv4Fragments
            .Where(x => x.Value.LastSeenUtc < cutoff)
            .Select(x => x.Key)
            .ToList();

        foreach (var key in expired) _ipv4Fragments.Remove(key);
    }

    private void TrimFragmentCache()
    {
        if (_ipv4Fragments.Count <= MaxPendingIpv4Fragments) return;

        var remove = _ipv4Fragments
            .OrderBy(x => x.Value.LastSeenUtc)
            .Take(_ipv4Fragments.Count - MaxPendingIpv4Fragments)
            .Select(x => x.Key)
            .ToList();

        foreach (var key in remove) _ipv4Fragments.Remove(key);
    }

    private void ClearIpv4Fragments()
    {
        lock (_fragmentGate) _ipv4Fragments.Clear();
    }

    private void HandleIpv6(ReadOnlySpan<byte> ip)
    {
        if (ip.Length < 40 || (ip[0] >> 4) != 6)
        {
            Interlocked.Increment(ref _malformedFrames);
            return;
        }

        // Extensões IPv6 não são necessárias para o tráfego Albion observado até aqui.
        if ((ProtocolType)ip[6] != ProtocolType.Udp) return;

        var payloadLength = BinaryPrimitives.ReadUInt16BigEndian(ip.Slice(4, 2));
        var available = Math.Min(payloadLength, ip.Length - 40);
        if (available <= 0) return;

        HandleUdp(ip.Slice(40, available));
    }

    private void HandleUdp(ReadOnlySpan<byte> udp)
    {
        if (udp.Length < 8) return;

        var src = BinaryPrimitives.ReadUInt16BigEndian(udp.Slice(0, 2));
        var dst = BinaryPrimitives.ReadUInt16BigEndian(udp.Slice(2, 2));
        if (!IsPhotonPort(src) && !IsPhotonPort(dst)) return;

        var length = BinaryPrimitives.ReadUInt16BigEndian(udp.Slice(4, 2));
        if (length < 8 || length > udp.Length) length = (ushort)Math.Min(udp.Length, ushort.MaxValue);

        var payload = udp.Slice(8, length - 8);
        if (payload.Length == 0) return;

        Interlocked.Increment(ref _photonDatagrams);

        try
        {
            _receiver.ReceivePacket(payload);
        }
        catch
        {
            Interlocked.Increment(ref _malformedFrames);
        }
    }

    private static bool IsPhotonPort(ushort port) => port is 5055 or 5056 or 5058;

    private void ResetCounters()
    {
        Interlocked.Exchange(ref _framesSeen, 0);
        Interlocked.Exchange(ref _photonDatagrams, 0);
        Interlocked.Exchange(ref _ipv4FragmentPackets, 0);
        Interlocked.Exchange(ref _ipv4Reassemblies, 0);
        Interlocked.Exchange(ref _malformedFrames, 0);
    }

    public void Dispose() => Stop();

    private readonly record struct Ipv4FragmentKey(
        uint Source,
        uint Destination,
        ushort Identification,
        byte Protocol);

    private readonly record struct Ipv4Header(
        byte Protocol,
        ushort Identification,
        int HeaderLength,
        int PayloadLength,
        bool MoreFragments,
        int FragmentOffset)
    {
        public bool IsFragmented => MoreFragments || FragmentOffset != 0;
    }

    private sealed class Ipv4FragmentBuffer
    {
        public byte[] Payload { get; } = new byte[MaxIpv4PayloadLength];
        public bool[] Received { get; } = new bool[MaxIpv4PayloadLength];
        public int ReceivedCount { get; set; }
        public int? TotalLength { get; set; }
        public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
    }
}

public sealed record CaptureDiagnosticsSnapshot(
    bool IsRunning,
    int OpenedDevices,
    IReadOnlyList<string> DeviceNames,
    long FramesSeen,
    long PhotonDatagrams,
    long Ipv4FragmentPackets,
    long Ipv4Reassemblies,
    long MalformedFrames);
