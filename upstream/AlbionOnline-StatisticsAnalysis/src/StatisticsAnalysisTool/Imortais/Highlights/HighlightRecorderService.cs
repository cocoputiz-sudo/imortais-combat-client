#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StatisticsAnalysisTool.Imortais.Highlights;

internal sealed class HighlightRecorderService : IDisposable
{
    private static readonly TimeSpan AutoPreRoll=TimeSpan.FromSeconds(45);
    private static readonly TimeSpan AutoPostRoll=TimeSpan.FromSeconds(15);
    private static readonly TimeSpan AutoMaxDuration=TimeSpan.FromSeconds(120);
    private const int MaxTriggerInbox=512;
    private const int MaxDetachedSnapshots=2;

    private readonly object _gate=new();
    private readonly ConcurrentQueue<HighlightTrigger> _triggerInbox=new();
    private readonly ConcurrentQueue<AutoSaveWorkItem> _saveQueue=new();
    private readonly BoundedAdmissionCounter _triggerInboxAdmission=new(MaxTriggerInbox);
    private readonly SemaphoreSlim _detachedSnapshotSlots=new(MaxDetachedSnapshots,MaxDetachedSnapshots);
    private readonly HighlightTriggerDeduplicator _triggerDeduplicator=new(TimeSpan.FromSeconds(1));
    private readonly HighlightClipCoalescer _coalescer=new(AutoPreRoll,AutoPostRoll,AutoMaxDuration);
    private readonly HighlightStorageManager _storage=new();
    private readonly SemaphoreSlim _saveSerial=new(1,1);
    private readonly Queue<double> _recentSaveDurationsMs=new();

    private WindowsHighlightCaptureSession? _capture;
    private RecorderState _state=RecorderState.Disabled;
    private string? _message;
    private bool _disposed;
    private bool _requestedEnabled;
    private bool _saveAbates=true;
    private bool _saveDeaths=true;
    private string? _lastCaptureError;
    private string? _lastCaptureErrorStage;
    private string? _lastSavedPath;
    private string? _lastAutoSavedPath;
    private DateTime? _lastAutoSavedUtc;
    private string? _lastAutoSaveError;
    private Task? _savePumpTask;
    private int _savePumpActive;
    private long _triggersReceived;
    private long _deathTriggers;
    private long _abateTriggers;
    private long _deathClipsSaved;
    private long _massClipsSaved;
    private long _abateClipsSaved;
    private long _triggersCoalesced;
    private long _clipsSaved;
    private long _clipsFailed;
    private long _triggersDroppedPressure;
    private long _triggersDeduplicated;
    private long _snapshotPressureDeferrals;
    private long _detachedSnapshotBytes;
    private long _detachedSnapshotPeakBytes;
    private double _lastSnapshotMs;
    private long _lastSaveFrameBusyDelta;
    private double _lastSaveWgcMaxBeforeMs;
    private double _lastSaveWgcMaxAfterMs;

    public HighlightRecorderService()
    {
        try{_storage.Initialize();}
        catch(Exception ex){_lastAutoSaveError=ex.Message;}
    }

    public event EventHandler? StatusChanged;

    public RecorderSnapshot Snapshot()
    {
        WindowsHighlightCaptureSession.CaptureStatus? capture;
        int plannerCount;
        string recentSaveTimes;
        lock(_gate)
        {
            capture=_capture?.SnapshotStatus();
            if(capture?.LastError is not null)
            {
                _lastCaptureError=capture.LastError;
                _lastCaptureErrorStage=capture.LastErrorStage;
            }
            if(capture?.LastSavedPath is not null)_lastSavedPath=capture.LastSavedPath;
            plannerCount=_coalescer.Count;
            recentSaveTimes=_recentSaveDurationsMs.Count==0
                ?"nenhum"
                :string.Join(", ",_recentSaveDurationsMs.Select(ms=>$"{ms:0} ms"));
        }

        var queueCurrent=_triggerInbox.Count+plannerCount+_saveQueue.Count+(Volatile.Read(ref _savePumpActive)!=0?1:0);

        return new RecorderSnapshot(
            _state,
            _message,
            _capture?.EncoderName,
            _capture?.OutputWidth,
            _capture?.OutputHeight,
            _capture?.Fps,
            capture?.BufferedDuration??TimeSpan.Zero,
            capture?.BufferBytes??0,
            capture?.FramesAccepted??0,
            capture?.FramesSkipped??0,
            capture?.EncodedSamples??0,
            capture?.CleanPoints??0,
            capture?.TargetGopFrames,
            capture?.EffectiveGopFrames??0,
            capture?.Nv12SurfacesInUse??0,
            capture?.Nv12PoolCapacity??GpuFrameConverter.OutputPoolSize,
            capture?.WgcReceived??0,
            capture?.PacerRejected??0,
            capture?.FrameBusyCallbacks??0,
            capture?.ContentSizeChanged??0,
            capture?.Nv12PoolFull??0,
            capture?.EncoderSubmitted??0,
            capture?.WgcSurfaceErrors??0,
            capture?.BgraToNv12Errors??0,
            capture?.H264WriteErrors??0,
            capture?.FramePipelineErrors??0,
            capture?.ActiveSnapshotOperations??0,
            capture?.WgcFps10s??0,
            capture?.SubmitFps10s??0,
            capture?.EncodedFps10s??0,
            capture?.WgcIntervalMinMs??0,
            capture?.WgcIntervalAverageMs??0,
            capture?.WgcIntervalMaxMs??0,
            capture?.LastSavedPath??_lastSavedPath,
            capture?.LastError??_lastCaptureError,
            capture?.LastErrorStage??_lastCaptureErrorStage,
            capture?.AudioStatus??"Áudio do Albion inativo.",
            capture?.AudioPackets??0,
            capture?.AudioBytes??0,
            capture?.AudioBufferedDuration??TimeSpan.Zero,
            capture?.AudioGapsDetected??0,
            capture?.AudioError,
            _capture?.BorderPropertyAvailable??false,
            _capture?.BorderlessRequestAccepted??false,
            _capture?.BorderWarning,
            Interlocked.Read(ref _triggersReceived),
            Interlocked.Read(ref _triggersCoalesced),
            Interlocked.Read(ref _clipsSaved),
            Interlocked.Read(ref _clipsFailed),
            Interlocked.Read(ref _triggersDroppedPressure),
            Interlocked.Read(ref _triggersDeduplicated),
            Interlocked.Read(ref _snapshotPressureDeferrals),
            _triggerInboxAdmission.Count,
            _triggerInboxAdmission.Peak,
            Interlocked.Read(ref _detachedSnapshotBytes),
            Interlocked.Read(ref _detachedSnapshotPeakBytes),
            queueCurrent,
            _storage.UsedBytes,
            _storage.QuotaBytes,
            recentSaveTimes,
            _lastSnapshotMs,
            _lastSaveFrameBusyDelta,
            _lastSaveWgcMaxBeforeMs,
            _lastSaveWgcMaxAfterMs,
            _lastAutoSavedPath,
            _lastAutoSavedUtc,
            _lastAutoSaveError,
            Interlocked.Read(ref _deathTriggers),
            Interlocked.Read(ref _abateTriggers),
            Interlocked.Read(ref _deathClipsSaved),
            Interlocked.Read(ref _massClipsSaved),
            Interlocked.Read(ref _abateClipsSaved));
    }

    public void Enable(int fps,bool saveAbates=true,bool saveDeaths=true)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        lock(_gate)
        {
            _requestedEnabled=true;
            _saveAbates=saveAbates;
            _saveDeaths=saveDeaths;
            if(_capture!=null)
            {
                HighlightTriggerService.UpdateOptions(saveAbates,saveDeaths);
                return;
            }
            _state=RecorderState.WaitingForAlbion;
            _message="Aguardando a janela do Albion.";
        }
        RaiseChanged();
        TryStartForCurrentGame(fps);
    }

    public void UpdateTriggerOptions(bool saveAbates,bool saveDeaths)
    {
        lock(_gate){_saveAbates=saveAbates;_saveDeaths=saveDeaths;}
        HighlightTriggerService.UpdateOptions(saveAbates,saveDeaths);
        RaiseChanged();
    }

    public void OnAlbionStarted(int fps,bool saveAbates,bool saveDeaths)
    {
        if(_disposed)return;
        lock(_gate)
        {
            if(!_requestedEnabled)return;
            _saveAbates=saveAbates;
            _saveDeaths=saveDeaths;
        }
        TryStartForCurrentGame(fps);
    }

    public void Tick(int fps)
    {
        if(_disposed)return;

        WindowsHighlightCaptureSession? capture;
        RecorderState state;
        bool requested;
        lock(_gate)
        {
            capture=_capture;
            state=_state;
            requested=_requestedEnabled;
        }
        if(!requested)return;

        if(state==RecorderState.WaitingForAlbion)
        {
            TryStartForCurrentGame(fps);
            return;
        }

        if(state==RecorderState.Running&&capture?.RestartRequested==true)
        {
            var reason=capture.RestartReason??"A sessão de highlights pediu reinicialização.";
            StopCapture(RecorderState.WaitingForAlbion,reason+" Recriando captura automaticamente…");
            TryStartForCurrentGame(fps);
            return;
        }

        if(state==RecorderState.Running&&capture is not null)
            ProcessAutomaticTriggers(capture);
    }

    public void OnAlbionStopped()
    {
        if(_disposed)return;
        lock(_gate){if(!_requestedEnabled)return;}
        StopCapture(RecorderState.WaitingForAlbion,"Albion fechado. Aguardando o jogo abrir novamente.");
    }

    public void Disable()
    {
        if(_disposed)return;
        lock(_gate)_requestedEnabled=false;
        StopCapture(RecorderState.Disabled,"Gravação em segundo plano desligada.");
    }

    public async Task<string> SaveTestReplayAsync()
    {
        WindowsHighlightCaptureSession capture;
        lock(_gate)
        {
            if(_state!=RecorderState.Running||_capture==null)
                throw new InvalidOperationException(_message??"Highlights não estão gravando.");
            capture=_capture;
        }

        await _saveSerial.WaitAsync().ConfigureAwait(false);
        try
        {
            _storage.PrepareForSave();
            var path=_storage.CreateManualPath();
            var saved=await capture.SaveTestReplayAsync(path).ConfigureAwait(false);
            _storage.CompleteSave(saved);
            lock(_gate)_lastSavedPath=saved;
            RaiseChanged();
            return saved;
        }
        finally
        {
            _saveSerial.Release();
        }
    }

    private void OnTriggerPublished(HighlightTrigger trigger)
    {
        Interlocked.Increment(ref _triggersReceived);
        if(trigger.Kind==HighlightTriggerKind.Death)Interlocked.Increment(ref _deathTriggers);
        else if(trigger.Kind==HighlightTriggerKind.Abate)Interlocked.Increment(ref _abateTriggers);
        if(!_triggerInboxAdmission.TryEnter())
        {
            Interlocked.Increment(ref _triggersDroppedPressure);
            return;
        }

        try{_triggerInbox.Enqueue(trigger);}
        catch
        {
            _triggerInboxAdmission.Exit();
            throw;
        }
    }

    private void ProcessAutomaticTriggers(WindowsHighlightCaptureSession capture)
    {
        DrainTriggerInbox();

        while(true)
        {
            HighlightClipPlan? plan;
            lock(_gate)
            {
                if(!_coalescer.TryDequeueReady(HighlightTriggerService.QpcNow100Ns(),out plan))
                    break;
            }

            if(plan is null)break;
            if(TryMaterialize(capture,plan,false))continue;

            lock(_gate)_coalescer.Prepend(plan);
            break;
        }
    }

    private void DrainTriggerInbox()
    {
        while(_triggerInbox.TryDequeue(out var trigger))
        {
            _triggerInboxAdmission.Exit();

            if(_triggerDeduplicator.IsDuplicate(trigger))
            {
                Interlocked.Increment(ref _triggersDeduplicated);
                continue;
            }

            bool coalesced;
            lock(_gate)coalesced=_coalescer.Add(trigger);
            if(coalesced)Interlocked.Increment(ref _triggersCoalesced);
        }
    }

    private bool TryMaterialize(
        WindowsHighlightCaptureSession capture,
        HighlightClipPlan plan,
        bool forceAvailableEnd)
    {
        var slotAcquired=forceAvailableEnd
            ? WaitForDetachedSnapshotSlot()
            : _detachedSnapshotSlots.Wait(0);

        if(!slotAcquired)
        {
            Interlocked.Increment(ref _snapshotPressureDeferrals);
            return false;
        }

        WindowsHighlightCaptureSession.CaptureStatus before;
        try{before=capture.SnapshotStatus();}
        catch
        {
            _detachedSnapshotSlots.Release();
            return false;
        }

        var sw=Stopwatch.StartNew();
        WindowsHighlightCaptureSession.DetachedReplaySnapshot? snapshot=null;
        try
        {
            snapshot=capture.CreateAutomaticReplaySnapshot(plan,forceAvailableEnd);
        }
        catch(Exception ex)
        {
            _lastAutoSaveError=ex.Message;
            Interlocked.Increment(ref _clipsFailed);
            _detachedSnapshotSlots.Release();
            return true;
        }
        finally
        {
            sw.Stop();
            _lastSnapshotMs=sw.Elapsed.TotalMilliseconds;
        }

        if(snapshot is null)
        {
            _detachedSnapshotSlots.Release();
            return false;
        }

        var snapshotBytes=snapshot.EstimatedBytes;
        var currentBytes=Interlocked.Add(ref _detachedSnapshotBytes,snapshotBytes);
        UpdateDetachedSnapshotPeak(currentBytes);

        _lastSaveFrameBusyDelta=0;
        _lastSaveWgcMaxBeforeMs=before.WgcIntervalMaxMs;
        _lastSaveWgcMaxAfterMs=before.WgcIntervalMaxMs;

        var nowQpc=HighlightTriggerService.QpcNow100Ns();
        var age=Math.Max(0,nowQpc-plan.FirstEventQpc100Ns);
        var firstEventLocal=DateTime.Now-TimeSpan.FromTicks(age);

        try
        {
            _saveQueue.Enqueue(new AutoSaveWorkItem(
                plan,
                snapshot,
                firstEventLocal,
                capture,
                before.FrameBusyCallbacks,
                before.WgcIntervalMaxMs,
                _lastSnapshotMs,
                snapshotBytes));
            snapshot=null;
            EnsureSavePump();
            return true;
        }
        finally
        {
            if(snapshot is not null)
            {
                snapshot.Dispose();
                Interlocked.Add(ref _detachedSnapshotBytes,-snapshotBytes);
                _detachedSnapshotSlots.Release();
            }
        }
    }

    private bool WaitForDetachedSnapshotSlot()
    {
        try
        {
            _detachedSnapshotSlots.Wait();
            return true;
        }
        catch(ObjectDisposedException)
        {
            return false;
        }
    }

    private void UpdateDetachedSnapshotPeak(long current)
    {
        while(true)
        {
            var peak=Interlocked.Read(ref _detachedSnapshotPeakBytes);
            if(current<=peak)return;
            if(Interlocked.CompareExchange(ref _detachedSnapshotPeakBytes,current,peak)==peak)return;
        }
    }

    private void TryStartForCurrentGame(int fps)
    {
        IntPtr hwnd=IntPtr.Zero;
        var processId=0;
        foreach(var process in Process.GetProcessesByName("Albion-Online"))
        {
            try
            {
                if(process.MainWindowHandle!=IntPtr.Zero)
                {
                    hwnd=process.MainWindowHandle;
                    processId=process.Id;
                    break;
                }
            }
            finally{process.Dispose();}
        }

        if(hwnd==IntPtr.Zero||processId<=0)
        {
            lock(_gate)
            {
                if(_requestedEnabled&&!_disposed&&_capture is null)
                {
                    _state=RecorderState.WaitingForAlbion;
                    _message="Albion detectado; aguardando a janela principal ficar disponível.";
                }
            }
            return;
        }

        bool saveAbates;
        bool saveDeaths;
        lock(_gate)
        {
            if(_capture!=null||_disposed||!_requestedEnabled||_state==RecorderState.Starting)return;
            _state=RecorderState.Starting;
            _message="Inicializando captura e encoder H.264 de hardware…";
            saveAbates=_saveAbates;
            saveDeaths=_saveDeaths;
        }
        RaiseChanged();

        try
        {
            var bitrate=fps==30?6_000_000:8_000_000;
            var capture=WindowsHighlightCaptureSession.Start(hwnd,processId,fps,bitrate);
            lock(_gate)
            {
                if(_disposed)
                {
                    capture.Dispose();
                    return;
                }
                _capture=capture;
                _state=RecorderState.Running;
                _message=capture.BorderWarning??"Replay buffer ativo.";
            }
            HighlightTriggerService.Configure(OnTriggerPublished,true,saveAbates,saveDeaths);
        }
        catch(HardwareEncoderUnavailableException ex)
        {
            lock(_gate)
            {
                _state=RecorderState.Unavailable;
                _message="HIGHLIGHTS INDISPONÍVEIS · "+ex.Message;
            }
        }
        catch(Exception ex)
        {
            lock(_gate)
            {
                _state=RecorderState.Unavailable;
                _message="HIGHLIGHTS INDISPONÍVEIS · "+ex.Message;
            }
        }
        RaiseChanged();
    }

    private void StopCapture(RecorderState next,string message)
    {
        HighlightTriggerService.DisableAndWait();

        WindowsHighlightCaptureSession? capture;
        lock(_gate)capture=_capture;

        if(capture is not null)
        {
            DrainTriggerInbox();

            HighlightClipPlan[] pending;
            lock(_gate)pending=_coalescer.DrainAll();
            foreach(var plan in pending)
            {
                if(!TryMaterialize(capture,plan,true))
                {
                    Interlocked.Increment(ref _clipsFailed);
                    _lastAutoSaveError="Não havia vídeo/keyframe suficiente para salvar um highlight pendente antes de encerrar a sessão.";
                }
            }
        }

        lock(_gate)
        {
            if(capture!=null)
            {
                var status=capture.SnapshotStatus();
                if(status.LastError is not null)
                {
                    _lastCaptureError=status.LastError;
                    _lastCaptureErrorStage=status.LastErrorStage;
                }
                if(status.LastSavedPath is not null)_lastSavedPath=status.LastSavedPath;
            }
            _capture=null;
            _state=next;
            _message=message;
        }

        try{capture?.Dispose();}catch{}
        RaiseChanged();
    }

    private void EnsureSavePump()
    {
        if(Interlocked.CompareExchange(ref _savePumpActive,1,0)!=0)return;
        var task=Task.Run(ProcessAutoSaveQueueAsync);
        lock(_gate)_savePumpTask=task;
    }

    private async Task ProcessAutoSaveQueueAsync()
    {
        try
        {
            while(_saveQueue.TryDequeue(out var item))
            {
                await _saveSerial.WaitAsync().ConfigureAwait(false);
                var sw=Stopwatch.StartNew();
                string? saved=null;
                try
                {
                    _storage.PrepareForSave();
                    var path=_storage.CreateAutomaticPath(item.Plan,item.FirstEventLocalTime);
                    saved=Mp4PassthroughWriter.Write(
                        path,
                        item.Snapshot.Video,
                        item.Snapshot.OutputType,
                        item.Snapshot.Audio);
                    _storage.CompleteSave(saved);

                    Interlocked.Increment(ref _clipsSaved);
                    if(item.Plan.SelectedTrigger.Kind==HighlightTriggerKind.Death)
                        Interlocked.Increment(ref _deathClipsSaved);
                    else if(item.Plan.MassAbate)
                        Interlocked.Increment(ref _massClipsSaved);
                    else
                        Interlocked.Increment(ref _abateClipsSaved);
                    lock(_gate)
                    {
                        _lastSavedPath=saved;
                        _lastAutoSavedPath=saved;
                        _lastAutoSavedUtc=DateTime.UtcNow;
                        _lastAutoSaveError=null;
                    }
                }
                catch(Exception ex)
                {
                    Interlocked.Increment(ref _clipsFailed);
                    lock(_gate)_lastAutoSaveError=ex.Message;
                }
                finally
                {
                    sw.Stop();
                    RecordSaveDuration(sw.Elapsed.TotalMilliseconds);
                    UpdateSaveImpact(item);
                    item.Snapshot.Dispose();
                    Interlocked.Add(ref _detachedSnapshotBytes,-item.SnapshotBytes);
                    _detachedSnapshotSlots.Release();
                    _saveSerial.Release();
                    RaiseChanged();
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _savePumpActive,0);
            if(!_saveQueue.IsEmpty)EnsureSavePump();
        }
    }

    private void UpdateSaveImpact(AutoSaveWorkItem item)
    {
        WindowsHighlightCaptureSession? active;
        lock(_gate)active=_capture;
        if(!ReferenceEquals(active,item.SourceCapture))return;

        try
        {
            var after=active!.SnapshotStatus();
            _lastSaveFrameBusyDelta=Math.Max(0,after.FrameBusyCallbacks-item.FrameBusyBefore);
            _lastSaveWgcMaxBeforeMs=item.WgcMaxBeforeMs;
            _lastSaveWgcMaxAfterMs=after.WgcIntervalMaxMs;
        }
        catch{}
    }

    private void RecordSaveDuration(double milliseconds)
    {
        lock(_gate)
        {
            _recentSaveDurationsMs.Enqueue(milliseconds);
            while(_recentSaveDurationsMs.Count>10)_recentSaveDurationsMs.Dequeue();
        }
    }

    private void WaitForAutoSaves()
    {
        while(true)
        {
            Task? task;
            lock(_gate)task=_savePumpTask;
            if(task is null&&_saveQueue.IsEmpty&&Volatile.Read(ref _savePumpActive)==0)return;
            try{task?.GetAwaiter().GetResult();}catch{}
            if(_saveQueue.IsEmpty&&Volatile.Read(ref _savePumpActive)==0)return;
        }
    }

    private void RaiseChanged()
    {
        try{StatusChanged?.Invoke(this,EventArgs.Empty);}catch{}
    }

    public void Dispose()
    {
        if(_disposed)return;
        lock(_gate)_requestedEnabled=false;
        StopCapture(RecorderState.Disabled,"Gravação em segundo plano desligada.");
        _disposed=true;
        WaitForAutoSaves();
        _saveSerial.Dispose();
        _detachedSnapshotSlots.Dispose();
    }

    private sealed record AutoSaveWorkItem(
        HighlightClipPlan Plan,
        WindowsHighlightCaptureSession.DetachedReplaySnapshot Snapshot,
        DateTime FirstEventLocalTime,
        WindowsHighlightCaptureSession SourceCapture,
        long FrameBusyBefore,
        double WgcMaxBeforeMs,
        double SnapshotMilliseconds,
        long SnapshotBytes);

    internal enum RecorderState
    {
        Disabled,
        WaitingForAlbion,
        Starting,
        Running,
        Unavailable
    }

    internal sealed record RecorderSnapshot(
        RecorderState State,
        string? Message,
        string? EncoderName,
        int? Width,
        int? Height,
        int? Fps,
        TimeSpan BufferedDuration,
        long BufferBytes,
        long FramesAccepted,
        long FramesSkipped,
        long EncodedSamples,
        long CleanPoints,
        int? TargetGopFrames,
        long EffectiveGopFrames,
        int Nv12SurfacesInUse,
        int Nv12PoolCapacity,
        long WgcReceived,
        long PacerRejected,
        long FrameBusyCallbacks,
        long ContentSizeChanged,
        long Nv12PoolFull,
        long EncoderSubmitted,
        long WgcSurfaceErrors,
        long BgraToNv12Errors,
        long H264WriteErrors,
        long FramePipelineErrors,
        int ActiveSnapshotOperations,
        double WgcFps10s,
        double SubmitFps10s,
        double EncodedFps10s,
        double WgcIntervalMinMs,
        double WgcIntervalAverageMs,
        double WgcIntervalMaxMs,
        string? LastSavedPath,
        string? LastCaptureError,
        string? LastCaptureErrorStage,
        string AudioStatus,
        long AudioPackets,
        long AudioBytes,
        TimeSpan AudioBufferedDuration,
        long AudioGapsDetected,
        string? AudioError,
        bool BorderPropertyAvailable,
        bool BorderlessRequestAccepted,
        string? BorderWarning,
        long AutoTriggersReceived,
        long AutoTriggersCoalesced,
        long AutoClipsSaved,
        long AutoClipsFailed,
        long AutoTriggersDroppedPressure,
        long AutoTriggersDeduplicated,
        long SnapshotPressureDeferrals,
        int TriggerInboxCurrent,
        int TriggerInboxPeak,
        long DetachedSnapshotBytes,
        long DetachedSnapshotPeakBytes,
        int AutoQueueCurrent,
        long HighlightFolderBytes,
        long HighlightQuotaBytes,
        string RecentSaveDurations,
        double LastSnapshotMilliseconds,
        long LastSaveFrameBusyDelta,
        double LastSaveWgcMaxBeforeMs,
        double LastSaveWgcMaxAfterMs,
        string? LastAutoSavedPath,
        DateTime? LastAutoSavedUtc,
        string? LastAutoSaveError,
        long AutoDeathTriggers,
        long AutoAbateTriggers,
        long AutoDeathClipsSaved,
        long AutoMassClipsSaved,
        long AutoAbateClipsSaved);
}
