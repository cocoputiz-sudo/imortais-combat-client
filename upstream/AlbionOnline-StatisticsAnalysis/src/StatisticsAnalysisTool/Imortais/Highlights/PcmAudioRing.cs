#nullable enable
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace StatisticsAnalysisTool.Imortais.Highlights;

internal sealed unsafe class PcmAudioRing:IDisposable
{
    internal const long TimestampJitterTolerance100Ns=30*TimeSpan.TicksPerMillisecond;

    public readonly record struct SnapshotSample(int Offset,int Length,long Timestamp100Ns,long Duration100Ns);

    public sealed class AudioSnapshot:IDisposable
    {
        private IntPtr _buffer;
        private bool _disposed;

        internal AudioSnapshot(IntPtr buffer,int length,SnapshotSample[] samples,int gapCount)
        {
            _buffer=buffer;
            Length=length;
            Samples=samples;
            GapCount=gapCount;
        }

        public int Length{get;}
        public SnapshotSample[] Samples{get;}
        public int Count=>Samples.Length;
        public int GapCount{get;}

        public ReadOnlySpan<byte> GetBytes(int index)
        {
            ObjectDisposedException.ThrowIf(_disposed,this);
            var s=Samples[index];
            return new ReadOnlySpan<byte>((void*)(_buffer+s.Offset),s.Length);
        }

        public void Dispose()
        {
            if(_disposed)return;
            _disposed=true;
            if(_buffer!=IntPtr.Zero)
            {
                NativeMemory.Free((void*)_buffer);
                _buffer=IntPtr.Zero;
            }
        }
    }

    private readonly struct Entry
    {
        public Entry(long offset,int length,long timestamp,long duration)
        {
            Offset=offset;
            Length=length;
            Timestamp=timestamp;
            Duration=duration;
        }

        public long Offset{get;}
        public int Length{get;}
        public long Timestamp{get;}
        public long Duration{get;}
    }

    private readonly object _gate=new();
    private readonly int _capacity;
    private readonly long _maxAge100Ns;
    private readonly int _blockAlign;
    private readonly int _sampleRate;
    private readonly Entry[] _entries;
    private IntPtr _buffer;
    private int _head;
    private int _count;
    private long _write;
    private long _gapsDetected;
    private bool _disposed;

    public PcmAudioRing(
        TimeSpan maxDuration,
        int byteCapacity,
        int sampleRate=48_000,
        int channels=2,
        int bitsPerSample=16,
        int metadataCapacity=32_768)
    {
        if(maxDuration<=TimeSpan.Zero)throw new ArgumentOutOfRangeException(nameof(maxDuration));
        if(byteCapacity<=0)throw new ArgumentOutOfRangeException(nameof(byteCapacity));
        if(sampleRate<=0||channels<=0||bitsPerSample<=0||bitsPerSample%8!=0)throw new ArgumentOutOfRangeException();

        _maxAge100Ns=maxDuration.Ticks;
        _capacity=byteCapacity;
        _sampleRate=sampleRate;
        _blockAlign=channels*(bitsPerSample/8);
        _entries=new Entry[metadataCapacity];
        _buffer=(IntPtr)NativeMemory.Alloc((nuint)byteCapacity);
        if(_buffer==IntPtr.Zero)throw new OutOfMemoryException();
    }

    public int Count{get{lock(_gate)return _count;}}
    public long BytesUsed{get{lock(_gate)return _count==0?0:_write-At(0).Offset;}}
    public long GapsDetected=>Interlocked.Read(ref _gapsDetected);

    public TimeSpan BufferedDuration
    {
        get
        {
            lock(_gate)
            {
                if(_count<2)return TimeSpan.Zero;
                var first=At(0);
                var last=At(_count-1);
                return TimeSpan.FromTicks(Math.Max(0,last.Timestamp+last.Duration-first.Timestamp));
            }
        }
    }

    public bool TryWrite(ReadOnlySpan<byte> pcm,long timestamp100Ns)
    {
        if(pcm.IsEmpty||pcm.Length>_capacity||pcm.Length%_blockAlign!=0)return false;

        var frames=pcm.Length/_blockAlign;
        var duration=FramesToTicks(frames);

        fixed(byte* p=pcm)
        {
            lock(_gate)
            {
                ThrowIfDisposed();
                EvictExpired(timestamp100Ns);

                var logicalTimestamp=timestamp100Ns;
                if(_count>0)
                {
                    var previous=At(_count-1);
                    var expected=previous.Timestamp+previous.Duration;
                    var delta=timestamp100Ns-expected;

                    // QPC/WASAPI não chega em uma grade perfeita. Até 30 ms de desvio
                    // mantemos os pacotes estritamente contíguos e ignoramos o jitter.
                    if(Math.Abs(delta)<=TimestampJitterTolerance100Ns)
                    {
                        logicalTimestamp=expected;
                    }
                    else if(delta>TimestampJitterTolerance100Ns)
                    {
                        // Só um salto positivo real cria silêncio e conta como lacuna.
                        logicalTimestamp=timestamp100Ns;
                        Interlocked.Increment(ref _gapsDetected);
                    }
                    else
                    {
                        // Um salto grande para trás nunca deve fazer a timeline regredir.
                        logicalTimestamp=expected;
                    }
                }

                while(_count>0&&((_write+pcm.Length)-At(0).Offset>_capacity||_count>=_entries.Length))
                    RemoveOldest();
                if(_count>=_entries.Length)return false;

                CopyIn(p,pcm.Length,_write);
                _entries[(_head+_count)%_entries.Length]=new Entry(
                    _write,pcm.Length,logicalTimestamp,duration);
                _count++;
                _write+=pcm.Length;
                return true;
            }
        }
    }

    public AudioSnapshot? CreateSnapshot(long start100Ns,long end100Ns,long rebaseOrigin100Ns)
    {
        lock(_gate)
        {
            ThrowIfDisposed();
            if(_count==0||end100Ns<=start100Ns)return null;

            var first=-1;
            for(var i=0;i<_count;i++)
            {
                var e=At(i);
                if(e.Timestamp+e.Duration<=start100Ns)continue;
                if(e.Timestamp>=end100Ns)break;
                first=i;
                break;
            }
            if(first<0)return null;

            var totalFrames=TicksToFramesCeiling(end100Ns-start100Ns);
            if(totalFrames<=0)return null;

            var totalBytes=checked((int)(totalFrames*_blockAlign));
            var dst=(IntPtr)NativeMemory.Alloc((nuint)totalBytes);
            if(dst==IntPtr.Zero)throw new OutOfMemoryException();

            try
            {
                // O snapshot representa a janela inteira do clipe. Regiões sem áudio
                // permanecem zero e portanto viram silêncio PCM real antes do AAC.
                new Span<byte>((void*)dst,totalBytes).Clear();

                var firstEntry=At(first);
                var firstStartFrame=TicksToFramesRounded(firstEntry.Timestamp-start100Ns);
                var sourceSkipFrames=Math.Max(0,-firstStartFrame);
                var cursorFrame=Math.Max(0,firstStartFrame);
                var expectedTimestamp=firstEntry.Timestamp;
                var gapCount=0;

                for(var i=first;i<_count;i++)
                {
                    var e=At(i);
                    if(e.Timestamp>=end100Ns)break;

                    if(i>first)
                    {
                        var deviation=e.Timestamp-expectedTimestamp;
                        if(deviation>TimestampJitterTolerance100Ns)
                        {
                            cursorFrame=checked(cursorFrame+TicksToFramesRounded(deviation));
                            gapCount++;
                        }
                        // Dentro da tolerância não reposicionamos o pacote: ele é
                        // concatenado exatamente após o anterior.
                    }

                    var packetFrames=e.Length/_blockAlign;
                    var skip=i==first?sourceSkipFrames:0;
                    if(skip<packetFrames&&cursorFrame<totalFrames)
                    {
                        var copyFrames=Math.Min(packetFrames-skip,totalFrames-cursorFrame);
                        if(copyFrames>0)
                        {
                            var sourceAbsolute=e.Offset+checked(skip*_blockAlign);
                            var destination=(byte*)dst+checked((int)(cursorFrame*_blockAlign));
                            CopyOut(sourceAbsolute,destination,checked((int)(copyFrames*_blockAlign)));
                            cursorFrame+=copyFrames;
                        }
                    }

                    expectedTimestamp=e.Timestamp+e.Duration;
                }

                const int chunkFrames=1024;
                var sampleCount=checked((int)((totalFrames+chunkFrames-1)/chunkFrames));
                var samples=new SnapshotSample[sampleCount];
                long frame=0;
                for(var i=0;i<sampleCount;i++)
                {
                    var frames=Math.Min(chunkFrames,totalFrames-frame);
                    var offset=checked((int)(frame*_blockAlign));
                    var length=checked((int)(frames*_blockAlign));
                    samples[i]=new SnapshotSample(
                        offset,
                        length,
                        Math.Max(0,FramesToTicks(frame)+(start100Ns-rebaseOrigin100Ns)),
                        Math.Max(1,FramesToTicks(frames)));
                    frame+=frames;
                }

                return new AudioSnapshot(dst,totalBytes,samples,gapCount);
            }
            catch
            {
                NativeMemory.Free((void*)dst);
                throw;
            }
        }
    }

    public void Clear()
    {
        lock(_gate)
        {
            ThrowIfDisposed();
            _head=0;
            _count=0;
            _write=0;
        }
    }

    public void Dispose()
    {
        lock(_gate)
        {
            if(_disposed)return;
            _disposed=true;
            _count=0;
            if(_buffer!=IntPtr.Zero)
            {
                NativeMemory.Free((void*)_buffer);
                _buffer=IntPtr.Zero;
            }
        }
    }

    private Entry At(int i)=>_entries[(_head+i)%_entries.Length];

    private void EvictExpired(long newest)
    {
        var min=newest-_maxAge100Ns;
        while(_count>0)
        {
            var e=At(0);
            if(e.Timestamp+e.Duration>=min)break;
            RemoveOldest();
        }
    }

    private void RemoveOldest()
    {
        if(_count==0)return;
        _entries[_head]=default;
        _head=(_head+1)%_entries.Length;
        _count--;
        if(_count==0)
        {
            _head=0;
            _write=0;
        }
    }

    private long FramesToTicks(long frames)=>checked(frames*TimeSpan.TicksPerSecond/_sampleRate);

    private long TicksToFramesCeiling(long ticks)
    {
        if(ticks<=0)return 0;
        return checked((ticks*_sampleRate+TimeSpan.TicksPerSecond-1)/TimeSpan.TicksPerSecond);
    }

    private long TicksToFramesRounded(long ticks)
    {
        if(ticks>=0)
            return checked((ticks*_sampleRate+TimeSpan.TicksPerSecond/2)/TimeSpan.TicksPerSecond);
        return -checked(((-ticks)*_sampleRate+TimeSpan.TicksPerSecond/2)/TimeSpan.TicksPerSecond);
    }

    private void CopyIn(byte* src,int len,long abs)
    {
        var p=(int)(abs%_capacity);
        var a=Math.Min(len,_capacity-p);
        Buffer.MemoryCopy(src,(byte*)_buffer+p,a,a);
        var b=len-a;
        if(b>0)Buffer.MemoryCopy(src+a,(byte*)_buffer,b,b);
    }

    private void CopyOut(long abs,byte* dst,int len)
    {
        var p=(int)(abs%_capacity);
        var a=Math.Min(len,_capacity-p);
        Buffer.MemoryCopy((byte*)_buffer+p,dst,a,a);
        var b=len-a;
        if(b>0)Buffer.MemoryCopy((byte*)_buffer,dst+a,b,b);
    }

    private void ThrowIfDisposed()=>ObjectDisposedException.ThrowIf(_disposed,this);
}
