namespace Imortais.LinuxClient.Network;

/// <summary>
/// Photon event codes fixed against the Dragonfire Patch 3 observations (12 Oct 2026).
/// Unlike the Photon wire EventData.Code (byte), Albion sends the full event code in
/// parameter 252; values such as OtherGrabbedLoot=278 cannot fit in a byte.
/// </summary>
public static class LinuxPhotonEventCodes
{
    public const int Leave = 1;
    public const int HealthUpdate = 6;
    public const int HealthUpdates = 7;
    public const int NewCharacter = 29;
    public const int GuildPlayerUpdated = 104;
    public const int NewMob = 123;
    public const int ClusterInfoUpdate = 139;
    public const int TimeSync = 159;
    public const int KilledPlayer = 163;
    public const int Died = 164;
    public const int PartyJoined = 230;
    public const int PartyDisbanded = 231;
    public const int PartyPlayerJoined = 232;
    public const int PartyPlayerLeft = 234;
    public const int OtherGrabbedLoot = 278;
    public const int FriendOnlineStatus = 297;
    public const int FeatureSwitchInfo = 503;

    public static int Resolve(byte packetCode, IReadOnlyDictionary<byte, object> parameters)
    {
        if (!parameters.TryGetValue(252, out var value) || value is null)
            return packetCode;
        try
        {
            var code = Convert.ToInt32(value);
            return code is >= 0 and <= 2048 ? code : -1;
        }
        catch
        {
            return -1;
        }
    }
}
