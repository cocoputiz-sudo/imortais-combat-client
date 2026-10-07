using System;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace StatisticsAnalysisTool.Imortais.Highlights;

internal static class WindowsGraphicsCaptureInterop
{
    private static readonly Guid GraphicsCaptureItemGuid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid D3D11Texture2DGuid = new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");
    private static readonly Guid GraphicsCaptureSession3Guid = new("F2CDD966-22AE-5EA1-9596-3A289344C3BE");

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int PutIsBorderRequiredDelegate(IntPtr @this, byte value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetIsBorderRequiredDelegate(IntPtr @this, out byte value);

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow(IntPtr window, in Guid iid);
        IntPtr CreateForMonitor(IntPtr monitor, in Guid iid);
    }

    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        IntPtr GetInterface(in Guid iid);
    }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(
        IntPtr dxgiDevice,
        out IntPtr graphicsDevice);

    public static GraphicsCaptureItem CreateItemForWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) throw new ArgumentException("Janela do Albion inválida.", nameof(hwnd));
        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        var pointer = interop.CreateForWindow(hwnd, GraphicsCaptureItemGuid);
        try
        {
            return GraphicsCaptureItem.FromAbi(pointer);
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    public static bool TryDisableCaptureBorder(GraphicsCaptureSession session, out string warning)
    {
        warning = string.Empty;
        if (session == null)
        {
            warning = "Sessão de captura inválida.";
            return false;
        }

        IntPtr inspectable = IntPtr.Zero;
        IntPtr session3 = IntPtr.Zero;
        try
        {
            // O projeto continua mirando Windows 10 19041; por isso não referenciamos
            // IsBorderRequired estaticamente. Obtemos IGraphicsCaptureSession3 por QI
            // somente quando ApiInformation disser que a propriedade existe.
            inspectable = MarshalInspectable<GraphicsCaptureSession>.FromManaged(session);
            var iid = GraphicsCaptureSession3Guid;
            var hr = Marshal.QueryInterface(inspectable, in iid, out session3);
            if (hr < 0 || session3 == IntPtr.Zero)
            {
                warning = "A API de captura existe, mas IGraphicsCaptureSession3 não foi disponibilizada.";
                return false;
            }

            var vtable = Marshal.ReadIntPtr(session3);
            // IInspectable = 6 entradas (IUnknown 3 + IInspectable 3).
            // IGraphicsCaptureSession3 acrescenta get/put IsBorderRequired nas posições 6/7.
            var putPtr = Marshal.ReadIntPtr(vtable, IntPtr.Size * 7);
            var getPtr = Marshal.ReadIntPtr(vtable, IntPtr.Size * 6);
            var put = Marshal.GetDelegateForFunctionPointer<PutIsBorderRequiredDelegate>(putPtr);
            var get = Marshal.GetDelegateForFunctionPointer<GetIsBorderRequiredDelegate>(getPtr);

            hr = put(session3, 0);
            if (hr < 0)
            {
                warning = $"Windows recusou IsBorderRequired=false (HRESULT 0x{hr:X8}).";
                return false;
            }

            hr = get(session3, out var required);
            if (hr < 0)
            {
                warning = $"Não foi possível confirmar o estado da borda (HRESULT 0x{hr:X8}).";
                return false;
            }

            if (required != 0)
            {
                warning = "O Windows manteve a borda de captura.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            warning = "O Windows não autorizou remover a borda de captura: " + ex.Message;
            return false;
        }
        finally
        {
            if (session3 != IntPtr.Zero) Marshal.Release(session3);
            if (inspectable != IntPtr.Zero) Marshal.Release(inspectable);
        }
    }

    public static IDirect3DDevice CreateWinRtDevice(ID3D11Device d3dDevice)
    {
        using var dxgiDevice = d3dDevice.QueryInterface<IDXGIDevice>();
        var hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var pointer);
        Marshal.ThrowExceptionForHR(hr);
        try
        {
            return MarshalInterface<IDirect3DDevice>.FromAbi(pointer);
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    public static IntPtr GetTexturePointer(IDirect3DSurface surface)
    {
        var access = surface.As<IDirect3DDxgiInterfaceAccess>();
        return access.GetInterface(D3D11Texture2DGuid);
    }
}
