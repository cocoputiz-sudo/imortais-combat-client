#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace StatisticsAnalysisTool.Imortais.Highlights;

public sealed unsafe class EncodedSampleRing : IDisposable
{
    public readonly record struct SampleInfo(long Timestamp100Ns,long Duration100Ns,int Length,bool IsCleanPoint);
    public readonly record struct SnapshotSample(int Offset,int Length,long Timestamp100Ns,long Duration100Ns,bool IsCleanPoint);

    public sealed class ClipSnapshot : IDisposable
    {
        private IntPtr _buffer;
        private bool _disposed;
        internal ClipSnapshot(IntPtr buffer,int length,SnapshotSample[] samples,long sourceStartTimestamp100Ns)
        {_buffer=buffer;Length=length;Samples=samples;SourceStartTimestamp100Ns=sourceStartTimestamp100Ns;}
        public int Length { get; }
        public SnapshotSample[] Samples { get; }
        public long SourceStartTimestamp100Ns { get; }
        public int Count => Samples.Length;
        public ReadOnlySpan<byte> GetBytes(int index)
        {
            ObjectDisposedException.ThrowIf(_disposed,this);
            var s=Samples[index];
            return new ReadOnlySpan<byte>((void*)(_buffer+s.Offset),s.Length);
        }
        public void Dispose(){if(_disposed)return;_disposed=true;if(_buffer!=IntPtr.Zero){NativeMemory.Free((void*)_buffer);_buffer=IntPtr.Zero;}}
    }

    private readonly struct Entry
    {
        public Entry(long offset,int length,long ts,long dur,bool clean){Offset=offset;Length=length;Timestamp=ts;Duration=dur;Clean=clean;}
        public long Offset{get;} public int Length{get;} public long Timestamp{get;} public long Duration{get;} public bool Clean{get;}
    }

    private readonly object _gate=new();
    private readonly int _capacity;
    private readonly long _maxAge;
    private readonly Entry[] _entries;
    private IntPtr _buffer;
    private int _head,_count;
    private long _write;
    private bool _disposed;

    public EncodedSampleRing(TimeSpan maxDuration,int byteCapacity,int metadataCapacity=12000)
    {
        if(maxDuration<=TimeSpan.Zero)throw new ArgumentOutOfRangeException(nameof(maxDuration));
        if(byteCapacity<=0)throw new ArgumentOutOfRangeException(nameof(byteCapacity));
        if(metadataCapacity<=0)throw new ArgumentOutOfRangeException(nameof(metadataCapacity));
        _maxAge=maxDuration.Ticks;_capacity=byteCapacity;_entries=new Entry[metadataCapacity];
        _buffer=(IntPtr)NativeMemory.Alloc((nuint)byteCapacity);
        if(_buffer==IntPtr.Zero)throw new OutOfMemoryException();
    }

    public int Count{get{lock(_gate)return _count;}}
    public long BytesUsed{get{lock(_gate)return _count==0?0:_write-At(0).Offset;}}
    public TimeSpan BufferedDuration{get{lock(_gate){if(_count<2)return TimeSpan.Zero;var a=At(0);var b=At(_count-1);return TimeSpan.FromTicks(Math.Max(0,b.Timestamp+Math.Max(0,b.Duration)-a.Timestamp));}}}

    public bool TryWrite(IntPtr source,int length,long timestamp100Ns,long duration100Ns,bool cleanPoint)
    {
        if(source==IntPtr.Zero)throw new ArgumentNullException(nameof(source));
        if(length<=0||length>_capacity)return false;
        lock(_gate)
        {
            ThrowIfDisposed();
            EvictExpired(timestamp100Ns);
            while(_count>0&&((_write+length)-At(0).Offset>_capacity||_count>=_entries.Length))RemoveOldest();
            if(_count>=_entries.Length)return false;
            CopyIn((byte*)source,length,_write);
            _entries[(_head+_count)%_entries.Length]=new Entry(_write,length,timestamp100Ns,duration100Ns,cleanPoint);
            _count++;_write+=length;return true;
        }
    }

    public bool TryWrite(ReadOnlySpan<byte> source,long timestamp100Ns,long duration100Ns,bool cleanPoint)
    {
        if(source.IsEmpty||source.Length>_capacity)return false;
        fixed(byte* p=source)return TryWrite((IntPtr)p,source.Length,timestamp100Ns,duration100Ns,cleanPoint);
    }

    public ClipSnapshot? CreateSnapshot(long requestedStart100Ns,long requestedEnd100Ns=long.MaxValue)
    {
        lock(_gate)
        {
            ThrowIfDisposed();if(_count==0)return null;
            var start=-1;
            for(var i=0;i<_count;i++){var e=At(i);if(e.Timestamp>requestedStart100Ns)break;if(e.Clean)start=i;}
            if(start<0)return null;
            int n=0,total=0;
            for(var i=start;i<_count;i++){var e=At(i);if(e.Timestamp>requestedEnd100Ns)break;n++;total=checked(total+e.Length);}
            if(n==0)return null;
            var dst=(IntPtr)NativeMemory.Alloc((nuint)total);if(dst==IntPtr.Zero)throw new OutOfMemoryException();
            try
            {
                var samples=new SnapshotSample[n];var baseTs=At(start).Timestamp;var off=0;
                for(var i=0;i<n;i++){var e=At(start+i);CopyOut(e.Offset,(byte*)dst+off,e.Length);samples[i]=new(off,e.Length,Math.Max(0,e.Timestamp-baseTs),e.Duration,e.Clean);off+=e.Length;}
                return new ClipSnapshot(dst,total,samples,baseTs);
            }
            catch{NativeMemory.Free((void*)dst);throw;}
        }
    }

    public bool TryGetBounds(out long oldestTimestamp100Ns, out long newestTimestamp100Ns)
    {
        lock(_gate)
        {
            ThrowIfDisposed();
            if(_count==0)
            {
                oldestTimestamp100Ns=0;
                newestTimestamp100Ns=0;
                return false;
            }
            var first=At(0);
            var last=At(_count-1);
            oldestTimestamp100Ns=first.Timestamp;
            newestTimestamp100Ns=last.Timestamp+Math.Max(0,last.Duration);
            return true;
        }
    }

    public IReadOnlyList<SampleInfo> Describe()
    {
        lock(_gate){ThrowIfDisposed();var a=new SampleInfo[_count];for(var i=0;i<_count;i++){var e=At(i);a[i]=new(e.Timestamp,e.Duration,e.Length,e.Clean);}return a;}
    }

    public void Clear(){lock(_gate){ThrowIfDisposed();_head=0;_count=0;_write=0;}}
    public void Dispose(){lock(_gate){if(_disposed)return;_disposed=true;_count=0;if(_buffer!=IntPtr.Zero){NativeMemory.Free((void*)_buffer);_buffer=IntPtr.Zero;}}}
    private Entry At(int i)=>_entries[(_head+i)%_entries.Length];
    private void EvictExpired(long newest){var min=newest-_maxAge;while(_count>0){var e=At(0);if(e.Timestamp+Math.Max(0,e.Duration)>=min)break;RemoveOldest();}}
    private void RemoveOldest(){if(_count==0)return;_entries[_head]=default;_head=(_head+1)%_entries.Length;_count--;if(_count==0){_head=0;_write=0;}}
    private void CopyIn(byte* src,int len,long abs){var p=(int)(abs%_capacity);var a=Math.Min(len,_capacity-p);Buffer.MemoryCopy(src,(byte*)_buffer+p,a,a);var b=len-a;if(b>0)Buffer.MemoryCopy(src+a,(byte*)_buffer,b,b);}
    private void CopyOut(long abs,byte* dst,int len){var p=(int)(abs%_capacity);var a=Math.Min(len,_capacity-p);Buffer.MemoryCopy((byte*)_buffer+p,dst,a,a);var b=len-a;if(b>0)Buffer.MemoryCopy((byte*)_buffer,dst+a,b,b);}
    private void ThrowIfDisposed()=>ObjectDisposedException.ThrowIf(_disposed,this);
}
