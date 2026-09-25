using System.Buffers.Binary;
using System.Net.Sockets;
using Imortais.LinuxClient.Network;
using Libpcap;

namespace Imortais.LinuxClient.Services;

public sealed class LinuxCaptureService : IDisposable
{
    public const string PacketFilter = "(ip or ip6) and udp and (port 5055 or port 5056 or port 5058)";

    private readonly LinuxPhotonReceiver _receiver;
    private readonly object _gate = new();
    private PcapDispatcher? _dispatcher;
    private CancellationTokenSource? _cts;
    private Thread? _thread;

    public event Action<string>? StatusChanged;
    public bool IsRunning => _thread?.IsAlive == true;

    public LinuxCaptureService(LinuxPhotonReceiver receiver) => _receiver = receiver;

    public (bool Ok, string Message, int Devices) Start()
    {
        lock (_gate)
        {
            if (IsRunning) return (true, "Captura já ativa", 0);

            try
            {
                var dispatcher = new PcapDispatcher(Dispatch);
                var opened = 0;

                foreach (var device in Pcap.ListDevices())
                {
                    if (device.Flags.HasFlag(PcapDeviceFlags.Loopback)) continue;
                    if (!device.Flags.HasFlag(PcapDeviceFlags.Up)) continue;

                    try
                    {
                        dispatcher.OpenDevice(device, pcap => pcap.NonBlocking = true);
                        opened++;
                    }
                    catch { }
                }

                if (opened == 0)
                {
                    dispatcher.Dispose();
                    return (false, "Nenhuma interface libpcap pôde ser aberta. Verifique libpcap e permissões CAP_NET_RAW/CAP_NET_ADMIN.", 0);
                }

                dispatcher.Filter = PacketFilter;
                _dispatcher = dispatcher;
                _cts = new CancellationTokenSource();
                _thread = new Thread(Worker) { IsBackground = true, Name = "IMORTAIS-LinuxCapture" };
                _thread.Start();
                StatusChanged?.Invoke($"Captura ativa em {opened} interface(s)");
                return (true, "Captura iniciada", opened);
            }
            catch (Exception ex)
            {
                _dispatcher?.Dispose();
                _dispatcher = null;
                return (false, ex.Message, 0);
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
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                errors++;
                if (errors == 1 || errors % 20 == 0) StatusChanged?.Invoke("Erro libpcap: " + ex.Message);
                _cts?.Token.WaitHandle.WaitOne(100);
            }
        }
    }

    private void Dispatch(Pcap pcap, ref Packet packet)
    {
        try
        {
            var frame = packet.Data;
            if (frame is null || frame.Length < 14) return;

            var etherType = BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(12, 2));
            if (etherType == 0x0800) HandleIpv4(frame.AsSpan(14));
            else if (etherType == 0x86DD) HandleIpv6(frame.AsSpan(14));
        }
        catch { }
    }

    private void HandleIpv4(ReadOnlySpan<byte> ip)
    {
        if (ip.Length < 20 || (ip[0] >> 4) != 4) return;
        var ihl = (ip[0] & 0x0F) * 4;
        if (ihl < 20 || ip.Length < ihl) return;

        var totalLength = BinaryPrimitives.ReadUInt16BigEndian(ip.Slice(2, 2));
        if (totalLength < ihl || totalLength > ip.Length) return;

        var flagsOffset = BinaryPrimitives.ReadUInt16BigEndian(ip.Slice(6, 2));
        var fragmented = (flagsOffset & 0x3FFF) != 0;
        if (fragmented) return; // Photon fragments are handled by the parser; IP fragment reassembly lands in v0.1.1.

        if ((ProtocolType)ip[9] != ProtocolType.Udp) return;
        HandleUdp(ip.Slice(ihl, totalLength - ihl));
    }

    private void HandleIpv6(ReadOnlySpan<byte> ip)
    {
        if (ip.Length < 40 || (ip[0] >> 4) != 6) return;
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
        if (length < 8 || length > udp.Length) length = (ushort)udp.Length;

        var payload = udp.Slice(8, length - 8);
        if (payload.Length == 0) return;
        _receiver.ReceivePacket(payload);
    }

    private static bool IsPhotonPort(ushort port) => port is 5055 or 5056 or 5058;

    public void Dispose() => Stop();
}
