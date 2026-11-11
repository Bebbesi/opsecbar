using System.Runtime.InteropServices;
using System.Windows.Threading;
using OpSecBar.Core;

namespace OpSecBar.Services;

/// <summary>
/// Volume principale del dispositivo di riproduzione predefinito (Core Audio, IAudioEndpointVolume).
/// Lo stato viene letto con un timer leggero, così segue anche le modifiche fatte da Windows o da altre app.
/// </summary>
public sealed class AudioService
{
    private readonly DispatcherTimer _timer;
    private IAudioEndpointVolume? _endpoint;
    private int _ticks;

    /// <summary>Volume 0–100.</summary>
    public int Volume { get; private set; } = -1;
    public bool Muted { get; private set; }
    public bool Available => _endpoint != null;

    public event Action? Changed;

    public AudioService(Dispatcher dispatcher)
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(900) };
        _timer.Tick += (_, _) => Poll();
    }

    public void SetEnabled(bool on)
    {
        if (on)
        {
            Poll();
            _timer.Start();
        }
        else
        {
            _timer.Stop();
            Release();
        }
    }

    public void SetVolume(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        Run(e =>
        {
            Guid ctx = Guid.Empty;
            e.SetMasterVolumeLevelScalar(percent / 100f, ref ctx);
            if (percent > 0 && Muted) e.SetMute(false, ref ctx);
        });
        Poll();
    }

    public void ToggleMute()
    {
        Run(e =>
        {
            Guid ctx = Guid.Empty;
            e.SetMute(!Muted, ref ctx);
        });
        Poll();
    }

    private void Poll()
    {
        // Il dispositivo predefinito può cambiare (cuffie, HDMI...): ogni tanto lo si richiede da capo.
        if (++_ticks % 6 == 0) Release();
        int volume = -1;
        bool muted = false;
        Run(e =>
        {
            if (e.GetMasterVolumeLevelScalar(out float level) == 0) volume = (int)Math.Round(level * 100);
            if (e.GetMute(out bool m) == 0) muted = m;
        });
        if (volume == Volume && muted == Muted) return;
        Volume = volume;
        Muted = muted;
        Changed?.Invoke();
    }

    private void Run(Action<IAudioEndpointVolume> action)
    {
        try
        {
            _endpoint ??= Acquire();
            if (_endpoint != null) action(_endpoint);
        }
        catch (Exception ex)
        {
            Log.Warn("Audio: " + ex.Message);
            Release();
        }
    }

    private static IAudioEndpointVolume? Acquire()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
        try
        {
            if (enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out IMMDevice? device) != 0 || device == null) return null;
            try
            {
                Guid iid = typeof(IAudioEndpointVolume).GUID;
                if (device.Activate(ref iid, 0x17 /* CLSCTX_ALL */, IntPtr.Zero, out object? o) != 0) return null;
                return o as IAudioEndpointVolume;
            }
            finally { Marshal.ReleaseComObject(device); }
        }
        finally { Marshal.ReleaseComObject(enumerator); }
    }

    private void Release()
    {
        if (_endpoint == null) return;
        try { Marshal.ReleaseComObject(_endpoint); } catch { }
        _endpoint = null;
    }

    // ---- COM -------------------------------------------------------------------------------

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice? device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object? iface);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid ctx);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid ctx);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid ctx);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
