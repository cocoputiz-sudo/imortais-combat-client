#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace StatisticsAnalysisTool.Imortais.Highlights;

/// <summary>
/// Captura apenas o áudio renderizado pelo processo do Albion (e filhos) via WASAPI
/// process-loopback. Nunca faz fallback para loopback global do Windows.
/// </summary>
internal sealed class AlbionProcessAudioCapture : IDisposable
{
    public const int SampleRate = 48_000;
    public const int Channels = 2;
    public const int BitsPerSample = 16;
    public const int BlockAlign = Channels * (BitsPerSample / 8);
    public const int RingCapacityBytes = 32 * 1024 * 1024;

    private readonly PcmAudioRing _ring;
    private readonly WasapiRecorder _recorder;
    private long _packets;
    private long _bytes;
    private string? _lastError;
    private bool _disposed;

    private AlbionProcessAudioCapture(PcmAudioRing ring, WasapiRecorder recorder)
    {
        _ring=ring;
        _recorder=recorder;
        _recorder.DataAvailable+=Recorder_OnDataAvailable;
        _recorder.RecordingStopped+=Recorder_OnRecordingStopped;
    }

    public long Packets => Interlocked.Read(ref _packets);
    public long Bytes => Interlocked.Read(ref _bytes);
    public string? LastError => Volatile.Read(ref _lastError);
    public PcmAudioRing Ring => _ring;

    public static async Task<AlbionProcessAudioCapture> StartAsync(int processId,PcmAudioRing ring)
    {
        if(processId<=0)throw new ArgumentOutOfRangeException(nameof(processId));
        ArgumentNullException.ThrowIfNull(ring);

        var format=new WaveFormat(SampleRate,BitsPerSample,Channels);
        var recorder=await new WasapiRecorderBuilder()
            .WithProcessLoopback((uint)processId,ProcessLoopbackMode.IncludeTargetProcessTree)
            .WithEventSync()
            .WithBufferLength(50)
            .WithFormat(format)
            .WithMmcssThreadPriority("Audio")
            .BuildAsync()
            .ConfigureAwait(false);

        var capture=new AlbionProcessAudioCapture(ring,recorder);
        try
        {
            recorder.StartRecording();
            return capture;
        }
        catch
        {
            capture.Dispose();
            throw;
        }
    }

    private void Recorder_OnDataAvailable(
        ReadOnlySpan<byte> buffer,
        AudioClientBufferFlags flags,
        long devicePosition,
        long qpcPosition)
    {
        if(_disposed||buffer.IsEmpty)return;
        try
        {
            if(_ring.TryWrite(buffer,qpcPosition))
            {
                Interlocked.Increment(ref _packets);
                Interlocked.Add(ref _bytes,buffer.Length);
            }
        }
        catch(Exception ex)
        {
            Volatile.Write(ref _lastError,$"{ex.GetType().Name} · HRESULT 0x{ex.HResult:X8} · {ex.Message}");
        }
    }

    private void Recorder_OnRecordingStopped(object? sender,StoppedEventArgs e)
    {
        if(e.Exception is not null)
            Volatile.Write(ref _lastError,$"{e.Exception.GetType().Name} · HRESULT 0x{e.Exception.HResult:X8} · {e.Exception.Message}");
    }

    public void Dispose()
    {
        if(_disposed)return;
        _disposed=true;
        _recorder.DataAvailable-=Recorder_OnDataAvailable;
        _recorder.RecordingStopped-=Recorder_OnRecordingStopped;
        try{_recorder.StopRecording();}catch{}
        try{_recorder.Dispose();}catch{}
    }
}
