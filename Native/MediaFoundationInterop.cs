using System.Runtime.InteropServices;

namespace WipShare.Client.Native;

/// <summary>
/// Hand-written P/Invoke + COM surface for Media Foundation. Only the slices
/// we actually need are declared. Signatures verified against the Windows 10 SDK
/// 10.0.19041 headers (mfapi.h, mfobjects.h, mfidl.h, mfreadwrite.h, mftransform.h).
/// </summary>
internal static unsafe class MediaFoundationInterop
{
    // ---- Versions / flags ---------------------------------------------------

    public const uint MF_VERSION = 0x00020070; // MF_SDK_VERSION 0x0002, MF_API_VERSION 0x0070
    public const uint MFSTARTUP_FULL = 0;

    public const uint MFT_ENUM_FLAG_SYNCMFT        = 0x00000001;
    public const uint MFT_ENUM_FLAG_ASYNCMFT       = 0x00000002;
    public const uint MFT_ENUM_FLAG_HARDWARE       = 0x00000004;
    public const uint MFT_ENUM_FLAG_FIELDOFUSE     = 0x00000008;
    public const uint MFT_ENUM_FLAG_LOCALMFT       = 0x00000010;
    public const uint MFT_ENUM_FLAG_TRANSCODE_ONLY = 0x00000020;
    public const uint MFT_ENUM_FLAG_SORTANDFILTER  = 0x00000040;

    public const uint MFVideoInterlace_Progressive = 2;
    public const uint eAVEncH264VProfile_Base = 66;
    public const uint eAVEncH264VProfile_Main = 77;
    public const uint eAVEncH264VProfile_High = 100;

    // ---- GUIDs (all verified against SDK headers) ---------------------------

    public static readonly Guid MFT_CATEGORY_VIDEO_ENCODER  = new("f79eac7d-e545-4387-bdee-d647d7bde42a");
    public static readonly Guid MFMediaType_Video           = new("73646976-0000-0010-8000-00aa00389b71");
    public static readonly Guid MFVideoFormat_H264          = new("34363248-0000-0010-8000-00aa00389b71"); // 'H264'
    public static readonly Guid MFVideoFormat_NV12          = new("3231564E-0000-0010-8000-00aa00389b71"); // 'NV12'
    public static readonly Guid MFVideoFormat_ARGB32        = new("00000015-0000-0010-8000-00aa00389b71"); // BGRA in memory
    public static readonly Guid MFVideoFormat_RGB32         = new("00000016-0000-0010-8000-00aa00389b71"); // BGRX in memory

    // mftransform.h
    public static readonly Guid MFT_FRIENDLY_NAME_Attribute   = new("314FFBAE-5B41-4C95-9C19-4E7D586FACE3");
    public static readonly Guid MFT_TRANSFORM_CLSID_Attribute = new("6821C42B-65A4-4E82-99BC-9A88205ECD0C");

    // mfapi.h media-type attributes
    public static readonly Guid MF_MT_MAJOR_TYPE          = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    public static readonly Guid MF_MT_SUBTYPE             = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    public static readonly Guid MF_MT_FRAME_SIZE          = new("1652c33d-d6b2-4012-b834-72030849a37d");
    public static readonly Guid MF_MT_FRAME_RATE          = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    public static readonly Guid MF_MT_PIXEL_ASPECT_RATIO  = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
    public static readonly Guid MF_MT_INTERLACE_MODE      = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
    public static readonly Guid MF_MT_AVG_BITRATE         = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
    public static readonly Guid MF_MT_MPEG2_PROFILE       = new("ad76a80b-2d5c-4e0b-b375-64e520137036"); // aka MF_MT_VIDEO_PROFILE
    public static readonly Guid MF_MT_DEFAULT_STRIDE      = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");

    // mfidl.h sink writer / transcode
    public static readonly Guid MF_TRANSCODE_CONTAINERTYPE   = new("150ff23f-4abc-478b-ac4f-e1916fba1cca");
    public static readonly Guid MFTranscodeContainerType_MPEG4 = new("dc6cd05d-b9d0-40ef-bd35-fa622c1ab28a");

    public static readonly Guid CLSID_MSH264EncoderMFT = new("6CA50344-051A-4DED-9779-A43305165E35");

    // ---- Structs ------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    public struct MFT_REGISTER_TYPE_INFO
    {
        public Guid guidMajorType;
        public Guid guidSubtype;
    }

    // ---- mfplat.dll ---------------------------------------------------------

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFStartup(uint Version, uint dwFlags);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFShutdown();

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFTEnumEx(
        Guid guidCategory,
        uint Flags,
        MFT_REGISTER_TYPE_INFO* pInputType,
        MFT_REGISTER_TYPE_INFO* pOutputType,
        out IntPtr pppMFTActivate,
        out uint pcMFTActivate);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFCreateMediaType(out IntPtr ppMFType);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFCreateAttributes(out IntPtr ppMFAttributes, uint cInitialSize);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFCreateMemoryBuffer(uint cbMaxLength, out IntPtr ppBuffer);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFCreateSample(out IntPtr ppIMFSample);

    // ---- mfreadwrite.dll ----------------------------------------------------

    [DllImport("mfreadwrite.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    public static extern int MFCreateSinkWriterFromURL(
        [MarshalAs(UnmanagedType.LPWStr)] string pwszOutputURL,
        IntPtr pByteStream,
        IntPtr pAttributes,
        out IntPtr ppSinkWriter);

    // ---- Bit-packing helpers ------------------------------------------------

    public static ulong PackU64(uint hi, uint lo) => ((ulong)hi << 32) | lo;

    // ---- Raw vtable helpers -------------------------------------------------
    //
    // IMFAttributes vtable layout (after IUnknown's QI/AddRef/Release at 0/1/2):
    //   3 GetItem            10 GetGUID            17 GetUnknown        24 SetGUID
    //   4 GetItemType        11 GetStringLength    18 SetItem           25 SetString
    //   5 CompareItem        12 GetString          19 DeleteItem        26 SetBlob
    //   6 Compare            13 GetAllocatedString 20 DeleteAllItems    27 SetUnknown
    //   7 GetUINT32          14 GetBlobSize        21 SetUINT32         28 LockStore
    //   8 GetUINT64          15 GetBlob            22 SetUINT64         29 UnlockStore
    //   9 GetDouble          16 GetAllocatedBlob   23 SetDouble         30 GetCount …

    public static int AttrSetGuid(IntPtr pAttrs, Guid key, Guid value)
    {
        var vtbl = *(void***)pAttrs;
        var fn = (delegate* unmanaged<IntPtr, Guid*, Guid*, int>)vtbl[24];
        return fn(pAttrs, &key, &value);
    }

    public static int AttrSetUInt32(IntPtr pAttrs, Guid key, uint value)
    {
        var vtbl = *(void***)pAttrs;
        var fn = (delegate* unmanaged<IntPtr, Guid*, uint, int>)vtbl[21];
        return fn(pAttrs, &key, value);
    }

    public static int AttrSetUInt64(IntPtr pAttrs, Guid key, ulong value)
    {
        var vtbl = *(void***)pAttrs;
        var fn = (delegate* unmanaged<IntPtr, Guid*, ulong, int>)vtbl[22];
        return fn(pAttrs, &key, value);
    }

    // IMFAttributes:GetGUID / GetAllocatedString — kept for the probe.
    public static Guid AttrGetGuid(IntPtr pAttrs, Guid key)
    {
        // Not used since the probe uses the [ComImport] IMFAttributes; kept for symmetry.
        var vtbl = *(void***)pAttrs;
        var fn = (delegate* unmanaged<IntPtr, Guid*, Guid*, int>)vtbl[10];
        Guid k = key, result;
        int hr = fn(pAttrs, &k, &result);
        return hr >= 0 ? result : Guid.Empty;
    }

    // IMFMediaBuffer vtable (after IUnknown):
    //   3 Lock   4 Unlock   5 GetCurrentLength   6 SetCurrentLength   7 GetMaxLength

    public static int BufferLock(IntPtr pBuffer, out IntPtr ppbBuffer, out uint pcbMaxLength, out uint pcbCurrentLength)
    {
        var vtbl = *(void***)pBuffer;
        var fn = (delegate* unmanaged<IntPtr, IntPtr*, uint*, uint*, int>)vtbl[3];
        IntPtr pb; uint max, cur;
        int hr = fn(pBuffer, &pb, &max, &cur);
        ppbBuffer = pb; pcbMaxLength = max; pcbCurrentLength = cur;
        return hr;
    }

    public static int BufferUnlock(IntPtr pBuffer)
    {
        var vtbl = *(void***)pBuffer;
        var fn = (delegate* unmanaged<IntPtr, int>)vtbl[4];
        return fn(pBuffer);
    }

    public static int BufferSetCurrentLength(IntPtr pBuffer, uint cbCurrentLength)
    {
        var vtbl = *(void***)pBuffer;
        var fn = (delegate* unmanaged<IntPtr, uint, int>)vtbl[6];
        return fn(pBuffer, cbCurrentLength);
    }

    // IMFSample vtable (inherits IMFAttributes => first 30 slots are IMFAttributes,
    // then own methods start at index 33 from the vtable start):
    //   33 GetSampleFlags    36 SetSampleTime      39 GetBufferCount       42 AddBuffer
    //   34 SetSampleFlags    37 GetSampleDuration  40 GetBufferByIndex     43 RemoveBufferByIndex
    //   35 GetSampleTime     38 SetSampleDuration  41 ConvertToContiguousBuffer …

    public static int SampleSetSampleTime(IntPtr pSample, long hnsSampleTime)
    {
        var vtbl = *(void***)pSample;
        var fn = (delegate* unmanaged<IntPtr, long, int>)vtbl[36];
        return fn(pSample, hnsSampleTime);
    }

    public static int SampleSetSampleDuration(IntPtr pSample, long hnsSampleDuration)
    {
        var vtbl = *(void***)pSample;
        var fn = (delegate* unmanaged<IntPtr, long, int>)vtbl[38];
        return fn(pSample, hnsSampleDuration);
    }

    public static int SampleAddBuffer(IntPtr pSample, IntPtr pBuffer)
    {
        var vtbl = *(void***)pSample;
        var fn = (delegate* unmanaged<IntPtr, IntPtr, int>)vtbl[42];
        return fn(pSample, pBuffer);
    }

    // IMFSinkWriter vtable (after IUnknown):
    //   3 AddStream                 7 SendStreamTick     11 FinalizeWriting
    //   4 SetInputMediaType         8 PlaceMarker        12 GetServiceForStream
    //   5 BeginWriting              9 NotifyEndOfSegment 13 GetStatistics
    //   6 WriteSample              10 Flush

    public static int SinkAddStream(IntPtr pSink, IntPtr pTargetMediaType, out uint streamIndex)
    {
        var vtbl = *(void***)pSink;
        var fn = (delegate* unmanaged<IntPtr, IntPtr, uint*, int>)vtbl[3];
        uint idx;
        int hr = fn(pSink, pTargetMediaType, &idx);
        streamIndex = idx;
        return hr;
    }

    public static int SinkSetInputMediaType(IntPtr pSink, uint streamIndex, IntPtr pInputMediaType, IntPtr pEncodingParameters)
    {
        var vtbl = *(void***)pSink;
        var fn = (delegate* unmanaged<IntPtr, uint, IntPtr, IntPtr, int>)vtbl[4];
        return fn(pSink, streamIndex, pInputMediaType, pEncodingParameters);
    }

    public static int SinkBeginWriting(IntPtr pSink)
    {
        var vtbl = *(void***)pSink;
        var fn = (delegate* unmanaged<IntPtr, int>)vtbl[5];
        return fn(pSink);
    }

    public static int SinkWriteSample(IntPtr pSink, uint streamIndex, IntPtr pSample)
    {
        var vtbl = *(void***)pSink;
        var fn = (delegate* unmanaged<IntPtr, uint, IntPtr, int>)vtbl[6];
        return fn(pSink, streamIndex, pSample);
    }

    public static int SinkFinalize(IntPtr pSink)
    {
        var vtbl = *(void***)pSink;
        var fn = (delegate* unmanaged<IntPtr, int>)vtbl[11];
        return fn(pSink);
    }
}

// ---- COM interfaces ---------------------------------------------------------

[ComImport]
[Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFAttributes
{
    [PreserveSig] int GetItem([In] ref Guid guidKey, IntPtr pValue);
    [PreserveSig] int GetItemType([In] ref Guid guidKey, out int pType);
    [PreserveSig] int CompareItem([In] ref Guid guidKey, IntPtr Value, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
    [PreserveSig] int Compare([MarshalAs(UnmanagedType.Interface)] IMFAttributes pTheirs, int MatchType, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
    [PreserveSig] int GetUINT32([In] ref Guid guidKey, out uint punValue);
    [PreserveSig] int GetUINT64([In] ref Guid guidKey, out ulong punValue);
    [PreserveSig] int GetDouble([In] ref Guid guidKey, out double pfValue);
    [PreserveSig] int GetGUID([In] ref Guid guidKey, out Guid pguidValue);
    [PreserveSig] int GetStringLength([In] ref Guid guidKey, out uint pcchLength);
    [PreserveSig] int GetString([In] ref Guid guidKey, IntPtr pwszValue, uint cchBufSize, IntPtr pcchLength);
    [PreserveSig] int GetAllocatedString([In] ref Guid guidKey, out IntPtr ppwszValue, out uint pcchLength);
    [PreserveSig] int GetBlobSize([In] ref Guid guidKey, out uint pcbBlobSize);
    [PreserveSig] int GetBlob([In] ref Guid guidKey, IntPtr pBuf, uint cbBufSize, IntPtr pcbBlobSize);
    [PreserveSig] int GetAllocatedBlob([In] ref Guid guidKey, out IntPtr ppBuf, out uint pcbSize);
    [PreserveSig] int GetUnknown([In] ref Guid guidKey, [In] ref Guid riid, out IntPtr ppv);
    [PreserveSig] int SetItem([In] ref Guid guidKey, IntPtr Value);
    [PreserveSig] int DeleteItem([In] ref Guid guidKey);
    [PreserveSig] int DeleteAllItems();
    [PreserveSig] int SetUINT32([In] ref Guid guidKey, uint unValue);
    [PreserveSig] int SetUINT64([In] ref Guid guidKey, ulong unValue);
    [PreserveSig] int SetDouble([In] ref Guid guidKey, double fValue);
    [PreserveSig] int SetGUID([In] ref Guid guidKey, [In] ref Guid guidValue);
    [PreserveSig] int SetString([In] ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
    [PreserveSig] int SetBlob([In] ref Guid guidKey, IntPtr pBuf, uint cbBufSize);
    [PreserveSig] int SetUnknown([In] ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object? pUnknown);
    [PreserveSig] int LockStore();
    [PreserveSig] int UnlockStore();
    [PreserveSig] int GetCount(out uint pcItems);
    [PreserveSig] int GetItemByIndex(uint unIndex, out Guid pguidKey, IntPtr pValue);
    [PreserveSig] int CopyAllItems([MarshalAs(UnmanagedType.Interface)] IMFAttributes pDest);
}

[ComImport]
[Guid("7FEE9E9A-4A89-47a6-899C-B6A53A70FB67")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFActivate
{
    [PreserveSig] int GetItem([In] ref Guid guidKey, IntPtr pValue);
    [PreserveSig] int GetItemType([In] ref Guid guidKey, out int pType);
    [PreserveSig] int CompareItem([In] ref Guid guidKey, IntPtr Value, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
    [PreserveSig] int Compare([MarshalAs(UnmanagedType.Interface)] IMFAttributes pTheirs, int MatchType, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
    [PreserveSig] int GetUINT32([In] ref Guid guidKey, out uint punValue);
    [PreserveSig] int GetUINT64([In] ref Guid guidKey, out ulong punValue);
    [PreserveSig] int GetDouble([In] ref Guid guidKey, out double pfValue);
    [PreserveSig] int GetGUID([In] ref Guid guidKey, out Guid pguidValue);
    [PreserveSig] int GetStringLength([In] ref Guid guidKey, out uint pcchLength);
    [PreserveSig] int GetString([In] ref Guid guidKey, IntPtr pwszValue, uint cchBufSize, IntPtr pcchLength);
    [PreserveSig] int GetAllocatedString([In] ref Guid guidKey, out IntPtr ppwszValue, out uint pcchLength);
    [PreserveSig] int GetBlobSize([In] ref Guid guidKey, out uint pcbBlobSize);
    [PreserveSig] int GetBlob([In] ref Guid guidKey, IntPtr pBuf, uint cbBufSize, IntPtr pcbBlobSize);
    [PreserveSig] int GetAllocatedBlob([In] ref Guid guidKey, out IntPtr ppBuf, out uint pcbSize);
    [PreserveSig] int GetUnknown([In] ref Guid guidKey, [In] ref Guid riid, out IntPtr ppv);
    [PreserveSig] int SetItem([In] ref Guid guidKey, IntPtr Value);
    [PreserveSig] int DeleteItem([In] ref Guid guidKey);
    [PreserveSig] int DeleteAllItems();
    [PreserveSig] int SetUINT32([In] ref Guid guidKey, uint unValue);
    [PreserveSig] int SetUINT64([In] ref Guid guidKey, ulong unValue);
    [PreserveSig] int SetDouble([In] ref Guid guidKey, double fValue);
    [PreserveSig] int SetGUID([In] ref Guid guidKey, [In] ref Guid guidValue);
    [PreserveSig] int SetString([In] ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
    [PreserveSig] int SetBlob([In] ref Guid guidKey, IntPtr pBuf, uint cbBufSize);
    [PreserveSig] int SetUnknown([In] ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object? pUnknown);
    [PreserveSig] int LockStore();
    [PreserveSig] int UnlockStore();
    [PreserveSig] int GetCount(out uint pcItems);
    [PreserveSig] int GetItemByIndex(uint unIndex, out Guid pguidKey, IntPtr pValue);
    [PreserveSig] int CopyAllItems([MarshalAs(UnmanagedType.Interface)] IMFAttributes pDest);

    [PreserveSig] int ActivateObject([In] ref Guid riid, out IntPtr ppv);
    [PreserveSig] int ShutdownObject();
    [PreserveSig] int DetachObject();
}

