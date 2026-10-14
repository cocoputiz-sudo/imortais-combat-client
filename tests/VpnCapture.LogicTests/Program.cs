using StatisticsAnalysisTool.Network.PacketProviders;

var now = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
Assert(VpnCaptureHealthPolicy.Describe(false, true, now, null, null).Contains("desligado"),
    "Normal mode must never be classified as a VPN outage");
Assert(VpnCaptureHealthPolicy.Describe(true, false, now, null, null).Contains("aguardando Albion"),
    "Game offline must never cause an automatic restart");
Assert(VpnCaptureHealthPolicy.Describe(true, true, now, null, null).Contains("aguardando pacotes"),
    "No candidate packets is inconclusive, not a recovery trigger");
Assert(VpnCaptureHealthPolicy.Describe(true, true, now, now.AddSeconds(-3), null).Contains("nenhum evento Photon"),
    "Candidate UDP traffic does not prove Photon decoding");
Assert(VpnCaptureHealthPolicy.Describe(true, true, now, now.AddSeconds(-5), now.AddSeconds(-40)).Contains("verificar rota"),
    "Decoded stream silence requires investigation, not blind adapter reset");
Assert(VpnCaptureHealthPolicy.Describe(true, true, now, now.AddSeconds(-5), now.AddSeconds(-2)).Contains("sem ação"),
    "Healthy decoded stream should not be disrupted");

VpnCaptureAssistDiagnostics.ObserveCandidate("adapter-a");
VpnCaptureAssistDiagnostics.ObserveCandidate("adapter-b");
VpnCaptureAssistDiagnostics.ObserveDecodedEvent(8, new Dictionary<byte, object>());
VpnCaptureAssistDiagnostics.ObserveSelectedAdapter("adapter-a");
VpnCaptureAssistDiagnostics.ObserveNetworkChange();
VpnCaptureAssistDiagnostics.ObserveUserRescan();
VpnCaptureAssistDiagnostics.ObserveRecoveryAttempt();
var snap = VpnCaptureAssistDiagnostics.Snapshot(true, true);
Assert(snap.CandidateAdapterCount == 2 && snap.CandidatePackets == 2,
    "Observe each candidate adapter without exposing its packet contents");
Assert(snap.DecodedEvents == 1 && snap.LastDecodedEventUtc is not null,
    "Decoded events must be counted separately from UDP candidates");
Assert(snap.UserRescans == 1 && snap.NetworkChanges == 1 && snap.RecoveryAttempts == 1,
    "A manual request must be distinguishable from network changes and actual recovery attempts");
Assert(snap.SelectedAdapter == "adapter-a", "Report selected adapter");
Console.WriteLine("PASS: VPN opt-in diagnostics, independent UDP/Photon counters, and conservative recovery classification");

static void Assert(bool okay, string explanation)
{
    if (!okay) throw new InvalidOperationException(explanation);
}
