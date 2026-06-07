using System.Runtime.InteropServices;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace WipShare.Client.Native;

/// <summary>
/// D3D11 + DXGI interop. Signatures verified against d3d11.h / dxgi.h /
/// windows.graphics.directx.direct3d11.interop.h in the Windows 10 SDK 10.0.19041.
/// </summary>
internal static unsafe class D3D11Interop
{
    // ---- Constants ----------------------------------------------------------

    public const uint D3D11_SDK_VERSION                = 7;
    public const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
    public const int  D3D_DRIVER_TYPE_HARDWARE         = 1;
    public const int  D3D_DRIVER_TYPE_WARP             = 5;

    // dxgiformat.h
    public const uint DXGI_FORMAT_B8G8R8A8_UNORM = 87;

    // d3d11.h
    public const uint D3D11_USAGE_DEFAULT          = 0;
    public const uint D3D11_USAGE_IMMUTABLE        = 1;
    public const uint D3D11_USAGE_DYNAMIC          = 2;
    public const uint D3D11_USAGE_STAGING          = 3;

    public const uint D3D11_BIND_SHADER_RESOURCE   = 0x8;
    public const uint D3D11_BIND_RENDER_TARGET     = 0x20;

    public const uint D3D11_CPU_ACCESS_READ        = 0x20000;
    public const uint D3D11_CPU_ACCESS_WRITE       = 0x10000;

    public const uint D3D11_MAP_READ               = 1;
    public const uint D3D11_MAP_WRITE              = 2;
    public const uint D3D11_MAP_READ_WRITE         = 3;

    // ---- IIDs (verified) ----------------------------------------------------

    public static readonly Guid IID_IDXGIDevice                  = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    public static readonly Guid IID_ID3D11Device                 = new("db6f6ddb-ac77-4e88-8253-819df9bbf140");
    public static readonly Guid IID_ID3D11Texture2D              = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
    public static readonly Guid IID_IDXGISurface                 = new("cafcb56c-6ac3-4889-bf47-9e23bbd260ec");
    public static readonly Guid IID_IDirect3DDxgiInterfaceAccess = new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");

    // ---- Structs ------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_SAMPLE_DESC
    {
        public uint Count;
        public uint Quality;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_TEXTURE2D_DESC
    {
        public uint Width;
        public uint Height;
        public uint MipLevels;
        public uint ArraySize;
        public uint Format;            // DXGI_FORMAT
        public DXGI_SAMPLE_DESC SampleDesc;
        public uint Usage;             // D3D11_USAGE
        public uint BindFlags;
        public uint CPUAccessFlags;
        public uint MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_BOX
    {
        public uint left;
        public uint top;
        public uint front;
        public uint right;
        public uint bottom;
        public uint back;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_MAPPED_SUBRESOURCE
    {
        public IntPtr pData;
        public uint   RowPitch;
        public uint   DepthPitch;
    }

    // ---- P/Invokes ----------------------------------------------------------

    [DllImport("d3d11.dll", ExactSpelling = true)]
    public static extern int D3D11CreateDevice(
        IntPtr pAdapter,
        int DriverType,
        IntPtr Software,
        uint Flags,
        IntPtr pFeatureLevels,
        uint FeatureLevels,
        uint SDKVersion,
        out IntPtr ppDevice,
        out int pFeatureLevel,
        out IntPtr ppImmediateContext);

    [DllImport("d3d11.dll", ExactSpelling = true)]
    public static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [DllImport("d3d11.dll", ExactSpelling = true)]
    public static extern int CreateDirect3D11SurfaceFromDXGISurface(IntPtr dxgiSurface, out IntPtr graphicsSurface);

    // ---- Raw vtable helpers -------------------------------------------------
    //
    // ID3D11Device vtable (mfobjects.h-style numbering):
    //   3  CreateBuffer        5 CreateTexture2D   ...    40 GetImmediateContext
    //   4  CreateTexture1D     ...
    //
    // ID3D11DeviceContext vtable (from d3d11.h):
    //   ...  14 Map  15 Unmap  ...  46 CopySubresourceRegion  47 CopyResource ...

    public static int DeviceCreateTexture2D(IntPtr pDevice, D3D11_TEXTURE2D_DESC desc, out IntPtr ppTexture2D)
    {
        var vtbl = *(void***)pDevice;
        var fn = (delegate* unmanaged<IntPtr, D3D11_TEXTURE2D_DESC*, IntPtr, IntPtr*, int>)vtbl[5];
        IntPtr p;
        int hr = fn(pDevice, &desc, IntPtr.Zero /* no initial data */, &p);
        ppTexture2D = p;
        return hr;
    }

    public static void ContextCopySubresourceRegion(
        IntPtr pContext,
        IntPtr pDstResource, uint dstSubresource, uint dstX, uint dstY, uint dstZ,
        IntPtr pSrcResource, uint srcSubresource, D3D11_BOX srcBox)
    {
        var vtbl = *(void***)pContext;
        var fn = (delegate* unmanaged<IntPtr, IntPtr, uint, uint, uint, uint, IntPtr, uint, D3D11_BOX*, void>)vtbl[46];
        fn(pContext, pDstResource, dstSubresource, dstX, dstY, dstZ, pSrcResource, srcSubresource, &srcBox);
    }

    /// <summary>ID3D11DeviceContext::Map - vtable slot 14.</summary>
    public static int ContextMap(IntPtr pContext, IntPtr pResource, uint subresource, uint mapType, uint mapFlags, out D3D11_MAPPED_SUBRESOURCE mapped)
    {
        var vtbl = *(void***)pContext;
        var fn = (delegate* unmanaged<IntPtr, IntPtr, uint, uint, uint, D3D11_MAPPED_SUBRESOURCE*, int>)vtbl[14];
        D3D11_MAPPED_SUBRESOURCE m;
        int hr = fn(pContext, pResource, subresource, mapType, mapFlags, &m);
        mapped = m;
        return hr;
    }

    /// <summary>ID3D11DeviceContext::Unmap - vtable slot 15.</summary>
    public static void ContextUnmap(IntPtr pContext, IntPtr pResource, uint subresource)
    {
        var vtbl = *(void***)pContext;
        var fn = (delegate* unmanaged<IntPtr, IntPtr, uint, void>)vtbl[15];
        fn(pContext, pResource, subresource);
    }

    /// <summary>ID3D11DeviceContext::Flush - vtable slot 111. Submits queued
    /// commands to the GPU. Useful when handing the result off to another
    /// subsystem that doesn't share our command queue.</summary>
    public static void ContextFlush(IntPtr pContext)
    {
        var vtbl = *(void***)pContext;
        var fn = (delegate* unmanaged<IntPtr, void>)vtbl[111];
        fn(pContext);
    }

    // ---- High-level helpers -------------------------------------------------

    /// <summary>
    /// Creates a hardware D3D11 device with BGRA support (required for Graphics
    /// Capture) and wraps it as a WinRT IDirect3DDevice. Caller owns the result.
    /// </summary>
    public static IDirect3DDevice CreateDirect3DDevice()
    {
        var bundle = CreateD3D11Bundle();
        // For consumers that don't need the raw pointers, release them now.
        // The WinRT wrapper retains its own reference to the device.
        if (bundle.RawContext != IntPtr.Zero) Marshal.Release(bundle.RawContext);
        if (bundle.RawDevice  != IntPtr.Zero) Marshal.Release(bundle.RawDevice);
        return bundle.WinRTDevice;
    }

    /// <summary>
    /// Like <see cref="CreateDirect3DDevice"/> but also returns the raw
    /// ID3D11Device + ID3D11DeviceContext pointers, useful for code that needs
    /// to issue D3D11 calls directly (e.g. CreateTexture2D, CopySubresourceRegion).
    /// </summary>
    public static D3D11Bundle CreateD3D11Bundle()
    {
        int hr = D3D11CreateDevice(
            IntPtr.Zero,
            D3D_DRIVER_TYPE_HARDWARE,
            IntPtr.Zero,
            D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            IntPtr.Zero,
            0,
            D3D11_SDK_VERSION,
            out IntPtr pDevice,
            out _,
            out IntPtr pContext);
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);

        IntPtr pDxgiDevice = IntPtr.Zero;
        IntPtr pInspectable = IntPtr.Zero;
        try
        {
            Guid iidDxgi = IID_IDXGIDevice;
            hr = Marshal.QueryInterface(pDevice, ref iidDxgi, out pDxgiDevice);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);

            hr = CreateDirect3D11DeviceFromDXGIDevice(pDxgiDevice, out pInspectable);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);

            var winrt = MarshalInspectable<IDirect3DDevice>.FromAbi(pInspectable);
            return new D3D11Bundle(winrt, pDevice, pContext);
        }
        catch
        {
            // Roll back the raw refs we'd otherwise hand over to the bundle.
            if (pContext != IntPtr.Zero) Marshal.Release(pContext);
            if (pDevice  != IntPtr.Zero) Marshal.Release(pDevice);
            throw;
        }
        finally
        {
            if (pInspectable != IntPtr.Zero) Marshal.Release(pInspectable);
            if (pDxgiDevice  != IntPtr.Zero) Marshal.Release(pDxgiDevice);
        }
    }

    /// <summary>
    /// Pulls the underlying ID3D11Texture2D out of a captured IDirect3DSurface
    /// via the private IDirect3DDxgiInterfaceAccess interface. Caller releases
    /// the returned pointer.
    ///
    /// On .NET 8, casting a CsWinRT-projected <see cref="IDirect3DSurface"/>
    /// directly to a custom <c>[ComImport]</c> interface throws InvalidCastException
    /// (the managed cast doesn't perform a COM QueryInterface). The historical
    /// "(IDirect3DDxgiInterfaceAccess)(object)surface" idiom worked under the
    /// pre-CsWinRT WinRT runtime but doesn't here. We have to drop to native:
    /// pull the IInspectable, QI to the interop interface, and dispatch through
    /// its vtable.
    /// </summary>
    public static IntPtr GetTextureFromSurface(IDirect3DSurface surface)
    {
        if (surface is null) throw new ArgumentNullException(nameof(surface));

        // 1. Surface (CsWinRT projection) → raw IInspectable* (AddRef'd).
        IntPtr pInspectable = MarshalInspectable<IDirect3DSurface>.FromManaged(surface);
        if (pInspectable == IntPtr.Zero)
            throw new InvalidOperationException("Failed to marshal IDirect3DSurface to native IInspectable");
        try
        {
            // 2. QI to IDirect3DDxgiInterfaceAccess.
            Guid iidAccess = IID_IDirect3DDxgiInterfaceAccess;
            int hr = Marshal.QueryInterface(pInspectable, ref iidAccess, out IntPtr pAccess);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);

            try
            {
                // 3. Call IDirect3DDxgiInterfaceAccess::GetInterface (vtable slot 3
                //    - first method after IUnknown's QI/AddRef/Release).
                var vtbl = *(void***)pAccess;
                var fn = (delegate* unmanaged<IntPtr, Guid*, IntPtr*, int>)vtbl[3];
                Guid iidTex = IID_ID3D11Texture2D;
                IntPtr texture;
                hr = fn(pAccess, &iidTex, &texture);
                if (hr < 0) Marshal.ThrowExceptionForHR(hr);
                return texture;
            }
            finally { Marshal.Release(pAccess); }
        }
        finally { Marshal.Release(pInspectable); }
    }

    /// <summary>
    /// Wraps an existing ID3D11Texture2D as a projected WinRT IDirect3DSurface
    /// (so it can be fed to <c>SoftwareBitmap.CreateCopyFromSurfaceAsync</c>
    /// downstream). The returned surface holds its own reference to the
    /// texture, so caller can release their texture ref independently.
    /// </summary>
    public static IDirect3DSurface WrapTextureAsSurface(IntPtr pTexture)
    {
        Guid iidSurface = IID_IDXGISurface;
        int hr = Marshal.QueryInterface(pTexture, ref iidSurface, out IntPtr pDxgiSurface);
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        try
        {
            hr = CreateDirect3D11SurfaceFromDXGISurface(pDxgiSurface, out IntPtr pInspectable);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);
            try
            {
                return MarshalInspectable<IDirect3DSurface>.FromAbi(pInspectable);
            }
            finally { Marshal.Release(pInspectable); }
        }
        finally { Marshal.Release(pDxgiSurface); }
    }
}

/// <summary>
/// Owns the D3D11 device + its immediate context alongside the WinRT
/// projection. Disposes all three on <see cref="Dispose"/>.
/// </summary>
internal sealed class D3D11Bundle : IDisposable
{
    public IDirect3DDevice WinRTDevice { get; }
    public IntPtr RawDevice  { get; private set; }
    public IntPtr RawContext { get; private set; }
    private bool _disposed;

    public D3D11Bundle(IDirect3DDevice winrtDevice, IntPtr rawDevice, IntPtr rawContext)
    {
        WinRTDevice = winrtDevice;
        RawDevice   = rawDevice;
        RawContext  = rawContext;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { WinRTDevice.Dispose(); } catch { }
        if (RawContext != IntPtr.Zero) { Marshal.Release(RawContext); RawContext = IntPtr.Zero; }
        if (RawDevice  != IntPtr.Zero) { Marshal.Release(RawDevice);  RawDevice  = IntPtr.Zero; }
    }
}

/// <summary>
/// IDirect3DDxgiInterfaceAccess - private WinRT helper that bridges projected
/// IDirect3DSurface / IDirect3DDevice objects back to their underlying DXGI/D3D11
/// COM interfaces. Defined in windows.graphics.directx.direct3d11.interop.h.
/// </summary>
[ComImport]
[Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDirect3DDxgiInterfaceAccess
{
    [PreserveSig]
    int GetInterface([In] ref Guid iid, out IntPtr p);
}
