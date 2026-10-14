using NUnit.Framework;
using StatisticsAnalysisTool.Network;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace StatisticsAnalysisTool.UnitTests.Network;

[TestFixture]
public sealed class PhotonEventObserverTests
{
    [Test]
    public void TwoRegisteredObservers_ReceiveSameEventAndParameters()
    {
        var builder = ReceiverBuilder.Create();
        var firstCalls = 0;
        var secondCalls = 0;
        short firstCode = -1;
        short secondCode = -1;
        Dictionary<byte, object>? firstParameters = null;
        Dictionary<byte, object>? secondParameters = null;

        builder.AddEventObserver((code, parameters) =>
        {
            firstCalls++;
            firstCode = code;
            firstParameters = parameters;
        });
        builder.AddEventObserver((code, parameters) =>
        {
            secondCalls++;
            secondCode = code;
            secondParameters = parameters;
        });

        var eventParameters = SampleEvent();
        DispatchEvent(builder, eventParameters);

        Assert.Multiple(() =>
        {
            Assert.That(firstCalls, Is.EqualTo(1), "diagnostic observer must not be overwritten");
            Assert.That(secondCalls, Is.EqualTo(1), "skill observer must also receive the event");
            Assert.That(firstCode, Is.EqualTo((short)77));
            Assert.That(secondCode, Is.EqualTo((short)77));
            Assert.That(firstParameters, Is.SameAs(eventParameters));
            Assert.That(secondParameters, Is.SameAs(eventParameters));
        });
    }

    [Test]
    public void ThrowingObserver_DoesNotBlockNextObserverOrNormalEventHandler()
    {
        var builder = ReceiverBuilder.Create();
        var secondCalls = 0;
        var handledCalls = 0;

        builder.AddEventObserver((_, _) => throw new InvalidOperationException("first observer failed"));
        builder.AddEventObserver((code, parameters) =>
        {
            Assert.That(code, Is.EqualTo((short)77));
            Assert.That(parameters, Is.Not.Null);
            secondCalls++;
        });
        builder.AddEventHandler(new CountingHandler(() => handledCalls++));

        Assert.DoesNotThrow(() => DispatchEvent(builder, SampleEvent()));
        Assert.Multiple(() =>
        {
            Assert.That(secondCalls, Is.EqualTo(1), "later observers must run despite exception");
            Assert.That(handledCalls, Is.EqualTo(1), "normal Photon handlers must still run");
        });
    }

    private static Dictionary<byte, object> SampleEvent() =>
        new()
        {
            [252] = (short)77, // AlbionParser.ParseEventCode
            [0] = 12345
        };

    private static void DispatchEvent(ReceiverBuilder builder, Dictionary<byte, object> parameters)
    {
        var receiver = builder.Build();
        // AlbionParser is internal and OnEvent is protected. Reflection allows exercising
        // the actual parser dispatch path without constructing a synthetic Photon datagram.
        var onEvent = receiver.GetType().GetMethod("OnEvent",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(onEvent, Is.Not.Null);
        onEvent!.Invoke(receiver, new object[] { (byte)0, parameters });
    }

    private sealed class CountingHandler(Action onCall) : PacketHandler<EventPacket>(77)
    {
        protected override Task OnHandleAsync(EventPacket packet)
        {
            onCall();
            return Task.CompletedTask;
        }
    }
}
