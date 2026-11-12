using StatisticsAnalysisTool.Network.PacketProviders;
using L = StatisticsAnalysisTool.Network.PacketProviders.CaptureLinkLayer;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FALHOU: " + message);
}

// Pacote IPv4/UDP mínimo (20 + 8 bytes) e IPv6 (40 bytes de cabeçalho).
var ipv4 = new byte[28];
ipv4[0] = 0x45; ipv4[9] = 17;
var ipv6 = new byte[48];
ipv6[0] = 0x60; ipv6[6] = 17;

static byte[] Concat(params byte[][] parts) => parts.SelectMany(x => x).ToArray();
var mac = new byte[12];

// Ethernet continua igual ao comportamento anterior.
Check(L.TryGetNetworkLayer(L.DltEthernet, Concat(mac, [0x08, 0x00], ipv4), out var et, out var l3)
      && et == L.EtherTypeIPv4 && l3.Length == ipv4.Length && l3[0] == 0x45, "Ethernet IPv4");
Check(L.TryGetNetworkLayer(L.DltEthernet, Concat(mac, [0x86, 0xDD], ipv6), out et, out l3)
      && et == L.EtherTypeIPv6 && l3[0] == 0x60, "Ethernet IPv6");
Check(!L.TryGetNetworkLayer(L.DltEthernet, Concat(mac, [0x08, 0x06], new byte[28]), out _, out _), "ARP ignorado");
Check(!L.TryGetNetworkLayer(L.DltEthernet, new byte[10], out _, out _), "Ethernet curto demais");
// VLAN 802.1Q, comum em placa com tag de prioridade.
Check(L.TryGetNetworkLayer(L.DltEthernet, Concat(mac, [0x81, 0x00, 0x00, 0x05, 0x08, 0x00], ipv4), out et, out l3)
      && et == L.EtherTypeIPv4 && l3[0] == 0x45, "Ethernet com VLAN");

// O defeito corrigido: Wintun/WireGuard/WARP entregam IP sem cabeçalho Ethernet.
Check(L.TryGetNetworkLayer(L.DltRaw, ipv4, out et, out l3) && et == L.EtherTypeIPv4 && l3.Length == ipv4.Length, "IP sem Ethernet (IPv4)");
Check(L.TryGetNetworkLayer(L.DltRaw, ipv6, out et, out _) && et == L.EtherTypeIPv6, "IP sem Ethernet (IPv6)");
Check(L.TryGetNetworkLayer(L.DltIPv4, ipv4, out et, out _) && et == L.EtherTypeIPv4, "DLT_IPV4");
Check(!L.TryGetNetworkLayer(L.DltRaw, [0x00, 0x01], out _, out _), "lixo sem versão IP");
// Prova do defeito antigo: lido como Ethernet, o mesmo pacote IP era descartado.
var oldEtherType = (ushort)((ipv4[12] << 8) | ipv4[13]);
Check(oldEtherType != 0x0800, "o leitor antigo via endereço IP no lugar do tipo e descartava");

// Loopback do Npcap (DLT_NULL, 4 bytes de família em ordem do host).
Check(L.TryGetNetworkLayer(L.DltNull, Concat([0x02, 0, 0, 0], ipv4), out et, out l3) && et == L.EtherTypeIPv4 && l3[0] == 0x45, "loopback IPv4");
Check(L.TryGetNetworkLayer(L.DltNull, Concat([0x17, 0, 0, 0], ipv6), out et, out _) && et == L.EtherTypeIPv6, "loopback IPv6 (AF_INET6 do Windows)");
Check(L.TryGetNetworkLayer(L.DltLoop, Concat([0, 0, 0, 0x02], ipv4), out et, out _) && et == L.EtherTypeIPv4, "DLT_LOOP");
Check(!L.TryGetNetworkLayer(L.DltNull, [0x02, 0, 0, 0], out _, out _), "loopback vazio");

// Linux cooked (captura 'any' do cliente Linux).
var sll = new byte[16]; sll[14] = 0x08; sll[15] = 0x00;
Check(L.TryGetNetworkLayer(L.DltLinuxSll, Concat(sll, ipv4), out et, out l3) && et == L.EtherTypeIPv4 && l3[0] == 0x45, "Linux SLL");
var sll2 = new byte[20]; sll2[0] = 0x86; sll2[1] = 0xDD;
Check(L.TryGetNetworkLayer(L.DltLinuxSll2, Concat(sll2, ipv6), out et, out _) && et == L.EtherTypeIPv6, "Linux SLL2");

// Tipo desconhecido: não suportado, nunca lança.
Check(!L.IsSupported(105) && !L.TryGetNetworkLayer(105, ipv4, out _, out _), "802.11 não suportado");
Check(L.IsSupported(L.DltRaw) && L.IsSupported(L.DltNull) && L.IsSupported(L.DltEthernet), "suportados");
Check(L.Describe(L.DltRaw) == "IP sem Ethernet" && L.Describe(L.DltNull) == "loopback", "descrições");

// ===== Assinatura estrita de Photon (só para o loopback) =====
static byte[] Command(byte type, int bodyLength)
{
    var c = new byte[12 + bodyLength];
    c[0] = type;
    var len = 12 + bodyLength;
    c[4] = (byte)(len >> 24); c[5] = (byte)(len >> 16); c[6] = (byte)(len >> 8); c[7] = (byte)len;
    return c;
}
static byte[] Photon(byte flags, params byte[][] commands)
{
    var header = new byte[12];
    header[2] = flags;
    header[3] = (byte)commands.Length;
    var crc = flags == 0xCC ? new byte[4] : [];
    return Concat([header, crc, .. commands]);
}

Check(PhotonDatagramSignature.IsWellFormed(Photon(0, Command(6, 30))), "um comando confiável");
Check(PhotonDatagramSignature.IsWellFormed(Photon(0, Command(1, 8), Command(7, 40), Command(8, 100))), "vários comandos");
Check(PhotonDatagramSignature.IsWellFormed(Photon(0xCC, Command(6, 10))), "com CRC");
Check(!PhotonDatagramSignature.IsWellFormed(Photon(1, Command(6, 30))), "criptografado não é aceito");
Check(!PhotonDatagramSignature.IsWellFormed(Concat(Photon(0, Command(6, 30)), [0x00])), "sobra de byte rejeitada");
var truncated = Photon(0, Command(6, 30));
Check(!PhotonDatagramSignature.IsWellFormed(truncated.AsSpan(0, truncated.Length - 1)), "comando cortado rejeitado");
Check(!PhotonDatagramSignature.IsWellFormed(Photon(0, Command(0, 4))), "tipo de comando 0 rejeitado");
Check(!PhotonDatagramSignature.IsWellFormed(Photon(0, Command(13, 4))), "tipo de comando acima de 12 rejeitado");
Check(!PhotonDatagramSignature.IsWellFormed(Photon(0)), "zero comandos rejeitado");
Check(!PhotonDatagramSignature.IsWellFormed(new byte[8]), "curto demais");

// Tráfego local comum não pode passar: DNS, mDNS/SSDP em texto e bytes aleatórios.
var dns = Convert.FromHexString("1a2b01000001000000000000076578616d706c6503636f6d0000010001");
Check(!PhotonDatagramSignature.IsWellFormed(dns), "consulta DNS rejeitada");
Check(!PhotonDatagramSignature.IsWellFormed(System.Text.Encoding.ASCII.GetBytes("M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\n\r\n")), "SSDP rejeitado");
var rng = new Random(20261112);
var falsePositives = 0;
for (var i = 0; i < 200_000; i++)
{
    var buf = new byte[rng.Next(24, 1400)];
    rng.NextBytes(buf);
    if (PhotonDatagramSignature.IsWellFormed(buf)) falsePositives++;
}
Check(falsePositives == 0, $"bytes aleatórios aceitos como Photon: {falsePositives}");

Console.WriteLine("CaptureLinkLayer.LogicTests: OK");
