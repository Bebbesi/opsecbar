using System.Runtime.InteropServices;
using System.Windows.Threading;
using OpSecBar.Core;

namespace OpSecBar.Services;

/// <summary>
/// Volume principale del dispositivo di riproduzione predefinito (Core Audio, IAudioEndpointVolume).
/// Lo stato arriva dalle notifiche di Core Audio (volume, mute, cambio del dispositivo predefinito), così segue anche
/// le modifiche fatte da Windows o da altre app senza interrogare il dispositivo di continuo. Un timer lento resta
/// come rete di sicurezza; se le notifiche non si possono registrare si torna alla lettura periodica.
/// </summary>
public sealed partial class AudioService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(900), SafetyInterval = TimeSpan.FromSeconds(20);

    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timer;
    private readonly VolumeCallback _volumeCallback;
    private readonly DeviceCallback _deviceCallback;
    private IAudioEndpointVolume? _endpoint;
    private IMMDeviceEnumerator? _enumerator; // tenuto vivo: è lui a consegnare le notifiche sui dispositivi
    private bool _volumeEvents, _deviceEvents;
    private int _ticks, _pollQueued;

    /// <summary>Volume 0–100.</summary>
    public int Volume { get; private set; } = -1;
    public bool Muted { get; private set; }
    public bool Available => _endpoint != null;

    public event Action? Changed;

    public AudioService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = PollInterval };
        _timer.Tick += (_, _) => Poll();
        _volumeCallback = new VolumeCallback(this);
        _deviceCallback = new DeviceCallback(this);
    }

    /// <summary>Notifica di Core Audio (arriva su un thread suo): la lettura vera avviene sul thread della UI, una per volta.</summary>
    private void QueuePoll(bool deviceChanged)
    {
        if (deviceChanged)
        {
            _dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                Release(); // il volume va riletto dal nuovo dispositivo
                Poll();
            });
            return;
        }
        if (Interlocked.Exchange(ref _pollQueued, 1) == 1) return;
        _dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            Interlocked.Exchange(ref _pollQueued, 0);
            Poll();
        });
    }

    private sealed class VolumeCallback(AudioService owner) : IAudioEndpointVolumeCallback
    {
        public int OnNotify(IntPtr data)
        {
            owner.QueuePoll(false);
            return 0;
        }
    }

    private sealed class DeviceCallback(AudioService owner) : IMMNotificationClient
    {
        public int OnDeviceStateChanged(string deviceId, int newState) => 0;
        public int OnDeviceAdded(string deviceId) => 0;
        public int OnDeviceRemoved(string deviceId) => 0;

        public int OnDefaultDeviceChanged(int flow, int role, string? defaultDeviceId)
        {
            if (flow == 0 /* eRender */ && role == 1 /* eMultimedia */) owner.QueuePoll(true);
            return 0;
        }

        public int OnPropertyValueChanged(string deviceId, PROPERTYKEY key) => 0;
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
            ReleaseEnumerator();
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
        // Senza notifiche il dispositivo predefinito può cambiare a nostra insaputa (cuffie, HDMI...): ogni tanto lo si
        // richiede da capo.
        if (!_deviceEvents && ++_ticks % 6 == 0) Release();
        int volume = -1;
        bool muted = false;
        Run(e =>
        {
            if (e.GetMasterVolumeLevelScalar(out float level) == 0) volume = (int)Math.Round(level * 100);
            if (e.GetMute(out bool m) == 0) muted = m;
        });
        // Con le notifiche attive il timer è solo una rete di sicurezza; senza, si torna alla lettura frequente.
        TimeSpan interval = _volumeEvents && _deviceEvents ? SafetyInterval : PollInterval;
        if (_timer.Interval != interval) _timer.Interval = interval;
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

    private IAudioEndpointVolume? Acquire()
    {
        if (_enumerator == null)
        {
            _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            try { _deviceEvents = _enumerator.RegisterEndpointNotificationCallback(_deviceCallback) == 0; }
            catch { _deviceEvents = false; }
        }
        if (_enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out IMMDevice? device) != 0 || device == null) return null;
        try
        {
            Guid iid = typeof(IAudioEndpointVolume).GUID;
            if (device.Activate(ref iid, 0x17 /* CLSCTX_ALL */, IntPtr.Zero, out object? o) != 0) return null;
            var endpoint = o as IAudioEndpointVolume;
            if (endpoint != null)
            {
                try { _volumeEvents = endpoint.RegisterControlChangeNotify(_volumeCallback) == 0; }
                catch { _volumeEvents = false; }
            }
            return endpoint;
        }
        finally { Marshal.ReleaseComObject(device); }
    }

    private void Release()
    {
        if (_endpoint == null) return;
        try
        {
            if (_volumeEvents) _endpoint.UnregisterControlChangeNotify(_volumeCallback);
        }
        catch { }
        _volumeEvents = false;
        try { Marshal.ReleaseComObject(_endpoint); } catch { }
        _endpoint = null;
    }

    private void ReleaseEnumerator()
    {
        if (_enumerator == null) return;
        try
        {
            if (_deviceEvents) _enumerator.UnregisterEndpointNotificationCallback(_deviceCallback);
        }
        catch { }
        _deviceEvents = false;
        try { Marshal.ReleaseComObject(_enumerator); } catch { }
        _enumerator = null;
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
        [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
    }

    [ComImport, Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMNotificationClient
    {
        [PreserveSig] int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int newState);
        [PreserveSig] int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        [PreserveSig] int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        [PreserveSig] int OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string? defaultDeviceId);
        [PreserveSig] int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, PROPERTYKEY key);
    }

    [ComImport, Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolumeCallback
    {
        [PreserveSig] int OnNotify(IntPtr data);
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
        [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
        [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
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
