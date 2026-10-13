using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using NUnit.Framework;
using StatisticsAnalysisTool.Imortais;
using StatisticsAnalysisTool.Network;

namespace StatisticsAnalysisTool.UnitTests.Network.Events;

[TestFixture]
public sealed class PhotonEventDiagnosticsCollectorTests
{
    [Test]
    public void Observe_CountsEveryEventAndKeepsOnlyFirstTypeSignature()
    {
        var collector = new PhotonEventDiagnosticsCollector();
        var code = (short)EventCodes.PartyPlayerJoined;
        collector.Observe(code, new Dictionary<byte, object>
        {
            [0] = 123,
            [1] = new byte[16],
            [2] = "BadMack",
            [3] = 1,
            [4] = 20,
            [5] = 12,
            [6] = -1,
            [7] = true,
            [252] = code
        });
        collector.Observe(code, new Dictionary<byte, object>
        {
            [1] = new byte[4],
            [2] = "different name",
            [252] = code
        });

        using var json = JsonDocument.Parse(collector.ExportJson());
        var root = json.RootElement;
        root.GetProperty("totalEvents").GetInt64().Should().Be(2);
        root.GetProperty("distinctCodes").GetInt32().Should().Be(1);
        var observed = root.GetProperty("observed").EnumerateArray().Single();
        observed.GetProperty("count").GetInt64().Should().Be(2);
        var shapes = observed.GetProperty("firstExample").GetProperty("valueShapes");
        shapes.GetProperty("1").GetString().Should().Be("byte[16]");
        shapes.GetProperty("2").GetString().Should().Be("string(7)");
        observed.GetProperty("matchesProfiles").EnumerateArray().Select(x => x.GetString())
            .Should().Contain("PartyPlayerJoined");
        var expected = root.GetProperty("expected").EnumerateArray()
            .Single(x => x.GetProperty("eventName").GetString() == "PartyPlayerJoined");
        expected.GetProperty("expectedEnumCode").GetInt16().Should().Be(code);
        expected.GetProperty("firstSampleMatchesExpectedShape").GetBoolean().Should().BeTrue();
    }

    [Test]
    public void Observe_UnregisteredShiftedCodeStillAppearsAsProfileCandidate()
    {
        var collector = new PhotonEventDiagnosticsCollector();
        var shifted = checked((short)((short)EventCodes.PartyPlayerJoined + 13));
        collector.Observe(shifted, new Dictionary<byte, object>
        {
            [0] = 222,
            [1] = new byte[16],
            [2] = "TEST_PLAYER",
            [3] = 1, [4] = 20, [5] = 1, [6] = -1, [7] = true,
            [252] = shifted
        });
        using var json = JsonDocument.Parse(collector.ExportJson());
        var evt = json.RootElement.GetProperty("observed").EnumerateArray().Single();
        evt.GetProperty("code").GetInt16().Should().Be(shifted);
        evt.GetProperty("matchesProfiles").EnumerateArray().Select(x => x.GetString())
            .Should().Contain("PartyPlayerJoined");
    }

    [Test]
    public void ExportNeverContainsPlayerNamesGuidsOrParameterValues()
    {
        var collector = new PhotonEventDiagnosticsCollector();
        var player = "SECRET_PLAYER_123";
        var guid = Guid.NewGuid();
        var code = (short)EventCodes.PartyJoined;
        collector.Observe(code, new Dictionary<byte, object>
        {
            [4] = new object[] { guid.ToByteArray() },
            [5] = new[] { player },
            [11] = guid,
            [12] = 987654321,
            [252] = code
        });
        var json = collector.ExportJson();
        json.Should().NotContain(player);
        json.Should().NotContain(guid.ToString());
        json.Should().NotContain("987654321");
        json.Should().Contain("byte[16]");
        using var doc = JsonDocument.Parse(json);
        var record = doc.RootElement.GetProperty("observed").EnumerateArray().Single();
        record.GetProperty("matchesProfiles").EnumerateArray().Select(x => x.GetString())
            .Should().Contain("PartyJoined");
    }

    [Test]
    public void ExpectedProfilesRecognizePartyLeaveDisbandAndLootShapes()
    {
        var left = new Dictionary<string, string>
        {
            ["0"] = "number",
            ["1"] = "byte[16]",
            ["252"] = "number"
        };
        var disband = new Dictionary<string, string>
        {
            ["1"] = "number",
            ["252"] = "number"
        };
        var loot = new Dictionary<string, string>
        {
            ["0"] = "number", ["1"] = "string(5)", ["2"] = "string(7)",
            ["4"] = "number", ["5"] = "number"
        };
        var silver = new Dictionary<string, string>
        {
            ["0"] = "number", ["2"] = "string(7)", ["3"] = "boolean", ["5"] = "number"
        };
        PhotonEventDiagnosticsCollector.Matches("PartyPlayerLeft", left).Should().BeTrue();
        PhotonEventDiagnosticsCollector.Matches("PartyDisbanded", disband).Should().BeTrue();
        PhotonEventDiagnosticsCollector.Matches("OtherGrabbedLoot", loot).Should().BeTrue();
        PhotonEventDiagnosticsCollector.Matches("OtherGrabbedLoot", silver).Should().BeTrue();
        PhotonEventDiagnosticsCollector.Matches("PartyPlayerJoined", left).Should().BeFalse();
    }

    [Test]
    public void TypeShapesDoNotSerializeNestedArrayContents()
    {
        PhotonEventDiagnosticsCollector.DescribeShape(new object[]
        {
            new byte[] { 1, 2, 3, 4 },
            new byte[] { 10, 11, 12, 13 }
        }).Should().Be("array[2]<byte[4]>");
        PhotonEventDiagnosticsCollector.DescribeShape("hello").Should().Be("string(5)");
        PhotonEventDiagnosticsCollector.DescribeShape(new int[] { 1, 1234, 55 })
            .Should().Be("array[3]<number>");
    }
    [Test]
    public void PackedRosterFromObservedCode230Matches()
    {
        var shape = new Dictionary<string, string>
        {
            ["0"] = "number", ["4"] = "byte[16]",
            ["8"] = "byte[80]", ["9"] = "array[5]<string>",
            ["252"] = "number"
        };
        PhotonEventDiagnosticsCollector.Matches("PartyJoined", shape).Should().BeTrue();
        shape["8"] = "byte[64]";
        PhotonEventDiagnosticsCollector.Matches("PartyJoined", shape).Should().BeFalse();
    }

    [Test]
    public void BroadUnrelatedShapesDoNotMatchLeaveOrDisband()
    {
        var shape = new Dictionary<string, string>
        {
            ["0"] = "number", ["1"] = "byte[16]",
            ["2"] = "number", ["252"] = "number"
        };
        PhotonEventDiagnosticsCollector.Matches("PartyPlayerLeft", shape).Should().BeFalse();
        shape["1"] = "number";
        PhotonEventDiagnosticsCollector.Matches("PartyDisbanded", shape).Should().BeFalse();
    }

}
