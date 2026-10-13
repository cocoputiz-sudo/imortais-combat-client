using FluentAssertions;
using NUnit.Framework;
using StatisticsAnalysisTool.Network.Events;

namespace StatisticsAnalysisTool.UnitTests.Network.Events;

[TestFixture]
public class PartyJoinedEventTests
{
    [Test]
    public void Constructor_WithGuidBytesParameter_AddsPartyUsers()
    {
        var firstGuidBytes = new byte[]
        {
            138, 61, 186, 72, 17, 149, 121, 72, 154, 110, 20, 231, 64, 20, 106, 2
        };

        var secondGuidBytes = new byte[]
        {
            211, 221, 124, 14, 233, 103, 215, 74, 185, 66, 67, 20, 244, 60, 44, 155
        };

        var parameters = new Dictionary<byte, object>
        {
            {
                8,
                new byte[]
                {
                    138, 61, 186, 72, 17, 149, 121, 72, 154, 110, 20, 231, 64, 20, 106, 2,
                    211, 221, 124, 14, 233, 103, 215, 74, 185, 66, 67, 20, 244, 60, 44, 155
                }
            },
            {
                9,
                new[]
                {
                    "Bruno313",
                    "Triky313"
                }
            }
        };

        var partyJoinedEvent = new PartyJoinedEvent(parameters);

        partyJoinedEvent.HasCompleteRoster.Should().BeTrue();
        partyJoinedEvent.PartyUsers.Should().HaveCount(2);
        partyJoinedEvent.PartyUsers[new Guid(firstGuidBytes)].Should().Be("Bruno313");
        partyJoinedEvent.PartyUsers[new Guid(secondGuidBytes)].Should().Be("Triky313");
    }

    [Test]
    public void Constructor_WithObjectArrayGuidParameter_AddsPartyUsers()
    {
        var firstGuidBytes = new byte[]
        {
            138, 61, 186, 72, 17, 149, 121, 72, 154, 110, 20, 231, 64, 20, 106, 2
        };

        var secondGuidBytes = new byte[]
        {
            211, 221, 124, 14, 233, 103, 215, 74, 185, 66, 67, 20, 244, 60, 44, 155
        };

        var parameters = new Dictionary<byte, object>
        {
            {
                8,
                new object[]
                {
                    firstGuidBytes,
                    secondGuidBytes
                }
            },
            {
                9,
                new object[]
                {
                    "Bruno313",
                    "Triky313"
                }
            }
        };

        var partyJoinedEvent = new PartyJoinedEvent(parameters);

        partyJoinedEvent.HasCompleteRoster.Should().BeTrue();
        partyJoinedEvent.PartyUsers.Should().HaveCount(2);
        partyJoinedEvent.PartyUsers[new Guid(firstGuidBytes)].Should().Be("Bruno313");
        partyJoinedEvent.PartyUsers[new Guid(secondGuidBytes)].Should().Be("Triky313");
    }
    [Test]
    public void Constructor_WithoutExpectedRosterFields_DoesNotTrustEmptyRoster()
    {
        var parsed = new PartyJoinedEvent(new Dictionary<byte, object>
        {
            { 7, new[] { "BadMack", "MitriusBR" } }
        });

        parsed.HasCompleteRoster.Should().BeFalse();
        parsed.PartyUsers.Should().BeEmpty();
        parsed.DecodeDiagnostics.Should().Contain("7:strings(2)");
    }

    [Test]
    public void Constructor_WithPartialGuidAndNameLists_RejectsWholeRoster()
    {
        var parsed = new PartyJoinedEvent(new Dictionary<byte, object>
        {
            { 8, Guid.NewGuid().ToByteArray() },
            { 9, new[] { "BadMack", "MitriusBR" } }
        });

        parsed.HasCompleteRoster.Should().BeFalse();
        parsed.PartyUsers.Should().BeEmpty();
    }

    [Test]
    public void Constructor_WithTruncatedGuidBytes_RejectsWholeRoster()
    {
        var parsed = new PartyJoinedEvent(new Dictionary<byte, object>
        {
            { 8, new byte[17] },
            { 9, new[] { "BadMack" } }
        });

        parsed.HasCompleteRoster.Should().BeFalse();
        parsed.PartyUsers.Should().BeEmpty();
    }

    [Test]
    public void Constructor_WithGuidObjects_DecodesCompleteRoster()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var parsed = new PartyJoinedEvent(new Dictionary<byte, object>
        {
            { 8, new object[] { first, second } },
            { 9, new[] { "BadMack", "MitriusBR" } }
        });

        parsed.HasCompleteRoster.Should().BeTrue();
        parsed.PartyUsers.Should().HaveCount(2);
        parsed.PartyUsers[first].Should().Be("BadMack");
        parsed.PartyUsers[second].Should().Be("MitriusBR");
    }

    [Test]
    public void Constructor_WithLegacyGuidAndNameFields_UsesValidatedLayout45()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var parsed = new PartyJoinedEvent(new Dictionary<byte, object>
        {
            { 4, new object[] { first.ToByteArray(), second.ToByteArray() } },
            { 5, new[] { "BadMack", "MitriusBR" } },
            { 8, new int[] { 35, 0 } },
            { 9, new int[] { -1, -1 } }
        });

        parsed.HasCompleteRoster.Should().BeTrue();
        parsed.PartyUsers.Should().HaveCount(2);
        parsed.PartyUsers[first].Should().Be("BadMack");
        parsed.PartyUsers[second].Should().Be("MitriusBR");
        parsed.DecodeDiagnostics.Should().Contain("layout=4/5");
    }

    [Test]
    public void Constructor_WithIncompleteLayout45_DoesNotPublishPartialMembers()
    {
        var parsed = new PartyJoinedEvent(new Dictionary<byte, object>
        {
            { 4, new object[] { Guid.NewGuid().ToByteArray() } },
            { 5, new[] { "BadMack", "MitriusBR" } }
        });

        parsed.HasCompleteRoster.Should().BeFalse();
        parsed.PartyUsers.Should().BeEmpty();
    }

}