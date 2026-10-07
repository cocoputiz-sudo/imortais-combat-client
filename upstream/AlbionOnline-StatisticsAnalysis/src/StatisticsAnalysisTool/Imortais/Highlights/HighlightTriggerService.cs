#nullable enable
using System;
using System.Diagnostics;
using System.Threading;

namespace StatisticsAnalysisTool.Imortais.Highlights;

internal static class HighlightTriggerService
{
    private static Action<HighlightTrigger>? _sink;
    private static int _enabled;
    private static int _saveAbates=1;
    private static int _publishers;

    public static long QpcNow100Ns()
        =>(long)Math.Round(Stopwatch.GetTimestamp()*(double)TimeSpan.TicksPerSecond/Stopwatch.Frequency);

    public static void Configure(
        Action<HighlightTrigger> sink,
        bool enabled,
        bool saveAbates)
    {
        Volatile.Write(ref _saveAbates,saveAbates?1:0);
        Volatile.Write(ref _sink,sink);
        Volatile.Write(ref _enabled,enabled?1:0);
    }

    public static void UpdateOptions(bool saveAbates)
    {
        Volatile.Write(ref _saveAbates,saveAbates?1:0);
    }

    public static void Publish(
        HighlightTriggerKind kind,
        string victim,
        string killer,
        long qpc100Ns)
    {
        if(Volatile.Read(ref _enabled)==0)return;
        if(kind!=HighlightTriggerKind.Abate)return;
        if(Volatile.Read(ref _saveAbates)==0)return;

        Interlocked.Increment(ref _publishers);
        try
        {
            if(Volatile.Read(ref _enabled)==0)return;
            var sink=Volatile.Read(ref _sink);
            if(sink is null)return;
            sink(new HighlightTrigger(kind,victim??string.Empty,killer??string.Empty,qpc100Ns));
        }
        finally
        {
            Interlocked.Decrement(ref _publishers);
        }
    }

    public static void DisableAndWait()
    {
        Volatile.Write(ref _enabled,0);
        Volatile.Write(ref _sink,null);
        var spinner=new SpinWait();
        while(Volatile.Read(ref _publishers)!=0)spinner.SpinOnce();
    }
}
