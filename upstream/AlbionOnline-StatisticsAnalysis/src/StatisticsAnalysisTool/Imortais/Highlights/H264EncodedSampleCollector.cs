using SharpGen.Runtime;
using System;
using System.Threading;
using Vortice.MediaFoundation;

namespace StatisticsAnalysisTool.Imortais.Highlights;

internal sealed class H264EncodedSampleCollector : CallbackBase, IMFSampleGrabberSinkCallback
{
    private readonly EncodedSampleRing _ring;
    private long _encodedSamples,_encodedBytes,_cleanPoints,_sampleOrdinal,_effectiveGopFrames;
    private long _lastCleanOrdinal=-1;

    public H264EncodedSampleCollector(EncodedSampleRing ring){_ring=ring??throw new ArgumentNullException(nameof(ring));}
    internal event Action<long>? SampleProcessed;
    internal event Action<long>? SampleEncoded;
    public long EncodedSamples=>Interlocked.Read(ref _encodedSamples);
    public long EncodedBytes=>Interlocked.Read(ref _encodedBytes);
    public long CleanPoints=>Interlocked.Read(ref _cleanPoints);
    public long EffectiveGopFrames=>Interlocked.Read(ref _effectiveGopFrames);

    public void OnProcessSample(Guid majorMediaType,int sampleFlags,long sampleTime,long sampleDuration,Span<byte> sampleBuffer)
    {
        try
        {
            if(sampleBuffer.IsEmpty)return;
            var clean=ContainsIdr(sampleBuffer);
            if(_ring.TryWrite(sampleBuffer,sampleTime,sampleDuration,clean))
            {
                var ordinal=Interlocked.Increment(ref _sampleOrdinal)-1;
                Interlocked.Increment(ref _encodedSamples);
                Interlocked.Add(ref _encodedBytes,sampleBuffer.Length);
                if(clean)
                {
                    Interlocked.Increment(ref _cleanPoints);
                    var previous=Interlocked.Exchange(ref _lastCleanOrdinal,ordinal);
                    if(previous>=0)Interlocked.Exchange(ref _effectiveGopFrames,ordinal-previous);
                }
                try{SampleEncoded?.Invoke(sampleTime);}catch{}
            }
        }
        finally
        {
            try{SampleProcessed?.Invoke(sampleTime);}catch{}
        }
    }

    public void OnSetPresentationClock(IMFPresentationClock presentationClock){}
    public void OnClockStart(long systemTime,long clockStartOffset){}
    public void OnClockStop(long systemTime){}
    public void OnClockPause(long systemTime){}
    public void OnClockRestart(long systemTime){}
    public void OnClockSetRate(long systemTime,float rate){}
    public void OnShutdown(){}

    internal static bool ContainsIdr(ReadOnlySpan<byte> data)
    {
        if(data.Length<5)return false;
        for(var i=0;i+4<data.Length;i++)
        {
            var start=-1;
            if(data[i]==0&&data[i+1]==0&&data[i+2]==1)start=i+3;
            else if(i+5<data.Length&&data[i]==0&&data[i+1]==0&&data[i+2]==0&&data[i+3]==1)start=i+4;
            if(start>=0&&start<data.Length&&(data[start]&0x1F)==5)return true;
        }
        var offset=0;
        while(offset+5<=data.Length)
        {
            var length=((uint)data[offset]<<24)|((uint)data[offset+1]<<16)|((uint)data[offset+2]<<8)|data[offset+3];
            offset+=4;
            if(length==0||length>(uint)(data.Length-offset))break;
            if((data[offset]&0x1F)==5)return true;
            offset+=(int)length;
        }
        return false;
    }
}
