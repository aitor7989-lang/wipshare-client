using System.Runtime.InteropServices;

namespace WipShare.Client.Native;

/// <summary>
/// Owns the process-wide MFStartup / MFShutdown lifecycle. Construct once at app
/// start, dispose at exit. MF reference-counts internally so a single Startup is
/// enough for all of WipShare's MF use (probe + sink writer).
/// </summary>
public sealed class MfRuntime : IDisposable
{
    private bool _started;

    public MfRuntime()
    {
        int hr = MediaFoundationInterop.MFStartup(
            MediaFoundationInterop.MF_VERSION,
            MediaFoundationInterop.MFSTARTUP_FULL);
        if (hr < 0)
            Marshal.ThrowExceptionForHR(hr);
        _started = true;
    }

    public void Dispose()
    {
        if (!_started) return;
        _started = false;
        // Ignore the HRESULT - shutdown failures during teardown have nowhere useful to go.
        MediaFoundationInterop.MFShutdown();
    }
}
