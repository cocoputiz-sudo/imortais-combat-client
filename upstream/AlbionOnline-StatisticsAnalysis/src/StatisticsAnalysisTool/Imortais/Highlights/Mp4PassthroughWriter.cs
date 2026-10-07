using System;
using System.IO;
using Vortice.MediaFoundation;

namespace StatisticsAnalysisTool.Imortais.Highlights;

internal static unsafe class Mp4PassthroughWriter
{
    public static string Write(
        string finalPath,
        EncodedSampleRing.ClipSnapshot videoSnapshot,
        IMFMediaType h264Type,
        PcmAudioRing.AudioSnapshot? audioSnapshot=null)
    {
        ArgumentNullException.ThrowIfNull(videoSnapshot);
        ArgumentNullException.ThrowIfNull(h264Type);
        if(videoSnapshot.Count==0)throw new InvalidOperationException("Replay vazio.");

        var mfStarted=false;
        try
        {
            MediaFactory.MFStartup(true).CheckError();
            mfStarted=true;

        var directory=Path.GetDirectoryName(finalPath);
        if(string.IsNullOrWhiteSpace(directory))throw new ArgumentException("Destino inválido.",nameof(finalPath));
        Directory.CreateDirectory(directory);

        var partialPath=finalPath+".partial.mp4";
        try
        {
            if(File.Exists(partialPath))File.Delete(partialPath);

            using var videoOutputType=HighlightMediaTypes.Clone(h264Type);
            using var videoInputType=HighlightMediaTypes.Clone(h264Type);
            using var writer=MediaFactory.MFCreateSinkWriterFromURL(partialPath,null,null);
            var videoStream=writer.AddStream(videoOutputType);
            writer.SetInputMediaType(videoStream,videoInputType,null);

            var audioStream=-1;
            IMFMediaType? audioOutputType=null;
            IMFMediaType? audioInputType=null;
            try
            {
                if(audioSnapshot is not null&&audioSnapshot.Count>0)
                {
                    audioOutputType=HighlightMediaTypes.CreateAac(
                        AlbionProcessAudioCapture.SampleRate,
                        AlbionProcessAudioCapture.Channels);
                    audioInputType=HighlightMediaTypes.CreatePcm16(
                        AlbionProcessAudioCapture.SampleRate,
                        AlbionProcessAudioCapture.Channels);
                    audioStream=writer.AddStream(audioOutputType);
                    writer.SetInputMediaType(audioStream,audioInputType,null);
                }

                writer.BeginWriting();

                var vi=0;
                var ai=0;
                while(vi<videoSnapshot.Count||(audioStream>=0&&audioSnapshot is not null&&ai<audioSnapshot.Count))
                {
                    var nextVideo=vi<videoSnapshot.Count
                        ? videoSnapshot.Samples[vi].Timestamp100Ns
                        : long.MaxValue;
                    var nextAudio=audioStream>=0&&audioSnapshot is not null&&ai<audioSnapshot.Count
                        ? audioSnapshot.Samples[ai].Timestamp100Ns
                        : long.MaxValue;

                    if(nextVideo<=nextAudio)
                    {
                        WriteVideoSample(writer,videoStream,videoSnapshot,vi++);
                    }
                    else
                    {
                        WriteAudioSample(writer,audioStream,audioSnapshot!,ai++);
                    }
                }

                writer.Finalize();
            }
            finally
            {
                audioInputType?.Dispose();
                audioOutputType?.Dispose();
            }

            if(File.Exists(finalPath))File.Delete(finalPath);
            File.Move(partialPath,finalPath);
            return finalPath;
        }
        catch
        {
            try{if(File.Exists(partialPath))File.Delete(partialPath);}catch{}
            throw;
        }
        }
        finally
        {
            if(mfStarted){try{MediaFactory.MFShutdown();}catch{}}
        }
    }

    private static void WriteVideoSample(
        IMFSinkWriter writer,
        int stream,
        EncodedSampleRing.ClipSnapshot snapshot,
        int index)
    {
        var meta=snapshot.Samples[index];
        var bytes=snapshot.GetBytes(index);
        using var buffer=CreateMemoryBuffer(bytes);
        using var sample=MediaFactory.MFCreateSample();
        sample.AddBuffer(buffer);
        sample.SampleTime=meta.Timestamp100Ns;
        sample.SampleDuration=Math.Max(1,meta.Duration100Ns);
        if(meta.IsCleanPoint)sample.Set(SampleAttributeKeys.CleanPoint,true).CheckError();
        writer.WriteSample(stream,sample);
    }

    private static void WriteAudioSample(
        IMFSinkWriter writer,
        int stream,
        PcmAudioRing.AudioSnapshot snapshot,
        int index)
    {
        var meta=snapshot.Samples[index];
        var bytes=snapshot.GetBytes(index);
        using var buffer=CreateMemoryBuffer(bytes);
        using var sample=MediaFactory.MFCreateSample();
        sample.AddBuffer(buffer);
        sample.SampleTime=meta.Timestamp100Ns;
        sample.SampleDuration=Math.Max(1,meta.Duration100Ns);
        writer.WriteSample(stream,sample);
    }

    private static IMFMediaBuffer CreateMemoryBuffer(ReadOnlySpan<byte> bytes)
    {
        var buffer=MediaFactory.MFCreateMemoryBuffer(bytes.Length);
        buffer.Lock(out var destination,out _,out _);
        try
        {
            bytes.CopyTo(new Span<byte>((void*)destination,bytes.Length));
            buffer.CurrentLength=bytes.Length;
            return buffer;
        }
        catch
        {
            buffer.Dispose();
            throw;
        }
        finally
        {
            buffer.Unlock();
        }
    }
}
