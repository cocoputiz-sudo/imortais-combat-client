#nullable enable
using System;
using Vortice;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace StatisticsAnalysisTool.Imortais.Highlights;

internal sealed class GpuFrameConverter : IDisposable
{
    private const BindFlags VideoEncoderBind=(BindFlags)0x400;
    public const int OutputPoolSize=6;

    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11VideoDevice _videoDevice;
    private readonly ID3D11VideoContext _videoContext;
    private readonly ID3D11VideoProcessorEnumerator _enumerator;
    private readonly ID3D11VideoProcessor _processor;
    private readonly ID3D11Texture2D _inputTexture;
    private readonly ID3D11VideoProcessorInputView _inputView;
    private readonly ID3D11Texture2D[] _outputTextures=new ID3D11Texture2D[OutputPoolSize];
    private readonly ID3D11VideoProcessorOutputView[] _outputViews=new ID3D11VideoProcessorOutputView[OutputPoolSize];
    private readonly VideoProcessorStream[] _streams=new VideoProcessorStream[1];
    private readonly TextureLeasePool _leasePool=new(OutputPoolSize);
    private bool _disposed;

    public GpuFrameConverter(ID3D11Device device,ID3D11DeviceContext context,int inputWidth,int inputHeight,int outputWidth,int outputHeight,int fps)
    {
        InputWidth=inputWidth;InputHeight=inputHeight;OutputWidth=outputWidth;OutputHeight=outputHeight;_context=context;
        _videoDevice=device.QueryInterface<ID3D11VideoDevice>();
        _videoContext=context.QueryInterface<ID3D11VideoContext>();

        var desc=new VideoProcessorContentDescription
        {
            InputFrameFormat=VideoFrameFormat.Progressive,
            InputFrameRate=new Rational((uint)fps,1),InputWidth=(uint)inputWidth,InputHeight=(uint)inputHeight,
            OutputFrameRate=new Rational((uint)fps,1),OutputWidth=(uint)outputWidth,OutputHeight=(uint)outputHeight,
            Usage=VideoUsage.PlaybackNormal
        };
        _enumerator=_videoDevice.CreateVideoProcessorEnumerator(desc);
        _processor=_videoDevice.CreateVideoProcessor(_enumerator,0);

        var inputDesc=new Texture2DDescription(
            Format.B8G8R8A8_UNorm,(uint)inputWidth,(uint)inputHeight,1,1,
            BindFlags.RenderTarget|BindFlags.ShaderResource,ResourceUsage.Default,CpuAccessFlags.None,1,0,ResourceOptionFlags.None);
        _inputTexture=device.CreateTexture2D(inputDesc);

        var inputViewDesc=new VideoProcessorInputViewDescription
        {
            FourCC=0,ViewDimension=VideoProcessorInputViewDimension.Texture2D,
            Texture2D=new Texture2DVideoProcessorInputView{MipSlice=0,ArraySlice=0}
        };
        _inputView=_videoDevice.CreateVideoProcessorInputView(_inputTexture,_enumerator,inputViewDesc);

        var outputTextureDesc=new Texture2DDescription(
            Format.NV12,(uint)outputWidth,(uint)outputHeight,1,1,
            BindFlags.RenderTarget|VideoEncoderBind,ResourceUsage.Default,CpuAccessFlags.None,1,0,ResourceOptionFlags.None);
        var outputViewDesc=new VideoProcessorOutputViewDescription
        {
            ViewDimension=VideoProcessorOutputViewDimension.Texture2D,
            Texture2D=new Texture2DVideoProcessorOutputView{MipSlice=0}
        };
        for(var i=0;i<OutputPoolSize;i++)
        {
            _outputTextures[i]=device.CreateTexture2D(outputTextureDesc);
            _outputViews[i]=_videoDevice.CreateVideoProcessorOutputView(_outputTextures[i],_enumerator,outputViewDesc);
        }

        _videoContext.VideoProcessorSetStreamFrameFormat(_processor,0,VideoFrameFormat.Progressive);
        _videoContext.VideoProcessorSetStreamSourceRect(_processor,0,true,new RawRect(0,0,inputWidth,inputHeight));
        _videoContext.VideoProcessorSetStreamDestRect(_processor,0,true,new RawRect(0,0,outputWidth,outputHeight));
        _videoContext.VideoProcessorSetOutputTargetRect(_processor,true,new RawRect(0,0,outputWidth,outputHeight));
        _videoContext.VideoProcessorSetStreamAutoProcessingMode(_processor,0,false);
        _streams[0]=new VideoProcessorStream{Enable=true,InputSurface=_inputView};
    }

    public int InputWidth{get;}
    public int InputHeight{get;}
    public int OutputWidth{get;}
    public int OutputHeight{get;}
    public int OutputSurfacesInUse=>_leasePool.InUseCount;

    public bool TryAcquireOutput(out OutputLease? lease)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        if(!_leasePool.TryAcquire(out var slot)){lease=null;return false;}
        lease=new OutputLease(this,slot,_outputTextures[slot],_outputViews[slot]);
        return true;
    }

    public void Convert(IntPtr texturePointer,OutputLease output)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        ArgumentNullException.ThrowIfNull(output);
        if(texturePointer==IntPtr.Zero)throw new ArgumentException("Texture WGC inválida.",nameof(texturePointer));
        if(!ReferenceEquals(output.Owner,this))throw new ArgumentException("Lease pertence a outro conversor.",nameof(output));
        using var source=(ID3D11Texture2D)texturePointer;
        _context.CopyResource(_inputTexture,source);
        _videoContext.VideoProcessorBlt(_processor,output.OutputView,0,_streams).CheckError();
    }

    private void ReleaseSlot(int slot)=>_leasePool.Release(slot);

    public void Dispose()
    {
        if(_disposed)return;
        var inUse=_leasePool.InUseCount;
        if(inUse!=0)throw new InvalidOperationException($"GpuFrameConverter encerrado com {inUse} superfícies NV12 ainda em uso.");
        _disposed=true;
        for(var i=0;i<_outputViews.Length;i++)_outputViews[i]?.Dispose();
        for(var i=0;i<_outputTextures.Length;i++)_outputTextures[i]?.Dispose();
        _inputView.Dispose();_inputTexture.Dispose();_processor.Dispose();_enumerator.Dispose();_videoContext.Dispose();_videoDevice.Dispose();
    }

    internal sealed class OutputLease:IDisposable
    {
        private GpuFrameConverter? _owner;
        internal OutputLease(GpuFrameConverter owner,int slot,ID3D11Texture2D texture,ID3D11VideoProcessorOutputView outputView)
        {_owner=owner;Slot=slot;Texture=texture;OutputView=outputView;}
        internal GpuFrameConverter? Owner=>_owner;
        internal int Slot{get;}
        public ID3D11Texture2D Texture{get;}
        internal ID3D11VideoProcessorOutputView OutputView{get;}
        public void Dispose()
        {
            var owner=System.Threading.Interlocked.Exchange(ref _owner,null);
            owner?.ReleaseSlot(Slot);
        }
    }
}
