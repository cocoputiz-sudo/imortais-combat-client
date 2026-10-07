#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.MediaFoundation;
using Windows.Foundation.Metadata;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace StatisticsAnalysisTool.Imortais.Highlights;

internal sealed class WindowsHighlightCaptureSession : IDisposable
{
    public const int RingCapacityBytes = 192 * 1024 * 1024;
    public static readonly TimeSpan RingDuration = TimeSpan.FromSeconds(150);
    public static readonly TimeSpan TestReplayDuration = TimeSpan.FromSeconds(120);
    public static readonly TimeSpan TestReplayPostRoll = TimeSpan.FromSeconds(15);

    private readonly object _stateGate = new();
    private readonly object _lifetimeGate = new();
    private readonly ManualResetEventSlim _frameIdle = new(true);
    private readonly ManualResetEventSlim _snapshotIdle = new(true);
    private readonly EncodedSampleRing _ring;
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly IDirect3DDevice _winRtDevice;
    private readonly GraphicsCaptureItem _captureItem;
    private readonly GpuFrameConverter _converter;
    private readonly HardwareH264SinkEncoder _encoder;
    private readonly IMFMediaType _snapshotOutputTypeTemplate;
    private readonly Direct3D11CaptureFramePool _framePool;
    private readonly GraphicsCaptureSession _captureSession;
    private readonly int _fps;
    private readonly FramePacer _framePacer;
    private readonly Nv12PoolStallWatchdog _nv12PoolWatchdog=new(TimeSpan.FromSeconds(2));
    private readonly SlidingRateCounter _wgcRate10s=new(TimeSpan.FromSeconds(10));
    private readonly SlidingRateCounter _submitRate10s=new(TimeSpan.FromSeconds(10));
    private readonly SlidingRateCounter _encodedRate10s=new(TimeSpan.FromSeconds(10));
    private readonly WgcIntervalWindow _wgcIntervals10s=new(TimeSpan.FromSeconds(10));
    private readonly PcmAudioRing? _audioRing;
    private readonly AlbionProcessAudioCapture? _audioCapture;
    private readonly string _audioStatus;
    private readonly string? _audioStartupError;
    private long? _videoEpochSystemTicks;
    private int _frameBusy;
    private int _activeSnapshotOperations;
    private int _restartRequested;
    private bool _disposed;
    private bool _mediaFoundationStarted;
    private long _framesAccepted;
    private long _framesSkipped;
    private long _wgcReceived;
    private long _pacerRejected;
    private long _frameBusyCallbacks;
    private long _contentSizeChanged;
    private long _nv12PoolFull;
    private long _encoderSubmitted;
    private long _wgcSurfaceErrors;
    private long _bgraToNv12Errors;
    private long _h264WriteErrors;
    private long _framePipelineErrors;
    private string? _lastError;
    private string? _lastErrorStage;
    private string? _lastSavedPath;
    private string? _restartReason;

    private WindowsHighlightCaptureSession(
        EncodedSampleRing ring,
        ID3D11Device device,
        ID3D11DeviceContext context,
        IDirect3DDevice winRtDevice,
        GraphicsCaptureItem captureItem,
        GpuFrameConverter converter,
        HardwareH264SinkEncoder encoder,
        Direct3D11CaptureFramePool framePool,
        GraphicsCaptureSession captureSession,
        int fps,
        PcmAudioRing? audioRing,
        AlbionProcessAudioCapture? audioCapture,
        string audioStatus,
        string? audioStartupError,
        bool borderPropertyAvailable,
        bool borderlessRequestAccepted,
        string? borderWarning)
    {
        _ring=ring;
        _device=device;
        _context=context;
        _winRtDevice=winRtDevice;
        _captureItem=captureItem;
        _converter=converter;
        _encoder=encoder;
        _snapshotOutputTypeTemplate=encoder.SnapshotOutputType();
        _framePool=framePool;
        _captureSession=captureSession;
        _fps=fps;
        _framePacer=new FramePacer(fps);
        _audioRing=audioRing;
        _audioCapture=audioCapture;
        _audioStatus=audioStatus;
        _audioStartupError=audioStartupError;
        BorderPropertyAvailable=borderPropertyAvailable;
        BorderlessRequestAccepted=borderlessRequestAccepted;
        BorderWarning=borderWarning;
        _mediaFoundationStarted=true;
        _encoder.SampleEncoded+=Encoder_OnSampleEncoded;
        _framePool.FrameArrived+=FramePool_OnFrameArrived;
    }

    public int InputWidth => _converter.InputWidth;
    public int InputHeight => _converter.InputHeight;
    public int OutputWidth => _converter.OutputWidth;
    public int OutputHeight => _converter.OutputHeight;
    public int Fps => _fps;
    public string EncoderName => _encoder.EncoderName;
    public int TargetGopFrames => _encoder.TargetGopFrames;
    public bool RestartRequested => Volatile.Read(ref _restartRequested)!=0;
    public string? RestartReason{get{lock(_stateGate)return _restartReason;}}
    public bool BorderPropertyAvailable { get; }
    public bool BorderlessRequestAccepted { get; }
    public string? BorderWarning { get; }

    public static WindowsHighlightCaptureSession Start(IntPtr hwnd,int processId,int fps=60,int bitrate=8_000_000)
    {
        if(hwnd==IntPtr.Zero)throw new InvalidOperationException("Janela do Albion não encontrada.");
        if(processId<=0)throw new InvalidOperationException("Processo do Albion não identificado para captura de áudio.");
        if(fps is not (30 or 60))throw new ArgumentOutOfRangeException(nameof(fps));

        EncodedSampleRing? ring=null;
        ID3D11Device? device=null;
        ID3D11DeviceContext? context=null;
        IDirect3DDevice? winRtDevice=null;
        GraphicsCaptureItem? item=null;
        GpuFrameConverter? converter=null;
        HardwareH264SinkEncoder? encoder=null;
        PcmAudioRing? audioRing=null;
        AlbionProcessAudioCapture? audioCapture=null;
        var audioStatus="Áudio do Albion indisponível.";
        string? audioStartupError=null;
        Direct3D11CaptureFramePool? framePool=null;
        GraphicsCaptureSession? session=null;
        var mfStarted=false;

        try
        {
            MediaFactory.MFStartup(true).CheckError();
            mfStarted=true;

            var flags=DeviceCreationFlags.BgraSupport|DeviceCreationFlags.VideoSupport;
            D3D11.D3D11CreateDevice(
                IntPtr.Zero,
                DriverType.Hardware,
                flags,
                new[]{FeatureLevel.Level_11_1,FeatureLevel.Level_11_0},
                out device,
                out context).CheckError();

            using(var multithread=context.QueryInterface<ID3D11Multithread>())
            {
                multithread.SetMultithreadProtected(true);
            }

            winRtDevice=WindowsGraphicsCaptureInterop.CreateWinRtDevice(device);
            item=WindowsGraphicsCaptureInterop.CreateItemForWindow(hwnd);
            var inputWidth=Math.Max(2,item.Size.Width);
            var inputHeight=Math.Max(2,item.Size.Height);
            var (outputWidth,outputHeight)=FitInside1080p(inputWidth,inputHeight);

            ring=new EncodedSampleRing(RingDuration,RingCapacityBytes);
            converter=new GpuFrameConverter(device,context,inputWidth,inputHeight,outputWidth,outputHeight,fps);
            encoder=HardwareH264SinkEncoder.Create(device,ring,outputWidth,outputHeight,fps,bitrate);

            // Áudio é opcional para o replay, mas nunca cai para loopback global:
            // tentamos exclusivamente o process-loopback do Albion.
            try
            {
                audioRing=new PcmAudioRing(
                    RingDuration,
                    AlbionProcessAudioCapture.RingCapacityBytes,
                    AlbionProcessAudioCapture.SampleRate,
                    AlbionProcessAudioCapture.Channels,
                    AlbionProcessAudioCapture.BitsPerSample);
                audioCapture=AlbionProcessAudioCapture.StartAsync(processId,audioRing).GetAwaiter().GetResult();
                audioStatus="Áudio do Albion ativo · 48 kHz estéreo.";
            }
            catch(Exception ex)
            {
                try{audioCapture?.Dispose();}catch{}
                audioCapture=null;
                try{audioRing?.Dispose();}catch{}
                audioRing=null;
                audioStartupError=$"{ex.GetType().Name} · HRESULT 0x{ex.HResult:X8} · {ex.Message}";
                audioStatus="Áudio do Albion indisponível · vídeo continua normalmente.";
            }

            framePool=Direct3D11CaptureFramePool.CreateFreeThreaded(
                winRtDevice,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                3,
                item.Size);
            session=framePool.CreateCaptureSession(item);

            if(ApiInformation.IsPropertyPresent(
                   "Windows.Graphics.Capture.GraphicsCaptureSession",
                   "IsCursorCaptureEnabled"))
            {
                try{session.IsCursorCaptureEnabled=false;}catch{}
            }

            var borderAvailable=ApiInformation.IsPropertyPresent(
                "Windows.Graphics.Capture.GraphicsCaptureSession",
                "IsBorderRequired");
            var borderAccepted=false;
            string? borderWarning=null;
            if(borderAvailable)
            {
                borderAccepted=WindowsGraphicsCaptureInterop.TryDisableCaptureBorder(session,out var warning);
                if(!borderAccepted)borderWarning=warning;
            }
            else
            {
                borderWarning="Esta versão do Windows usa a borda de captura do sistema.";
            }

            var result=new WindowsHighlightCaptureSession(
                ring,device,context,winRtDevice,item,converter,encoder,framePool,session,
                fps,audioRing,audioCapture,audioStatus,audioStartupError,
                borderAvailable,borderAccepted,borderWarning);
            session.StartCapture();
            return result;
        }
        catch
        {
            try{session?.Dispose();}catch{}
            try{framePool?.Dispose();}catch{}
            try{audioCapture?.Dispose();}catch{}
            try{audioRing?.Dispose();}catch{}
            try{encoder?.Dispose();}catch{}
            try{converter?.Dispose();}catch{}
            try{winRtDevice?.Dispose();}catch{}
            try{context?.Dispose();}catch{}
            try{device?.Dispose();}catch{}
            try{ring?.Dispose();}catch{}
            if(mfStarted){try{MediaFactory.MFShutdown();}catch{}}
            throw;
        }
    }

    public CaptureStatus SnapshotStatus()
    {
        var intervals=_wgcIntervals10s.GetSnapshot();
        var wgcFps=_wgcRate10s.SnapshotPerSecond();
        var submitFps=_submitRate10s.SnapshotPerSecond();
        var encodedFps=_encodedRate10s.SnapshotPerSecond();

        lock(_stateGate)
        {
            return new CaptureStatus(
                Interlocked.Read(ref _framesAccepted),
                Interlocked.Read(ref _framesSkipped),
                _ring.BufferedDuration,
                _ring.BytesUsed,
                _encoder.EncodedSamples,
                _encoder.CleanPoints,
                _encoder.TargetGopFrames,
                _encoder.EffectiveGopFrames,
                _converter.OutputSurfacesInUse,
                GpuFrameConverter.OutputPoolSize,
                Interlocked.Read(ref _wgcReceived),
                Interlocked.Read(ref _pacerRejected),
                Interlocked.Read(ref _frameBusyCallbacks),
                Interlocked.Read(ref _contentSizeChanged),
                Interlocked.Read(ref _nv12PoolFull),
                Interlocked.Read(ref _encoderSubmitted),
                Interlocked.Read(ref _wgcSurfaceErrors),
                Interlocked.Read(ref _bgraToNv12Errors),
                Interlocked.Read(ref _h264WriteErrors),
                Interlocked.Read(ref _framePipelineErrors),
                Volatile.Read(ref _activeSnapshotOperations),
                wgcFps,
                submitFps,
                encodedFps,
                intervals.MinMs,
                intervals.AverageMs,
                intervals.MaxMs,
                _lastError,
                _lastErrorStage,
                _lastSavedPath,
                _audioStatus,
                _audioCapture?.Packets??0,
                _audioRing?.BytesUsed??0,
                _audioRing?.BufferedDuration??TimeSpan.Zero,
                _audioRing?.GapsDetected??0,
                _audioCapture?.LastError??_audioStartupError);
        }
    }

    public DetachedReplaySnapshot? CreateAutomaticReplaySnapshot(HighlightClipPlan plan,bool forceAvailableEnd)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if(!TryEnterSnapshotOperation(requireVideoEpoch:true,out var videoEpochSystemTicks))
            return null;

        try
        {
            if(!_ring.TryGetBounds(out var oldestNow,out var newestNow))return null;

            var requestedStart=HighlightTimeline.QpcToVideoTimestamp100Ns(
                plan.StartQpc100Ns,
                videoEpochSystemTicks);
            var requestedEnd=HighlightTimeline.QpcToVideoTimestamp100Ns(
                plan.EndQpc100Ns,
                videoEpochSystemTicks);

            requestedStart=Math.Max(0,requestedStart);
            if(requestedEnd<=requestedStart)return null;
            if(!forceAvailableEnd&&newestNow<requestedEnd)return null;

            requestedStart=Math.Max(oldestNow,requestedStart);
            requestedEnd=Math.Min(newestNow,requestedEnd);
            if(requestedEnd<=requestedStart)return null;

            EncodedSampleRing.ClipSnapshot? video=null;
            IMFMediaType? outputType=null;
            PcmAudioRing.AudioSnapshot? audioSnapshot=null;
            try
            {
                // Os rings têm locks próprios. A sessão fica "pinada" pelo contador de
                // snapshot, portanto o Dispose não libera ring/MF durante esta cópia,
                // sem bloquear a entrada do callback WGC no _lifetimeGate.
                video=_ring.CreateSnapshot(requestedStart,requestedEnd);
                if(video is null)return null;

                outputType=HighlightMediaTypes.Clone(_snapshotOutputTypeTemplate);
                if(_audioRing is not null)
                {
                    var audioBounds=HighlightTimeline.AudioClipFromVideoEpoch(
                        videoEpochSystemTicks,
                        video.SourceStartTimestamp100Ns,
                        requestedEnd);
                    audioSnapshot=_audioRing.CreateSnapshot(
                        audioBounds.Start100Ns,
                        audioBounds.End100Ns,
                        audioBounds.Start100Ns);
                }

                var detached=new DetachedReplaySnapshot(video,outputType,audioSnapshot);
                video=null;
                outputType=null;
                audioSnapshot=null;
                return detached;
            }
            finally
            {
                audioSnapshot?.Dispose();
                outputType?.Dispose();
                video?.Dispose();
            }
        }
        finally
        {
            ExitSnapshotOperation();
        }
    }

    public async Task<string> SaveTestReplayAsync(string? requestedPath=null)
    {
        long triggerTimestamp;
        lock(_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed,this);
            if(!_ring.TryGetBounds(out _,out triggerTimestamp))
                throw new InvalidOperationException("O buffer ainda está vazio. Jogue por alguns segundos antes de salvar.");
        }

        await Task.Delay(TestReplayPostRoll).ConfigureAwait(false);

        if(!TryEnterSnapshotOperation(requireVideoEpoch:false,out var videoEpochSystemTicks))
            throw new InvalidOperationException("A sessão foi encerrada antes de concluir o replay.");

        EncodedSampleRing.ClipSnapshot? snapshot=null;
        IMFMediaType? outputType=null;
        PcmAudioRing.AudioSnapshot? audioSnapshot=null;
        try
        {
            if(!_ring.TryGetBounds(out var oldestNow,out var newestNow))
                throw new InvalidOperationException("O buffer foi encerrado antes de concluir o replay.");

            var requestedStart=Math.Max(oldestNow,triggerTimestamp-TestReplayDuration.Ticks);
            var requestedEnd=Math.Min(newestNow,triggerTimestamp+TestReplayPostRoll.Ticks);

            snapshot=_ring.CreateSnapshot(requestedStart,requestedEnd);
            if(snapshot==null)
                throw new InvalidOperationException("Ainda não existe um keyframe (CleanPoint) válido para iniciar o replay.");

            outputType=HighlightMediaTypes.Clone(_snapshotOutputTypeTemplate);

            if(_audioRing is not null&&videoEpochSystemTicks>0)
            {
                var audioBounds=HighlightTimeline.AudioClipFromVideoEpoch(
                    videoEpochSystemTicks,
                    snapshot.SourceStartTimestamp100Ns,
                    requestedEnd);
                audioSnapshot=_audioRing.CreateSnapshot(
                    audioBounds.Start100Ns,
                    audioBounds.End100Ns,
                    audioBounds.Start100Ns);
            }

            var path=requestedPath;
            if(string.IsNullOrWhiteSpace(path))
            {
                var videos=Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
                var folder=Path.Combine(videos,"IMORTAIS Highlights");
                path=Path.Combine(folder,$"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_TESTE.mp4");
            }

            var saved=await Task.Run(()=>Mp4PassthroughWriter.Write(path,snapshot,outputType,audioSnapshot)).ConfigureAwait(false);
            lock(_stateGate)_lastSavedPath=saved;
            return saved;
        }
        catch(Exception ex)
        {
            lock(_stateGate)_lastError=ex.Message;
            throw;
        }
        finally
        {
            audioSnapshot?.Dispose();
            outputType?.Dispose();
            snapshot?.Dispose();
            ExitSnapshotOperation();
        }
    }

    private bool TryEnterSnapshotOperation(bool requireVideoEpoch,out long videoEpochSystemTicks)
    {
        lock(_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed,this);
            if(requireVideoEpoch&&!_videoEpochSystemTicks.HasValue)
            {
                videoEpochSystemTicks=0;
                return false;
            }

            _activeSnapshotOperations++;
            if(_activeSnapshotOperations==1)_snapshotIdle.Reset();
            videoEpochSystemTicks=_videoEpochSystemTicks??0;
            return true;
        }
    }

    private void ExitSnapshotOperation()
    {
        lock(_lifetimeGate)
        {
            if(_activeSnapshotOperations<=0)
                throw new InvalidOperationException("Contador de snapshots da sessão ficou inconsistente.");
            _activeSnapshotOperations--;
            if(_activeSnapshotOperations==0)_snapshotIdle.Set();
        }
    }

    private void FramePool_OnFrameArrived(Direct3D11CaptureFramePool sender,object args)
    {
        lock(_lifetimeGate)
        {
            if(_disposed)return;
            if(Interlocked.Exchange(ref _frameBusy,1)!=0)
            {
                Interlocked.Increment(ref _frameBusyCallbacks);
                return;
            }
            _frameIdle.Reset();
        }

        try
        {
            // Um evento FrameArrived pode representar mais de um frame já enfileirado.
            // Drenamos o pool inteiro para não deixar o frame seguinte preso até outro callback.
            while(true)
            {
                using var frame=sender.TryGetNextFrame();
                if(frame==null)break;

                Interlocked.Increment(ref _wgcReceived);
                _wgcRate10s.Mark();

                var systemTicks=frame.SystemRelativeTime.Ticks;
                _wgcIntervals10s.Add(systemTicks);

                if(!ProcessFrame(frame,systemTicks))
                    break;
            }
        }
        catch(Exception ex)
        {
            RecordFrameFailure("FRAME_PIPELINE",ex);
        }
        finally
        {
            Volatile.Write(ref _frameBusy,0);
            _frameIdle.Set();
        }
    }

    private bool ProcessFrame(Direct3D11CaptureFrame frame,long systemTicks)
    {
        var size=frame.ContentSize;
        if(size.Width!=InputWidth||size.Height!=InputHeight)
        {
            Interlocked.Increment(ref _contentSizeChanged);
            Interlocked.Increment(ref _framesSkipped);
            RequestRestart(
                "CONTENT_SIZE_CHANGED",
                $"A resolução do Albion mudou de {InputWidth}x{InputHeight} para {size.Width}x{size.Height}. A sessão será recriada automaticamente.");
            return false;
        }

        if(!_framePacer.TryAccept(systemTicks,out var timestamp,out var duration))
        {
            Interlocked.Increment(ref _pacerRejected);
            Interlocked.Increment(ref _framesSkipped);
            return true;
        }

        _videoEpochSystemTicks??=systemTicks;

        IntPtr texturePointer;
        try
        {
            texturePointer=WindowsGraphicsCaptureInterop.GetTexturePointer(frame.Surface);
        }
        catch(Exception ex)
        {
            RecordFrameFailure("WGC_SURFACE",ex);
            return true;
        }

        if(!_converter.TryAcquireOutput(out var outputLease)||outputLease is null)
        {
            Interlocked.Increment(ref _nv12PoolFull);
            Interlocked.Increment(ref _framesSkipped);

            var stalled=_nv12PoolWatchdog.Observe(true,systemTicks);
            if(stalled)
            {
                RequestRestart(
                    "NV12_POOL_STALLED",
                    $"Pool NV12 permaneceu cheio em {GpuFrameConverter.OutputPoolSize}/{GpuFrameConverter.OutputPoolSize} por pelo menos 2 s. A sessão será recriada automaticamente.");
                return false;
            }

            lock(_stateGate)
            {
                _lastErrorStage="NV12_POOL";
                _lastError=$"Pool NV12 ocupado ({GpuFrameConverter.OutputPoolSize}/{GpuFrameConverter.OutputPoolSize}); frame descartado aguardando liberação do encoder.";
            }
            return true;
        }

        _nv12PoolWatchdog.Observe(false,systemTicks);

        try
        {
            _converter.Convert(texturePointer,outputLease);
        }
        catch(Exception ex)
        {
            outputLease.Dispose();
            RecordFrameFailure("BGRA_TO_NV12",ex);
            return true;
        }

        try
        {
            _encoder.WriteFrame(outputLease,timestamp,duration);
        }
        catch(Exception ex)
        {
            RecordFrameFailure("H264_WRITE_SAMPLE",ex);
            return true;
        }

        Interlocked.Increment(ref _framesAccepted);
        Interlocked.Increment(ref _encoderSubmitted);
        _submitRate10s.Mark();

        lock(_stateGate)
        {
            _lastError=null;
            _lastErrorStage=null;
        }
        return true;
    }

    private void Encoder_OnSampleEncoded(long sampleTime)
    {
        _encodedRate10s.Mark();
    }

    private void RequestRestart(string stage,string reason)
    {
        lock(_stateGate)
        {
            _restartReason=reason;
            _lastErrorStage=stage;
            _lastError=reason;
        }
        Interlocked.Exchange(ref _restartRequested,1);
    }

    private void RecordFrameFailure(string stage,Exception ex)
    {
        switch(stage)
        {
            case "WGC_SURFACE":
                Interlocked.Increment(ref _wgcSurfaceErrors);
                break;
            case "BGRA_TO_NV12":
                Interlocked.Increment(ref _bgraToNv12Errors);
                break;
            case "H264_WRITE_SAMPLE":
                Interlocked.Increment(ref _h264WriteErrors);
                break;
            default:
                Interlocked.Increment(ref _framePipelineErrors);
                break;
        }

        lock(_stateGate)
        {
            _lastErrorStage=stage;
            _lastError=FormatException(ex);
        }
        Interlocked.Increment(ref _framesSkipped);
    }

    private static string FormatException(Exception ex)
    {
        var hr=ex.HResult;
        return $"{ex.GetType().Name} · HRESULT 0x{hr:X8} · {ex.Message}";
    }

    private static (int Width,int Height) FitInside1080p(int width,int height)
    {
        var scale=Math.Min(1.0,Math.Min(1920.0/width,1080.0/height));
        var w=Math.Max(2,(int)Math.Floor(width*scale));
        var h=Math.Max(2,(int)Math.Floor(height*scale));
        if((w&1)!=0)w--;
        if((h&1)!=0)h--;
        return (Math.Max(2,w),Math.Max(2,h));
    }

    public void Dispose()
    {
        lock(_lifetimeGate)
        {
            if(_disposed)return;
            _disposed=true;
            _framePool.FrameArrived-=FramePool_OnFrameArrived;
            _encoder.SampleEncoded-=Encoder_OnSampleEncoded;
        }

        _frameIdle.Wait();
        _snapshotIdle.Wait();

        try{_captureSession.Dispose();}catch{}
        try{_framePool.Dispose();}catch{}
        try{_audioCapture?.Dispose();}catch{}
        try{_audioRing?.Dispose();}catch{}
        try{_snapshotOutputTypeTemplate.Dispose();}catch{}
        try{_encoder.Dispose();}catch{}
        try{_converter.Dispose();}catch{}
        try{_winRtDevice.Dispose();}catch{}
        try{_context.Dispose();}catch{}
        try{_device.Dispose();}catch{}
        try{_ring.Dispose();}catch{}
        if(_mediaFoundationStarted)
        {
            _mediaFoundationStarted=false;
            try{MediaFactory.MFShutdown();}catch{}
        }
        _frameIdle.Dispose();
        _snapshotIdle.Dispose();
    }

    internal sealed class DetachedReplaySnapshot:IDisposable
    {
        private bool _disposed;
        public DetachedReplaySnapshot(
            EncodedSampleRing.ClipSnapshot video,
            IMFMediaType outputType,
            PcmAudioRing.AudioSnapshot? audio)
        {
            Video=video;
            OutputType=outputType;
            Audio=audio;
        }

        public EncodedSampleRing.ClipSnapshot Video{get;}
        public IMFMediaType OutputType{get;}
        public PcmAudioRing.AudioSnapshot? Audio{get;}
        public long EstimatedBytes=>Video.Length+(Audio?.Length??0);

        public void Dispose()
        {
            if(_disposed)return;
            _disposed=true;
            Audio?.Dispose();
            OutputType.Dispose();
            Video.Dispose();
        }
    }

    public sealed record CaptureStatus(
        long FramesAccepted,
        long FramesSkipped,
        TimeSpan BufferedDuration,
        long BufferBytes,
        long EncodedSamples,
        long CleanPoints,
        int TargetGopFrames,
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
        string? LastError,
        string? LastErrorStage,
        string? LastSavedPath,
        string AudioStatus,
        long AudioPackets,
        long AudioBytes,
        TimeSpan AudioBufferedDuration,
        long AudioGapsDetected,
        string? AudioError);
}
