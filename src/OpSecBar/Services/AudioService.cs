using System.Runtime.InteropServices;
using System.Windows.Threading;
using OpSecBar.Core;

namespace OpSecBar.Services;

/// <summary>
/// Volume principale del dispositivo di riproduzione predefinito (Core Audio, IAudioEndpointVolume).
/// Lo stato viene letto con un timer leggero, così segue anche le modifiche fatte da Windows o da altre app.
/// </summary>
public sealed partial class AudioService
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

    /// <summary>Legge subito volume e mute (anche se il widget del volume è spento).</summary>
    public void PollNow() => Poll();

    // ---- Dispositivi di uscita -----------------------------------------------------------------

    /// <summary>Un dispositivo di riproduzione.</summary>
    public sealed record AudioDevice(string Id, string Name, bool IsDefault);

    /// <summary>I dispositivi di riproduzione attivi (altoparlanti, cuffie, HDMI...), con quello predefinito segnato.</summary>
    public List<AudioDevice> GetOutputDevices()
    {
        var list = new List<AudioDevice>();
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            try
            {
                string? defaultId = null;
                if (enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out IMMDevice? current) == 0 && current != null)
                {
                    try { current.GetId(out defaultId); }
                    finally { Marshal.ReleaseComObject(current); }
                }

                if (enumerator.EnumAudioEndpoints(0 /* eRender */, 1 /* DEVICE_STATE_ACTIVE */, out IMMDeviceCollection? devices) != 0 || devices == null) return list;
                try
                {
                    devices.GetCount(out uint count);
                    for (uint i = 0; i < count; i++)
                    {
                        if (devices.Item(i, out IMMDevice? device) != 0 || device == null) continue;
                        try
                        {
                            if (device.GetId(out string? id) != 0 || string.IsNullOrEmpty(id)) continue;
                            list.Add(new AudioDevice(id, FriendlyName(device) ?? "Audio device", id == defaultId));
                        }
                        finally { Marshal.ReleaseComObject(device); }
                    }
                }
                finally { Marshal.ReleaseComObject(devices); }
            }
            finally { Marshal.ReleaseComObject(enumerator); }
        }
        catch (Exception ex) { Log.Warn("Elenco dispositivi audio: " + ex.Message); }
        return list.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Rende predefinito un dispositivo di uscita (come il selettore nelle impostazioni rapide di Windows).</summary>
    public bool SetDefaultOutput(string deviceId)
    {
        try
        {
            var policy = (IPolicyConfig)new PolicyConfigClient();
            try
            {
                int hr = policy.SetDefaultEndpoint(deviceId, 0 /* eConsole */);
                if (hr == 0) hr = policy.SetDefaultEndpoint(deviceId, 1 /* eMultimedia */);
                if (hr != 0) Log.Warn($"Dispositivo audio predefinito: 0x{hr:X8}");
                return hr == 0;
            }
            finally { Marshal.ReleaseComObject(policy); }
        }
        catch (Exception ex)
        {
            Log.Warn("Dispositivo audio predefinito: " + ex.Message);
            return false;
        }
        finally
        {
            Release(); // il volume va riletto dal nuovo dispositivo
            Poll();
        }
    }

    private static string? FriendlyName(IMMDevice device)
    {
        if (device.OpenPropertyStore(0 /* STGM_READ */, out IPropertyStore? store) != 0 || store == null) return null;
        try
        {
            var key = new PROPERTYKEY { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 }; // PKEY_Device_FriendlyName
            if (store.GetValue(ref key, out PROPVARIANT value) != 0) return null;
            try { return value.vt == 31 /* VT_LPWSTR */ ? Marshal.PtrToStringUni(value.pointer) : null; }
            finally { PropVariantClear(ref value); }
        }
        finally { Marshal.ReleaseComObject(store); }
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
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection? devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice? device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice? device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object? iface);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore? store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string? id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice? device);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY { public Guid fmtid; public uint pid; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPVARIANT { public ushort vt, reserved1, reserved2, reserved3; public IntPtr pointer; public IntPtr extra; }

    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PROPVARIANT value);

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
    }

    // Interfaccia non documentata ma stabile da Windows 7: è quella che usa il pannello Audio per cambiare il dispositivo
    // predefinito. Dei metodi serve solo SetDefaultEndpoint; gli altri occupano il loro posto nella tabella.
    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    private class PolicyConfigClient { }

    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(IntPtr id, IntPtr format);
        [PreserveSig] int GetDeviceFormat(IntPtr id, int isDefault, IntPtr format);
        [PreserveSig] int ResetDeviceFormat(IntPtr id);
        [PreserveSig] int SetDeviceFormat(IntPtr id, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod(IntPtr id, int isDefault, IntPtr defaultPeriod, IntPtr minimumPeriod);
        [PreserveSig] int SetProcessingPeriod(IntPtr id, IntPtr period);
        [PreserveSig] int GetShareMode(IntPtr id, IntPtr mode);
        [PreserveSig] int SetShareMode(IntPtr id, IntPtr mode);
        [PreserveSig] int GetPropertyValue(IntPtr id, int store, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue(IntPtr id, int store, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
        [PreserveSig] int SetEndpointVisibility(IntPtr id, int visible);
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
