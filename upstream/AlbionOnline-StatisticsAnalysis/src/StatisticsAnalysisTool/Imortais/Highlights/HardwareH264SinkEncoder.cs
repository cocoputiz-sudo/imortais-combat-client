#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.MediaFoundation;

namespace StatisticsAnalysisTool.Imortais.Highlights;

internal sealed class HardwareH264SinkEncoder : IDisposable
{
    private static readonly Guid IidD3D11Texture2D=new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
    private static readonly Guid CodecGopSize=new("95f31b26-95a4-41aa-9303-246a7fc6eef1");
    private static readonly Guid CodecBPictureCount=new("8d390aac-dc5c-4200-b57f-814d04babab2");
    private static readonly Guid CodecRateControlMode=new("1c0608e9-370c-4710-8a58-cb6181c42423");
    private static readonly Guid CodecMeanBitRate=new("f7222374-2144-4815-b550-a37f8e12ee52");
    private static readonly Guid CodecLowLatencyMode=new("9c27891a-ed7a-40e1-88e8-b22727a024ee");

    [DllImport("mfplat.dll",ExactSpelling=true)]
    private static extern int MFCreateDXGISurfaceBuffer(ref Guid riid,IntPtr punkSurface,uint subresourceIndex,[MarshalAs(UnmanagedType.Bool)] bool bottomUpWhenLinear,out IntPtr buffer);

    private readonly object _pendingGate=new();
    private readonly SortedDictionary<long,PendingFrame> _pending=new();
    private readonly H264EncodedSampleCollector _collector;
    private readonly IMFActivate _sinkActivate;
    private readonly IMFMediaSink _mediaSink;
    private readonly IMFAttributes _writerAttributes;
    private readonly IMFAttributes _encodingParameters;
    private readonly IMFMediaType _requestedOutputType;
    private readonly IMFMediaType _inputType;
    private readonly IMFDXGIDeviceManager _dxgiManager;
    private readonly IMFSinkWriter _writer;
    private readonly IMFTransform _hardwareTransform;
    private readonly int _streamIndex;
    private bool _disposed;

    private HardwareH264SinkEncoder(H264EncodedSampleCollector collector,IMFActivate sinkActivate,IMFMediaSink mediaSink,
        IMFAttributes writerAttributes,IMFAttributes encodingParameters,IMFMediaType requestedOutputType,IMFMediaType inputType,
        IMFDXGIDeviceManager dxgiManager,IMFSinkWriter writer,IMFTransform hardwareTransform,int streamIndex,string encoderName,int targetGopFrames)
    {
        _collector=collector;_sinkActivate=sinkActivate;_mediaSink=mediaSink;_writerAttributes=writerAttributes;
        _encodingParameters=encodingParameters;_requestedOutputType=requestedOutputType;_inputType=inputType;_dxgiManager=dxgiManager;
        _writer=writer;_hardwareTransform=hardwareTransform;_streamIndex=streamIndex;EncoderName=encoderName;TargetGopFrames=targetGopFrames;
        _collector.SampleProcessed+=Collector_OnSampleProcessed;
        _collector.SampleEncoded+=Collector_OnSampleEncoded;
    }

    public string EncoderName{get;}
    public int TargetGopFrames{get;}
    public long EffectiveGopFrames=>_collector.EffectiveGopFrames;
    public long EncodedSamples=>_collector.EncodedSamples;
    public long EncodedBytes=>_collector.EncodedBytes;
    public long CleanPoints=>_collector.CleanPoints;
    public event Action<long>? SampleEncoded;

    public static HardwareH264SinkEncoder Create(ID3D11Device d3dDevice,EncodedSampleRing ring,int width,int height,int fps,int bitrate)
    {
        ArgumentNullException.ThrowIfNull(d3dDevice);ArgumentNullException.ThrowIfNull(ring);
        var targetGop=checked(fps*2);
        var collector=new H264EncodedSampleCollector(ring);
        IMFActivate? sinkActivate=null;IMFMediaSink? mediaSink=null;IMFAttributes? attrs=null;IMFAttributes? encodingParameters=null;
        IMFMediaType? requestedOutput=null;IMFMediaType? inputType=null;IMFDXGIDeviceManager? manager=null;IMFSinkWriter? writer=null;IMFTransform? hardware=null;

        try
        {
            requestedOutput=HighlightMediaTypes.CreateH264(width,height,fps,bitrate);
            inputType=HighlightMediaTypes.CreateNv12(width,height,fps);
            var inputInfo=new RegisterTypeInfo{GuidMajorType=MediaTypeGuids.Video,GuidSubtype=VideoFormatGuids.NV12};
            var outputInfo=new RegisterTypeInfo{GuidMajorType=MediaTypeGuids.Video,GuidSubtype=VideoFormatGuids.H264};
            using(var hardwareEncoders=MediaFactory.MFTEnumEx(TransformCategoryGuids.VideoEncoder,(uint)(EnumFlag.EnumFlagHardware|EnumFlag.EnumFlagSortandfilter),inputInfo,outputInfo))
            {
                using var enumerator=hardwareEncoders.GetEnumerator();
                if(!enumerator.MoveNext())throw new HardwareEncoderUnavailableException("Nenhum encoder H.264 de hardware compatível com NV12 foi encontrado.");
            }

            sinkActivate=MediaFactory.MFCreateSampleGrabberSinkActivate(requestedOutput,collector);
            mediaSink=sinkActivate.ActivateObject<IMFMediaSink>();
            manager=MediaFactory.MFCreateDXGIDeviceManager();manager.ResetDevice(d3dDevice).CheckError();

            attrs=MediaFactory.MFCreateAttributes(4);
            attrs.Set(SinkWriterAttributeKeys.ReadwriteEnableHardwareTransforms,true).CheckError();
            attrs.Set(SinkWriterAttributeKeys.D3DManager,manager).CheckError();

            encodingParameters=MediaFactory.MFCreateAttributes(5);
            encodingParameters.Set(CodecGopSize,(uint)targetGop).CheckError();
            encodingParameters.Set(CodecBPictureCount,0u).CheckError();
            encodingParameters.Set(CodecRateControlMode,0u).CheckError();
            encodingParameters.Set(CodecMeanBitRate,(uint)bitrate).CheckError();
            encodingParameters.Set(CodecLowLatencyMode,true).CheckError();

            writer=MediaFactory.MFCreateSinkWriterFromMediaSink(mediaSink,attrs);
            using var fixedStreamSink=mediaSink.GetStreamSinkByIndex(0);
            const int stream=0;
            writer.SetInputMediaType(stream,inputType,encodingParameters);
            writer.BeginWriting();

            string? encoderName=null;
            using(var writerEx=writer.QueryInterface<IMFSinkWriterEx>())
            {
                for(var i=0;i<8;i++)
                {
                    IMFTransform? candidate=null;
                    try{writerEx.GetTransformForStream(stream,i,out _,out candidate);}
                    catch{candidate?.Dispose();break;}
                    if(candidate==null)continue;
                    if(IsHardwareTransform(candidate,out var name)){hardware=candidate;encoderName=name;break;}
                    candidate.Dispose();
                }
            }
            if(hardware==null)throw new HardwareEncoderUnavailableException("Nenhum encoder H.264 de hardware foi selecionado pelo Media Foundation.");
            return new HardwareH264SinkEncoder(collector,sinkActivate,mediaSink,attrs,encodingParameters,requestedOutput,inputType,manager,writer,hardware,stream,encoderName??"H.264 hardware",targetGop);
        }
        catch
        {
            hardware?.Dispose();writer?.Dispose();manager?.Dispose();inputType?.Dispose();requestedOutput?.Dispose();encodingParameters?.Dispose();attrs?.Dispose();
            try{mediaSink?.Shutdown();}catch{} mediaSink?.Dispose();sinkActivate?.Dispose();collector.Dispose();throw;
        }
    }

    public void WriteFrame(GpuFrameConverter.OutputLease nv12Lease,long timestamp100Ns,long duration100Ns)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);ArgumentNullException.ThrowIfNull(nv12Lease);
        var iid=IidD3D11Texture2D;IntPtr bufferPtr=IntPtr.Zero;
        try
        {
            var hr=MFCreateDXGISurfaceBuffer(ref iid,nv12Lease.Texture.NativePointer,0,false,out bufferPtr);
            Marshal.ThrowExceptionForHR(hr);
            if(bufferPtr==IntPtr.Zero)throw new InvalidOperationException("MFCreateDXGISurfaceBuffer retornou um ponteiro nulo.");
        }
        catch(Exception ex){nv12Lease.Dispose();throw new InvalidOperationException("H264_DXGI_BUFFER_CREATE: "+ex.Message,ex);}

        using var buffer=new IMFMediaBuffer(bufferPtr);
        try{buffer.CurrentLength=buffer.MaxLength;}
        catch(Exception ex){nv12Lease.Dispose();throw new InvalidOperationException("H264_DXGI_BUFFER_LENGTH: "+ex.Message,ex);}

        IMFSample? sample=null;var transferred=false;
        try
        {
            sample=MediaFactory.MFCreateSample();sample.AddBuffer(buffer);sample.SampleTime=timestamp100Ns;sample.SampleDuration=Math.Max(1,duration100Ns);
            lock(_pendingGate)
            {
                if(_pending.ContainsKey(timestamp100Ns))throw new InvalidOperationException($"Timestamp H.264 duplicado: {timestamp100Ns}.");
                _pending.Add(timestamp100Ns,new PendingFrame(timestamp100Ns,sample,nv12Lease));transferred=true;sample=null;
            }

            IMFSample pendingSample;
            lock(_pendingGate)pendingSample=_pending[timestamp100Ns].Sample;
            try{_writer.WriteSample(_streamIndex,pendingSample);}
            catch(Exception ex)
            {
                ReleaseExactPending(timestamp100Ns);transferred=false;
                throw new InvalidOperationException("H264_SINK_WRITER: "+ex.Message,ex);
            }
        }
        catch
        {
            if(!transferred){sample?.Dispose();nv12Lease.Dispose();}
            throw;
        }
    }

    public IMFMediaType SnapshotOutputType()
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        try{using var current=_hardwareTransform.GetOutputCurrentType(0);return HighlightMediaTypes.Clone(current);}
        catch{return HighlightMediaTypes.Clone(_requestedOutputType);}
    }

    private void Collector_OnSampleEncoded(long sampleTime)
    {
        try{SampleEncoded?.Invoke(sampleTime);}catch{}
    }

    private void Collector_OnSampleProcessed(long sampleTime)
    {
        List<PendingFrame>? released=null;
        lock(_pendingGate)
        {
            foreach(var pair in _pending)
            {
                if(pair.Key>sampleTime)break;
                released??=new List<PendingFrame>();released.Add(pair.Value);
            }
            if(released is not null)foreach(var pending in released)_pending.Remove(pending.Timestamp100Ns);
        }
        if(released is not null)foreach(var pending in released)pending.Dispose();
    }

    private void ReleaseExactPending(long timestamp)
    {
        PendingFrame? pending=null;
        lock(_pendingGate){if(_pending.TryGetValue(timestamp,out pending))_pending.Remove(timestamp);}
        pending?.Dispose();
    }

    private void ReleaseAllPending()
    {
        PendingFrame[] pending;
        lock(_pendingGate){pending=new PendingFrame[_pending.Count];_pending.Values.CopyTo(pending,0);_pending.Clear();}
        foreach(var item in pending)item.Dispose();
    }

    private static bool IsHardwareTransform(IMFTransform transform,out string? name)
    {
        name=null;
        try
        {
            var attributes=transform.Attributes;
            var hardwareUrl=attributes.GetString(TransformAttributeKeys.MftEnumHardwareUrlAttribute);
            if(string.IsNullOrWhiteSpace(hardwareUrl))return false;
            try{name=attributes.GetString(TransformAttributeKeys.MftFriendlyNameAttribute);}catch{name=hardwareUrl;}
            return true;
        }
        catch{return false;}
    }

    public void Dispose()
    {
        if(_disposed)return;_disposed=true;
        try{_writer.Finalize();}catch{}
        _collector.SampleProcessed-=Collector_OnSampleProcessed;
        _collector.SampleEncoded-=Collector_OnSampleEncoded;
        ReleaseAllPending();
        _hardwareTransform.Dispose();_writer.Dispose();_dxgiManager.Dispose();_inputType.Dispose();_requestedOutputType.Dispose();
        _encodingParameters.Dispose();_writerAttributes.Dispose();
        try{_mediaSink.Shutdown();}catch{} _mediaSink.Dispose();_sinkActivate.Dispose();_collector.Dispose();
    }

    private sealed class PendingFrame:IDisposable
    {
        private IMFSample? _sample;private GpuFrameConverter.OutputLease? _lease;
        public PendingFrame(long timestamp100Ns,IMFSample sample,GpuFrameConverter.OutputLease lease){Timestamp100Ns=timestamp100Ns;_sample=sample;_lease=lease;}
        public long Timestamp100Ns{get;}
        public IMFSample Sample=>_sample??throw new ObjectDisposedException(nameof(PendingFrame));
        public void Dispose()
        {
            var sample=System.Threading.Interlocked.Exchange(ref _sample,null);sample?.Dispose();
            var lease=System.Threading.Interlocked.Exchange(ref _lease,null);lease?.Dispose();
        }
    }
}

internal sealed class HardwareEncoderUnavailableException:InvalidOperationException
{
    public HardwareEncoderUnavailableException(string message):base(message){}
}
