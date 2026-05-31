using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using WipShare.Client.Native;
using static WipShare.Client.Native.MediaFoundationInterop;

namespace WipShare.Client.Encoding;

public enum EncoderKind
{
    Nvidia,
    Amd,
    Intel,
    Microsoft,
    Unknown,
}

public sealed record EncoderInfo(EncoderKind Kind, string FriendlyName, Guid Clsid, bool IsHardware);

/// <summary>
/// Enumerates H.264 encoder MFTs via MFTEnumEx and picks the best one in the order
/// NVIDIA → AMD → Intel → Microsoft software. Logs every candidate so the log file
/// always shows which encoder a recording actually used.
/// </summary>
public sealed class HardwareEncoderProbe
{
    private readonly ILogger<HardwareEncoderProbe> _logger;

    public HardwareEncoderProbe(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<HardwareEncoderProbe>();
    }

    public EncoderInfo Select()
    {
        var hardware = Enumerate(hardware: true);
        foreach (var c in hardware)
            _logger.LogInformation("Hardware H.264 encoder candidate: '{Name}' CLSID={Clsid} → {Kind}",
                c.FriendlyName, c.Clsid, c.Kind);

        foreach (var pref in new[] { EncoderKind.Nvidia, EncoderKind.Amd, EncoderKind.Intel })
        {
            var match = hardware.FirstOrDefault(e => e.Kind == pref);
            if (match is not null)
                return match;
        }

        var software = Enumerate(hardware: false);
        foreach (var c in software)
            _logger.LogInformation("Software H.264 encoder candidate: '{Name}' CLSID={Clsid} → {Kind}",
                c.FriendlyName, c.Clsid, c.Kind);

        var ms = software.FirstOrDefault(e => e.Kind == EncoderKind.Microsoft)
                 ?? software.FirstOrDefault();
        if (ms is null)
            throw new InvalidOperationException("No H.264 encoder MFT registered on this system");
        return ms;
    }

    private unsafe List<EncoderInfo> Enumerate(bool hardware)
    {
        var outputType = new MFT_REGISTER_TYPE_INFO
        {
            guidMajorType = MFMediaType_Video,
            guidSubtype = MFVideoFormat_H264,
        };

        uint flags = MFT_ENUM_FLAG_SORTANDFILTER;
        if (hardware)
            flags |= MFT_ENUM_FLAG_HARDWARE;
        else
            flags |= MFT_ENUM_FLAG_SYNCMFT | MFT_ENUM_FLAG_ASYNCMFT;

        int hr = MFTEnumEx(
            MFT_CATEGORY_VIDEO_ENCODER,
            flags,
            null,
            &outputType,
            out IntPtr pppActivates,
            out uint count);
        if (hr < 0)
            Marshal.ThrowExceptionForHR(hr);

        var result = new List<EncoderInfo>((int)count);
        try
        {
            for (uint i = 0; i < count; i++)
            {
                IntPtr pAct = Marshal.ReadIntPtr(pppActivates, (int)(i * (uint)IntPtr.Size));
                if (pAct == IntPtr.Zero) continue;

                object? rcw = null;
                try
                {
                    rcw = Marshal.GetObjectForIUnknown(pAct);
                    if (rcw is not IMFAttributes attrs)
                    {
                        _logger.LogWarning("Activate #{I}: did not QI to IMFAttributes (rcw={Type})", i, rcw?.GetType().FullName ?? "null");
                        continue;
                    }

                    var (name, _) = ReadFriendlyName(attrs);
                    var (clsid, _) = ReadClsid(attrs);
                    var friendly = name ?? "(unnamed)";
                    result.Add(new EncoderInfo(Classify(friendly, clsid, hardware), friendly, clsid, hardware));
                }
                finally
                {
                    if (rcw != null) Marshal.ReleaseComObject(rcw);
                    Marshal.Release(pAct);
                }
            }
        }
        finally
        {
            if (pppActivates != IntPtr.Zero)
                Marshal.FreeCoTaskMem(pppActivates);
        }
        return result;
    }

    private static (string? Value, int Hr) ReadFriendlyName(IMFAttributes attrs)
    {
        Guid key = MFT_FRIENDLY_NAME_Attribute;
        int hr = attrs.GetAllocatedString(ref key, out IntPtr pStr, out uint cch);
        if (hr < 0 || pStr == IntPtr.Zero) return (null, hr);
        try { return (Marshal.PtrToStringUni(pStr, (int)cch), hr); }
        finally { Marshal.FreeCoTaskMem(pStr); }
    }

    private static (Guid Value, int Hr) ReadClsid(IMFAttributes attrs)
    {
        Guid key = MFT_TRANSFORM_CLSID_Attribute;
        int hr = attrs.GetGUID(ref key, out Guid value);
        return (hr >= 0 ? value : Guid.Empty, hr);
    }

    private static EncoderKind Classify(string name, Guid clsid, bool isHardware)
    {
        if (clsid == CLSID_MSH264EncoderMFT) return EncoderKind.Microsoft;

        if (Contains(name, "NVIDIA") || Contains(name, "NVENC"))
            return EncoderKind.Nvidia;
        if (Contains(name, "AMD") || Contains(name, "AMF") || Contains(name, "Radeon"))
            return EncoderKind.Amd;
        if (Contains(name, "Intel") || Contains(name, "QuickSync") || Contains(name, "Quick Sync"))
            return EncoderKind.Intel;
        if (Contains(name, "Microsoft"))
            return EncoderKind.Microsoft;

        // Any unidentified software encoder is almost certainly Microsoft's.
        return isHardware ? EncoderKind.Unknown : EncoderKind.Microsoft;

        static bool Contains(string s, string needle) =>
            s.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }
}
