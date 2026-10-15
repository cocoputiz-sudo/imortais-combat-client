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

Assert(VpnCaptureRecoveryPolicy.IsAlbionProcessName("Albion-Online")
       && VpnCaptureRecoveryPolicy.IsAlbionProcessName("AlbionOnline")
       && VpnCaptureRecoveryPolicy.IsAlbionProcessName("Albion-Online-Client"),
    "Allow independently observed Albion process even without Photon packets");
Assert(!VpnCaptureRecoveryPolicy.IsAlbionProcessName("steam")
       && !VpnCaptureRecoveryPolicy.IsAlbionProcessName(null),
    "Do not treat unrelated network activity as Albion running");
Assert(VpnCaptureRecoveryPolicy.HasGamePresence(false, true)
       && VpnCaptureRecoveryPolicy.HasGamePresence(true, false)
       && !VpnCaptureRecoveryPolicy.HasGamePresence(false, false),
    "Absence of decoded Photon must not hide a running Albion client");
Console.WriteLine("PASS: initially blind VPN capture can recover if game process is running");

var packets = new VpnCandidateTrafficWindow();
for (var i = 0; i < 7; i++)
    packets.Observe(now.AddSeconds(-70 + i * 10));
Assert(packets.Count < VpnCaptureRecoveryPolicy.MinAlternateCandidates,
    "Sparse traffic over a minute cannot qualify for VPN failover");
for (var i = 0; i < 7; i++)
    packets.Observe(now.AddMilliseconds(-600 + i * 75));
Assert(packets.Count >= VpnCaptureRecoveryPolicy.MinAlternateCandidates
       && packets.LastSeenUtc <= now,
    "Real six-packet burst within the sliding 12s window qualifies");
Console.WriteLine("PASS: actual rolling 12s candidate window prevents sparse false positives");

Assert(VpnCaptureRecoveryPolicy.CanProbe(true, now, now.AddSeconds(-90),
    now.AddSeconds(-35), null, now.AddSeconds(-1), 7, 0),
    "Eligible: prior decoded stream silent 35s and alternate has new candidates");
Assert(!VpnCaptureRecoveryPolicy.CanProbe(false, now, now.AddSeconds(-90),
    now.AddSeconds(-35), null, now.AddSeconds(-1), 7, 0),
    "Never probe without explicit VPN opt-in");
Assert(!VpnCaptureRecoveryPolicy.CanProbe(true, now, now.AddSeconds(-90),
    now.AddSeconds(-2), null, now.AddSeconds(-1), 7, 0),
    "Never disturb healthy Photon stream");
Assert(!VpnCaptureRecoveryPolicy.CanProbe(true, now, now.AddSeconds(-90),
    now.AddSeconds(-35), null, now.AddSeconds(-1), 2, 0),
    "Alternate requires sustained candidates, not single packet");
Assert(!VpnCaptureRecoveryPolicy.CanProbe(true, now, now.AddSeconds(-90),
    now.AddSeconds(-35), null, now.AddSeconds(-20), 7, 0),
    "Do not probe stale alternate traffic");
Assert(!VpnCaptureRecoveryPolicy.CanProbe(true, now, now.AddSeconds(-90),
    now.AddSeconds(-35), now.AddSeconds(-20), now.AddSeconds(-1), 7, 0),
    "Never enter reconnect loop within 90s cooldown");
Assert(!VpnCaptureRecoveryPolicy.CanProbe(true, now, now.AddSeconds(-90),
    now.AddSeconds(-35), null, now.AddSeconds(-1), 7, 3),
    "Three per hour is the hard probe budget");
Assert(!VpnCaptureRecoveryPolicy.CanProbe(true, now, now.AddSeconds(-8),
    null, null, now.AddSeconds(-1), 7, 0),
    "Allow starting game to settle before any probe");
Assert(VpnCaptureRecoveryPolicy.TrialSucceeded(110, 111) &&
      !VpnCaptureRecoveryPolicy.TrialSucceeded(110, 110) &&
      VpnCaptureRecoveryPolicy.TrialExpired(now, now.AddSeconds(-8)) &&
      !VpnCaptureRecoveryPolicy.TrialExpired(now, now.AddSeconds(-5)),
    "Only a decoded Photon event confirms a probe; revert on deadline");
VpnCaptureAssistDiagnostics.ObserveProbeStarted();
VpnCaptureAssistDiagnostics.ObserveProbeConfirmed();
VpnCaptureAssistDiagnostics.ObserveProbeReverted();
var detailed = VpnCaptureAssistDiagnostics.Snapshot(true, true);
Assert(detailed.ProbeStarts == 1 && detailed.ProbeConfirmed == 1 && detailed.ProbeReverted == 1,
    "Distinguish temporary interface trials from opening Npcap dispatcher");
Console.WriteLine("PASS: VPN probes gated by opt-in, decoded silence, fresh candidates, cooldown and bounded retry");

static void Assert(bool okay, string explanation)
{
    if (!okay) throw new InvalidOperationException(explanation);
}
