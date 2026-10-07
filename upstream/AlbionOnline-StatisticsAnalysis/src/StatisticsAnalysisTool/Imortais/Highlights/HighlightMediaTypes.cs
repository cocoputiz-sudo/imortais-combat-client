using System;
using Vortice.MediaFoundation;

namespace StatisticsAnalysisTool.Imortais.Highlights;

internal static class HighlightMediaTypes
{
    private static readonly Guid PcmAudioSubtype = new("00000001-0000-0010-8000-00AA00389B71");
    private static readonly Guid AacAudioSubtype = new("00001610-0000-0010-8000-00AA00389B71");
    private static readonly Guid H264ProfileAttribute = new("ad76a80b-2d5c-4e0b-b375-64e520137036");
    public const uint H264MainProfile = 77;
    public static IMFMediaType CreateH264(int width, int height, int fps, int bitrate)
    {
        var type = MediaFactory.MFCreateMediaType();
        type.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video).CheckError();
        type.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.H264).CheckError();
        type.Set(MediaTypeAttributeKeys.AvgBitrate, (uint)bitrate).CheckError();
        type.Set(MediaTypeAttributeKeys.InterlaceMode, 2u).CheckError(); // MFVideoInterlace_Progressive
        type.Set(MediaTypeAttributeKeys.MaxKeyframeSpacing, (uint)(fps * 2)).CheckError();
        type.Set(H264ProfileAttribute, H264MainProfile).CheckError();
        MediaFactory.MFSetAttributeSize(type, MediaTypeAttributeKeys.FrameSize, (uint)width, (uint)height).CheckError();
        MediaFactory.MFSetAttributeRatio(type, MediaTypeAttributeKeys.FrameRate, (uint)fps, 1).CheckError();
        MediaFactory.MFSetAttributeRatio(type, MediaTypeAttributeKeys.PixelAspectRatio, 1, 1).CheckError();
        return type;
    }

    public static IMFMediaType CreateNv12(int width, int height, int fps)
    {
        var type = MediaFactory.MFCreateMediaType();
        type.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video).CheckError();
        type.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.NV12).CheckError();
        type.Set(MediaTypeAttributeKeys.InterlaceMode, 2u).CheckError();
        MediaFactory.MFSetAttributeSize(type, MediaTypeAttributeKeys.FrameSize, (uint)width, (uint)height).CheckError();
        MediaFactory.MFSetAttributeRatio(type, MediaTypeAttributeKeys.FrameRate, (uint)fps, 1).CheckError();
        MediaFactory.MFSetAttributeRatio(type, MediaTypeAttributeKeys.PixelAspectRatio, 1, 1).CheckError();
        return type;
    }


    public static IMFMediaType CreatePcm16(int sampleRate=48_000,int channels=2)
    {
        var type=MediaFactory.MFCreateMediaType();
        type.Set(MediaTypeAttributeKeys.MajorType,MediaTypeGuids.Audio).CheckError();
        type.Set(MediaTypeAttributeKeys.Subtype,PcmAudioSubtype).CheckError();
        type.Set(MediaTypeAttributeKeys.AudioBitsPerSample,16u).CheckError();
        type.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond,(uint)sampleRate).CheckError();
        type.Set(MediaTypeAttributeKeys.AudioNumChannels,(uint)channels).CheckError();
        type.Set(MediaTypeAttributeKeys.AudioBlockAlignment,(uint)(channels*2)).CheckError();
        type.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond,(uint)(sampleRate*channels*2)).CheckError();
        return type;
    }

    public static IMFMediaType CreateAac(int sampleRate=48_000,int channels=2,int bytesPerSecond=20_000)
    {
        var type=MediaFactory.MFCreateMediaType();
        type.Set(MediaTypeAttributeKeys.MajorType,MediaTypeGuids.Audio).CheckError();
        type.Set(MediaTypeAttributeKeys.Subtype,AacAudioSubtype).CheckError();
        type.Set(MediaTypeAttributeKeys.AudioBitsPerSample,16u).CheckError();
        type.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond,(uint)sampleRate).CheckError();
        type.Set(MediaTypeAttributeKeys.AudioNumChannels,(uint)channels).CheckError();
        type.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond,(uint)bytesPerSecond).CheckError();
        return type;
    }

    public static IMFMediaType Clone(IMFMediaType source)
    {
        var copy = MediaFactory.MFCreateMediaType();
        source.CopyAllItems(copy).CheckError();
        return copy;
    }
}
