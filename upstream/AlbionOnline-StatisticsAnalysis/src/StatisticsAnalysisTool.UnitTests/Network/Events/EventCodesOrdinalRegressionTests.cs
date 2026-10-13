using System;
using FluentAssertions;
using NUnit.Framework;
using StatisticsAnalysisTool.Network;

namespace StatisticsAnalysisTool.UnitTests.Network.Events;

/// <summary>
/// Códigos Photon fixados contra a captura real do Dragonfire Patch 3
/// (12/10/2026). Qualquer inserção/remoção no enum ordinal deve falhar
/// antes de produzir uma versão incapaz de receber os eventos corretos.
/// </summary>
[TestFixture]
public sealed class EventCodesOrdinalRegressionTests
{
    [TestCase(EventCodes.GuildPlayerUpdated, 104)]
    [TestCase(EventCodes.NewMob, 123)]
    [TestCase(EventCodes.ClusterInfoUpdate, 139)]
    [TestCase(EventCodes.TimeSync, 159)]
    [TestCase(EventCodes.KilledPlayer, 163)]
    [TestCase(EventCodes.Died, 164)]
    [TestCase(EventCodes.PartyJoined, 230)]
    [TestCase(EventCodes.PartyDisbanded, 231)]
    [TestCase(EventCodes.PartyPlayerJoined, 232)]
    [TestCase(EventCodes.PartyPlayerLeft, 234)]
    [TestCase(EventCodes.OtherGrabbedLoot, 278)]
    [TestCase(EventCodes.FriendOnlineStatus, 297)]
    [TestCase(EventCodes.FeatureSwitchInfo, 503)]
    public void EventCode_MatchesDragonfirePatch3ExpectedOrdinal(EventCodes eventCode, int expectedCode)
    {
        ((int)eventCode).Should().Be(expectedCode,
            "os códigos foram fixados a partir da captura Photon pós-Dragonfire Patch 3");
    }

    [Test]
    public void RemovedMercenaryEvent_MustNotReturnToOrdinalEnum()
    {
        Enum.GetNames<EventCodes>().Should().NotContain("InvitedMercenaryToMatch",
            "o evento removido desloca todos os códigos a partir de ClusterInfoUpdate");
    }

    [Test]
    public void RemovalShiftsOnlyTheSuffixOfOrdinalEnum()
    {
        ((int)EventCodes.ClusterInfoUpdate).Should().Be((int)EventCodes.NewMob + 16);
        ((int)EventCodes.PartyJoined).Should().Be((int)EventCodes.PartyDisbanded - 1);
        ((int)EventCodes.OtherGrabbedLoot).Should().Be(278);
    }
}
