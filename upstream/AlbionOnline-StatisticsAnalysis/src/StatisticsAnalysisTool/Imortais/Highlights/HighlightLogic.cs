#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace StatisticsAnalysisTool.Imortais.Highlights;

internal static class HighlightTimeline
{
    public readonly record struct AudioClipBounds(long Start100Ns,long End100Ns);

    public static long QpcToVideoTimestamp100Ns(long qpc100Ns,long videoEpochSystemTicks)
        =>checked(qpc100Ns-videoEpochSystemTicks);

    public static AudioClipBounds AudioClipFromVideoEpoch(
        long videoEpochSystemTicks,
        long sourceStartTimestamp100Ns,
        long requestedEndTimestamp100Ns)
    {
        var start=checked(videoEpochSystemTicks+sourceStartTimestamp100Ns);
        var end=checked(videoEpochSystemTicks+requestedEndTimestamp100Ns);
        if(end<=start)throw new ArgumentOutOfRangeException(nameof(requestedEndTimestamp100Ns));
        return new AudioClipBounds(start,end);
    }
}

internal sealed class TextureLeasePool
{
    private readonly object _gate=new();
    private readonly bool[] _leased;
    private int _next;

    public TextureLeasePool(int capacity)
    {
        if(capacity<=0)throw new ArgumentOutOfRangeException(nameof(capacity));
        _leased=new bool[capacity];
    }

    public int Capacity=>_leased.Length;
    public int InUseCount
    {
        get
        {
            lock(_gate)
            {
                var count=0;
                for(var i=0;i<_leased.Length;i++)if(_leased[i])count++;
                return count;
            }
        }
    }

    public bool TryAcquire(out int slot)
    {
        lock(_gate)
        {
            for(var offset=0;offset<_leased.Length;offset++)
            {
                var candidate=(_next+offset)%_leased.Length;
                if(_leased[candidate])continue;
                _leased[candidate]=true;
                _next=(candidate+1)%_leased.Length;
                slot=candidate;
                return true;
            }
            slot=-1;
            return false;
        }
    }

    public void Release(int slot)
    {
        lock(_gate)
        {
            if((uint)slot>=(uint)_leased.Length)throw new ArgumentOutOfRangeException(nameof(slot));
            if(!_leased[slot])throw new InvalidOperationException($"Texture slot {slot} não estava alugado.");
            _leased[slot]=false;
        }
    }
}


internal sealed class Nv12PoolStallWatchdog
{
    private readonly long _threshold100Ns;
    private long? _fullSince100Ns;

    public Nv12PoolStallWatchdog(TimeSpan threshold)
    {
        if(threshold<=TimeSpan.Zero)throw new ArgumentOutOfRangeException(nameof(threshold));
        _threshold100Ns=threshold.Ticks;
    }

    public bool Observe(bool poolFull,long now100Ns)
    {
        if(!poolFull)
        {
            _fullSince100Ns=null;
            return false;
        }

        _fullSince100Ns??=now100Ns;
        return now100Ns-_fullSince100Ns.Value>=_threshold100Ns;
    }
}


internal sealed class SlidingRateCounter
{
    private readonly object _gate=new();
    private readonly Queue<long> _events=new();
    private readonly long _windowStopwatchTicks;

    public SlidingRateCounter(TimeSpan window)
    {
        if(window<=TimeSpan.Zero)throw new ArgumentOutOfRangeException(nameof(window));
        _windowStopwatchTicks=Math.Max(1,(long)Math.Round(window.TotalSeconds*Stopwatch.Frequency));
    }

    public void Mark()
    {
        var now=Stopwatch.GetTimestamp();
        lock(_gate)
        {
            _events.Enqueue(now);
            Prune(now);
        }
    }

    public double SnapshotPerSecond()
    {
        var now=Stopwatch.GetTimestamp();
        lock(_gate)
        {
            Prune(now);
            if(_events.Count<2)return 0;
            var first=_events.Peek();
            long last=first;
            foreach(var value in _events)last=value;
            var seconds=(last-first)/(double)Stopwatch.Frequency;
            return seconds>0?(_events.Count-1)/seconds:0;
        }
    }

    private void Prune(long now)
    {
        var min=now-_windowStopwatchTicks;
        while(_events.Count>0&&_events.Peek()<min)_events.Dequeue();
    }
}

internal sealed class WgcIntervalWindow
{
    public readonly record struct Snapshot(double MinMs,double AverageMs,double MaxMs,int IntervalCount);

    private readonly object _gate=new();
    private readonly Queue<long> _timestamps=new();
    private readonly long _window100Ns;

    public WgcIntervalWindow(TimeSpan window)
    {
        if(window<=TimeSpan.Zero)throw new ArgumentOutOfRangeException(nameof(window));
        _window100Ns=window.Ticks;
    }

    public void Add(long timestamp100Ns)
    {
        lock(_gate)
        {
            _timestamps.Enqueue(timestamp100Ns);
            var min=timestamp100Ns-_window100Ns;
            while(_timestamps.Count>0&&_timestamps.Peek()<min)_timestamps.Dequeue();
        }
    }

    public Snapshot GetSnapshot()
    {
        lock(_gate)
        {
            if(_timestamps.Count<2)return new Snapshot(0,0,0,0);
            long? previous=null;
            long min=long.MaxValue,max=0,sum=0,count=0;
            foreach(var timestamp in _timestamps)
            {
                if(previous.HasValue)
                {
                    var delta=timestamp-previous.Value;
                    if(delta>0)
                    {
                        if(delta<min)min=delta;
                        if(delta>max)max=delta;
                        sum+=delta;
                        count++;
                    }
                }
                previous=timestamp;
            }
            if(count==0)return new Snapshot(0,0,0,0);
            return new Snapshot(
                min/(double)TimeSpan.TicksPerMillisecond,
                sum/(double)count/TimeSpan.TicksPerMillisecond,
                max/(double)TimeSpan.TicksPerMillisecond,
                checked((int)count));
        }
    }
}


internal enum HighlightTriggerKind
{
    Death,
    Abate
}

internal readonly record struct HighlightTrigger(
    HighlightTriggerKind Kind,
    string Victim,
    string Killer,
    long Qpc100Ns);

internal sealed class HighlightClipPlan
{
    public HighlightClipPlan(
        HighlightTrigger firstTrigger,
        TimeSpan preRoll,
        TimeSpan postRoll,
        TimeSpan maxDuration)
    {
        if(preRoll<TimeSpan.Zero)throw new ArgumentOutOfRangeException(nameof(preRoll));
        if(postRoll<TimeSpan.Zero)throw new ArgumentOutOfRangeException(nameof(postRoll));
        if(maxDuration<=TimeSpan.Zero)throw new ArgumentOutOfRangeException(nameof(maxDuration));

        FirstTrigger=firstTrigger;
        SelectedTrigger=firstTrigger;
        FirstEventQpc100Ns=firstTrigger.Qpc100Ns;
        LastEventQpc100Ns=firstTrigger.Qpc100Ns;
        StartQpc100Ns=Math.Max(0,firstTrigger.Qpc100Ns-preRoll.Ticks);
        HardEndQpc100Ns=checked(StartQpc100Ns+maxDuration.Ticks);
        EndQpc100Ns=Math.Min(
            checked(firstTrigger.Qpc100Ns+postRoll.Ticks),
            HardEndQpc100Ns);
        PostRoll100Ns=postRoll.Ticks;
        TriggerCount=1;
    }

    public HighlightTrigger FirstTrigger{get;}
    // A morte do próprio jogador prevalece sobre claps/abates na mesma janela.
    public HighlightTrigger SelectedTrigger{get;private set;}
    public bool MassAbate=>SelectedTrigger.Kind!=HighlightTriggerKind.Death && TriggerCount>1;
    public long FirstEventQpc100Ns{get;}
    public long LastEventQpc100Ns{get;private set;}
    public long StartQpc100Ns{get;}
    public long EndQpc100Ns{get;private set;}
    public long HardEndQpc100Ns{get;}
    public long PostRoll100Ns{get;}
    public int TriggerCount{get;private set;}
    public TimeSpan PlannedDuration=>TimeSpan.FromTicks(Math.Max(0,EndQpc100Ns-StartQpc100Ns));

    public bool CanCoalesce(HighlightTrigger trigger)
        =>trigger.Qpc100Ns>=LastEventQpc100Ns
          && trigger.Qpc100Ns<=EndQpc100Ns
          && trigger.Qpc100Ns<=HardEndQpc100Ns;

    public void Coalesce(HighlightTrigger trigger)
    {
        if(!CanCoalesce(trigger))throw new InvalidOperationException("Trigger fora da janela de coalescência.");
        LastEventQpc100Ns=trigger.Qpc100Ns;
        if(trigger.Kind==HighlightTriggerKind.Death)SelectedTrigger=trigger;
        EndQpc100Ns=Math.Min(
            checked(trigger.Qpc100Ns+PostRoll100Ns),
            HardEndQpc100Ns);
        TriggerCount++;
    }
}

internal sealed class HighlightClipCoalescer
{
    private readonly TimeSpan _preRoll;
    private readonly TimeSpan _postRoll;
    private readonly TimeSpan _maxDuration;
    private readonly List<HighlightClipPlan> _plans=new();

    public HighlightClipCoalescer(TimeSpan preRoll,TimeSpan postRoll,TimeSpan maxDuration)
    {
        _preRoll=preRoll;
        _postRoll=postRoll;
        _maxDuration=maxDuration;
    }

    public int Count=>_plans.Count;

    public bool Add(HighlightTrigger trigger)
    {
        if(_plans.Count>0)
        {
            var last=_plans[^1];
            if(last.CanCoalesce(trigger))
            {
                last.Coalesce(trigger);
                return true;
            }
        }

        _plans.Add(new HighlightClipPlan(trigger,_preRoll,_postRoll,_maxDuration));
        return false;
    }

    public bool TryDequeueReady(long nowQpc100Ns,out HighlightClipPlan? plan)
    {
        if(_plans.Count==0||_plans[0].EndQpc100Ns>nowQpc100Ns)
        {
            plan=null;
            return false;
        }

        plan=_plans[0];
        _plans.RemoveAt(0);
        return true;
    }

    public void Prepend(HighlightClipPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        _plans.Insert(0,plan);
    }

    public HighlightClipPlan[] DrainAll()
    {
        var result=_plans.ToArray();
        _plans.Clear();
        return result;
    }
}

internal readonly record struct HighlightQuotaFile(
    string Path,
    long Length,
    DateTime LastWriteUtc);

internal static class HighlightQuotaPlanner
{
    public static IReadOnlyList<string> SelectFilesToDelete(
        IReadOnlyList<HighlightQuotaFile> files,
        long quotaBytes,
        long reserveBytes,
        string? protectedPath=null)
    {
        if(quotaBytes<0)throw new ArgumentOutOfRangeException(nameof(quotaBytes));
        if(reserveBytes<0)throw new ArgumentOutOfRangeException(nameof(reserveBytes));

        long total=0;
        foreach(var file in files)total=checked(total+Math.Max(0,file.Length));
        var target=Math.Max(0,quotaBytes-reserveBytes);
        if(total<=target)return Array.Empty<string>();

        var ordered=new List<HighlightQuotaFile>(files);
        ordered.Sort(static (a,b)=>a.LastWriteUtc.CompareTo(b.LastWriteUtc));
        var deleted=new List<string>();
        foreach(var file in ordered)
        {
            if(total<=target)break;
            if(!string.IsNullOrWhiteSpace(protectedPath)
               && string.Equals(file.Path,protectedPath,StringComparison.OrdinalIgnoreCase))
                continue;
            deleted.Add(file.Path);
            total-=Math.Max(0,file.Length);
        }
        return deleted;
    }
}

internal static class HighlightFileNaming
{
    public static string SanitizeComponent(string? value,int maxLength=48)
    {
        if(maxLength<=0)throw new ArgumentOutOfRangeException(nameof(maxLength));
        var source=string.IsNullOrWhiteSpace(value)?"desconhecido":value.Trim();
        var chars=new char[Math.Min(source.Length,maxLength)];
        var count=0;
        foreach(var c in source)
        {
            if(count>=maxLength)break;
            var invalid=c<32||char.IsWhiteSpace(c)||c is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*';
            chars[count++]=invalid?'_':c;
        }
        var result=new string(chars,0,count).Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(result)?"desconhecido":result;
    }

    public static string BuildFileName(HighlightTrigger first,int triggerCount,DateTime localTime)
    {
        var suffix=triggerCount>1?$"_x{triggerCount}":string.Empty;
        var prefix=localTime.ToString("yyyy-MM-dd_HH-mm-ss");
        return first.Kind==HighlightTriggerKind.Death
            ? $"{prefix}_MORTE_por_{SanitizeComponent(first.Killer)}{suffix}.mp4"
            : triggerCount>1
                ? $"{prefix}_ABATES_EM_MASSA{suffix}.mp4"
                : $"{prefix}_ABATE_{SanitizeComponent(first.Victim)}{suffix}.mp4";
    }
}


internal sealed class BoundedAdmissionCounter
{
    private readonly int _capacity;
    private int _count;
    private int _peak;

    public BoundedAdmissionCounter(int capacity)
    {
        if(capacity<=0)throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity=capacity;
    }

    public int Capacity=>_capacity;
    public int Count=>Volatile.Read(ref _count);
    public int Peak=>Volatile.Read(ref _peak);

    public bool TryEnter()
    {
        while(true)
        {
            var current=Volatile.Read(ref _count);
            if(current>=_capacity)return false;
            if(Interlocked.CompareExchange(ref _count,current+1,current)!=current)continue;
            UpdatePeak(current+1);
            return true;
        }
    }

    public void Exit()
    {
        var value=Interlocked.Decrement(ref _count);
        if(value>=0)return;
        Interlocked.Exchange(ref _count,0);
        throw new InvalidOperationException("BoundedAdmissionCounter liberado além das admissões.");
    }

    private void UpdatePeak(int value)
    {
        while(true)
        {
            var peak=Volatile.Read(ref _peak);
            if(value<=peak)return;
            if(Interlocked.CompareExchange(ref _peak,value,peak)==peak)return;
        }
    }
}

internal sealed class HighlightTriggerDeduplicator
{
    private readonly long _window100Ns;
    private readonly Dictionary<string,long> _lastByKey=new(StringComparer.OrdinalIgnoreCase);

    public HighlightTriggerDeduplicator(TimeSpan window)
    {
        if(window<TimeSpan.Zero)throw new ArgumentOutOfRangeException(nameof(window));
        _window100Ns=window.Ticks;
    }

    public bool IsDuplicate(HighlightTrigger trigger)
    {
        var key=$"{trigger.Kind}|{trigger.Victim}|{trigger.Killer}";
        if(_lastByKey.TryGetValue(key,out var previous)
           && trigger.Qpc100Ns>=previous
           && trigger.Qpc100Ns-previous<=_window100Ns)
            return true;

        _lastByKey[key]=trigger.Qpc100Ns;
        if(_lastByKey.Count>512)Prune(trigger.Qpc100Ns);
        return false;
    }

    private void Prune(long now100Ns)
    {
        var threshold=now100Ns-Math.Max(_window100Ns,TimeSpan.FromSeconds(5).Ticks);
        var stale=new List<string>();
        foreach(var pair in _lastByKey)
            if(pair.Value<threshold)stale.Add(pair.Key);
        foreach(var key in stale)_lastByKey.Remove(key);
    }
}
