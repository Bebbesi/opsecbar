using System.Runtime.InteropServices;
using OpSecBar.Core;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using Windows.Networking.Connectivity;

namespace OpSecBar.Services;

/// <summary>Stato delle connessioni senza fili, come lo mostrano le impostazioni rapide.</summary>
public sealed record RadioSnapshot
{
    public bool HasWifi { get; init; }
    public bool WifiOn { get; init; }
    /// <summary>Nome della rete Wi-Fi collegata; null se non collegati (o se Windows non lo rivela).</summary>
    public string? WifiNetwork { get; init; }
    public bool HasBluetooth { get; init; }
    public bool BluetoothOn { get; init; }
    /// <summary>Nome del dispositivo Bluetooth collegato (il primo, se più d'uno).</summary>
    public string? BluetoothDevice { get; init; }
    public bool AirplaneMode { get; init; }
    /// <summary>C'è una connessione a Internet (anche via cavo).</summary>
    public bool Online { get; init; }
    /// <summary>Nome della connessione in uso quando non è Wi-Fi (es. la rete Ethernet).</summary>
    public string? WiredNetwork { get; init; }
}

/// <summary>
/// Wi-Fi, Bluetooth e modalità aereo: lettura e accensione/spegnimento (API Windows.Devices.Radios, le stesse usate
/// dalle Impostazioni di Windows).
/// </summary>
public static class RadioService
{
    private static bool _accessAsked;
    private static List<RadioKind> _beforeAirplane = new();

    private static async Task<IReadOnlyList<Radio>> RadiosAsync()
    {
        if (!_accessAsked)
        {
            _accessAsked = true;
            try { await Radio.RequestAccessAsync(); } catch (Exception ex) { Log.Warn("Accesso alle radio: " + ex.Message); }
        }
        return await Radio.GetRadiosAsync();
    }

    public static async Task<RadioSnapshot> ReadAsync()
    {
        IReadOnlyList<Radio> radios;
        try { radios = await RadiosAsync(); }
        catch (Exception ex)
        {
            Log.Warn("Lettura delle radio: " + ex.Message);
            radios = Array.Empty<Radio>();
        }

        var wifi = radios.Where(r => r.Kind == RadioKind.WiFi).ToList();
        var bluetooth = radios.Where(r => r.Kind == RadioKind.Bluetooth).ToList();
        bool wifiOn = wifi.Any(r => r.State == RadioState.On);
        bool bluetoothOn = bluetooth.Any(r => r.State == RadioState.On);

        string? wifiNetwork = null, wired = null;
        bool online = false;
        try
        {
            ConnectionProfile? profile = NetworkInformation.GetInternetConnectionProfile();
            if (profile != null)
            {
                online = profile.GetNetworkConnectivityLevel() >= NetworkConnectivityLevel.LocalAccess;
                if (profile.IsWlanConnectionProfile)
                {
                    try { wifiNetwork = profile.WlanConnectionProfileDetails?.GetConnectedSsid(); } catch { }
                    if (string.IsNullOrWhiteSpace(wifiNetwork)) wifiNetwork = profile.ProfileName;
                }
                else wired = profile.ProfileName;
            }
        }
        catch (Exception ex) { Log.Warn("Stato della rete: " + ex.Message); }

        return new RadioSnapshot
        {
            HasWifi = wifi.Count > 0, WifiOn = wifiOn, WifiNetwork = wifiOn ? wifiNetwork : null,
            HasBluetooth = bluetooth.Count > 0, BluetoothOn = bluetoothOn,
            BluetoothDevice = bluetoothOn ? await ConnectedBluetoothDeviceAsync() : null,
            // Il gestore radio è un oggetto COM fuori processo: lo si interroga lontano dal thread dell'interfaccia.
            AirplaneMode = await Task.Run(ReadAirplaneMode) ?? (radios.Count > 0 && radios.All(r => r.State != RadioState.On)),
            Online = online, WiredNetwork = wired
        };
    }

    private static async Task<string?> ConnectedBluetoothDeviceAsync()
    {
        try
        {
            foreach (string selector in new[]
                     {
                         BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected),
                         BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected)
                     })
            {
                DeviceInformationCollection devices = await DeviceInformation.FindAllAsync(selector);
                string? name = devices.Select(d => d.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
                if (name != null) return name;
            }
        }
        catch (Exception ex) { Log.Warn("Dispositivi Bluetooth collegati: " + ex.Message); }
        return null;
    }

    /// <summary>Accende o spegne tutte le radio di un tipo. False se Windows non lo ha permesso.</summary>
    public static async Task<bool> SetAsync(RadioKind kind, bool on)
    {
        bool ok = true;
        try
        {
            foreach (Radio radio in (await RadiosAsync()).Where(r => r.Kind == kind))
            {
                RadioAccessStatus status = await radio.SetStateAsync(on ? RadioState.On : RadioState.Off);
                if (status != RadioAccessStatus.Allowed)
                {
                    ok = false;
                    Log.Warn($"Radio {kind}: {status}");
                }
            }
        }
        catch (Exception ex)
        {
            ok = false;
            Log.Warn($"Radio {kind}: {ex.Message}");
        }
        return ok;
    }

    // ---- Modalità aereo --------------------------------------------------------------------------
    // Windows non ha un'API pubblica per la modalità aereo: si usa il gestore radio di sistema (lo stesso oggetto COM
    // del pannello di Windows). Se non è disponibile si ripiega sullo spegnere, e poi riaccendere, le singole radio.

    public static async Task SetAirplaneModeAsync(bool on)
    {
        if (SetSystemRadios(!on)) return;

        IReadOnlyList<Radio> radios;
        try { radios = await RadiosAsync(); } catch { return; }
        if (on)
        {
            _beforeAirplane = radios.Where(r => r.State == RadioState.On).Select(r => r.Kind).Distinct().ToList();
            foreach (RadioKind kind in _beforeAirplane) await SetAsync(kind, false);
        }
        else
        {
            var kinds = _beforeAirplane.Count > 0 ? _beforeAirplane : radios.Select(r => r.Kind).Distinct().ToList();
            foreach (RadioKind kind in kinds) await SetAsync(kind, true);
            _beforeAirplane = new List<RadioKind>();
        }
    }

    /// <summary>True = modalità aereo attiva; null se il gestore radio di sistema non risponde.</summary>
    private static bool? ReadAirplaneMode()
    {
        IRadioManager? manager = null;
        try
        {
            manager = (IRadioManager)new RadioManagementApi();
            return manager.GetSystemRadioState(out int enabled, out _, out _) == 0 ? enabled == 0 : null;
        }
        catch { return null; }
        finally { if (manager != null) Marshal.ReleaseComObject(manager); }
    }

    private static bool SetSystemRadios(bool enabled)
    {
        IRadioManager? manager = null;
        try
        {
            manager = (IRadioManager)new RadioManagementApi();
            int hr = manager.SetSystemRadioState(enabled ? 1 : 0);
            if (hr != 0) Log.Warn($"Modalità aereo: 0x{hr:X8}");
            return hr == 0;
        }
        catch (Exception ex)
        {
            Log.Warn("Modalità aereo: " + ex.Message);
            return false;
        }
        finally { if (manager != null) Marshal.ReleaseComObject(manager); }
    }

    [ComImport, Guid("581333F6-28DB-41BE-BC7A-FF201F12F3F6")]
    private class RadioManagementApi { }

    [ComImport, Guid("DB3AFBFB-08E6-46C6-AA70-BF9A34C30AB7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IRadioManager
    {
        [PreserveSig] int IsRMSupported(out int supported);
        [PreserveSig] int GetUIRadioInstances(out IntPtr instances);
        /// <param name="enabled">1 = radio accese (modalità aereo spenta), 0 = modalità aereo attiva.</param>
        [PreserveSig] int GetSystemRadioState(out int enabled, out int unknown, out int changeReason);
        [PreserveSig] int SetSystemRadioState(int enabled);
        [PreserveSig] int Refresh();
        [PreserveSig] int OnHardwareSliderChange(int a, int b);
    }
}
