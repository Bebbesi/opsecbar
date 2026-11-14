using System.Runtime.InteropServices;
using OpSecBar.Core;

namespace OpSecBar.Services;

// Cuffie e altoparlanti Bluetooth: Windows non ha un'API pubblica "connetti questo dispositivo". Il driver audio
// Bluetooth però espone due comandi (riconnetti / disconnetti) sul proprio filtro: sono quelli che usa anche il pannello
// di Windows. Li si raggiunge dal dispositivo audio, risalendo al filtro a cui è collegato.
public sealed partial class AudioService
{
    private static readonly Guid BluetoothAudioPropertySet = new("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D");
    private const uint OneShotReconnect = 0, OneShotDisconnect = 1;

    /// <summary>Contenitori (= dispositivi fisici) che hanno almeno un'uscita o un ingresso audio Bluetooth, anche scollegati.</summary>
    public static HashSet<Guid> BluetoothAudioContainers()
    {
        var containers = new HashSet<Guid>();
        ForEachBluetoothAudioFilter((container, _, _) => containers.Add(container));
        return containers;
    }

    /// <summary>Collega o scollega l'audio di un dispositivo Bluetooth già associato. False se non ha audio o il driver rifiuta.</summary>
    public static bool SetBluetoothAudioConnected(Guid containerId, bool connect)
    {
        bool any = false, ok = true;
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ForEachBluetoothAudioFilter((container, filterId, enumerator) =>
        {
            // Lo stesso filtro serve più dispositivi audio (es. cuffie + microfono): il comando va dato una volta sola.
            if (container != containerId || !done.Add(filterId)) return;
            any = true;
            if (enumerator.GetDevice(filterId, out IMMDevice? filter) != 0 || filter == null)
            {
                ok = false;
                return;
            }
            try
            {
                Guid iid = typeof(IKsControl).GUID;
                if (filter.Activate(ref iid, 0x17 /* CLSCTX_ALL */, IntPtr.Zero, out object? o) != 0 || o is not IKsControl control)
                {
                    ok = false;
                    return;
                }
                try
                {
                    var property = new KSPROPERTY { Set = BluetoothAudioPropertySet, Id = connect ? OneShotReconnect : OneShotDisconnect, Flags = 1 /* GET */ };
                    int hr = control.KsProperty(ref property, (uint)Marshal.SizeOf<KSPROPERTY>(), IntPtr.Zero, 0, out _);
                    if (hr != 0)
                    {
                        ok = false;
                        Log.Warn($"Audio Bluetooth ({(connect ? "collega" : "scollega")}): 0x{hr:X8}");
                    }
                }
                finally { Marshal.ReleaseComObject(control); }
            }
            finally { Marshal.ReleaseComObject(filter); }
        });
        return any && ok;
    }

    /// <summary>Visita i dispositivi audio (attivi o scollegati) il cui filtro è del driver Bluetooth.</summary>
    private static void ForEachBluetoothAudioFilter(Action<Guid, string, IMMDeviceEnumerator> visit)
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            try
            {
                // eAll, DEVICE_STATE_ACTIVE | DEVICE_STATE_UNPLUGGED: un dispositivo Bluetooth scollegato risulta "unplugged".
                if (enumerator.EnumAudioEndpoints(2, 1 | 8, out IMMDeviceCollection? devices) != 0 || devices == null) return;
                try
                {
                    devices.GetCount(out uint count);
                    for (uint i = 0; i < count; i++)
                    {
                        if (devices.Item(i, out IMMDevice? device) != 0 || device == null) continue;
                        try
                        {
                            string? filterId = ConnectedFilterId(device);
                            if (filterId == null || !IsBluetoothFilter(filterId)) continue;
                            if (ContainerId(device) is { } container) visit(container, filterId, enumerator);
                        }
                        finally { Marshal.ReleaseComObject(device); }
                    }
                }
                finally { Marshal.ReleaseComObject(devices); }
            }
            finally { Marshal.ReleaseComObject(enumerator); }
        }
        catch (Exception ex) { Log.Warn("Dispositivi audio Bluetooth: " + ex.Message); }
    }

    /// <summary>Identificativo del filtro (driver) a cui è collegato il dispositivo audio.</summary>
    private static string? ConnectedFilterId(IMMDevice device)
    {
        Guid iid = typeof(IDeviceTopology).GUID;
        if (device.Activate(ref iid, 0x17 /* CLSCTX_ALL */, IntPtr.Zero, out object? o) != 0 || o is not IDeviceTopology topology) return null;
        try
        {
            if (topology.GetConnectorCount(out uint connectors) != 0 || connectors == 0) return null;
            if (topology.GetConnector(0, out IConnector? connector) != 0 || connector == null) return null;
            try { return connector.GetDeviceIdConnectedTo(out string? id) == 0 ? id : null; }
            finally { Marshal.ReleaseComObject(connector); }
        }
        finally { Marshal.ReleaseComObject(topology); }
    }

    /// <summary>I filtri del driver audio Bluetooth (A2DP e vivavoce) hanno un percorso che inizia per "bth".</summary>
    private static bool IsBluetoothFilter(string filterId) =>
        filterId.Contains(@"\\?\bth", StringComparison.OrdinalIgnoreCase);

    private static Guid? ContainerId(IMMDevice device)
    {
        if (device.OpenPropertyStore(0 /* STGM_READ */, out IPropertyStore? store) != 0 || store == null) return null;
        try
        {
            var key = new PROPERTYKEY { fmtid = new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), pid = 2 }; // PKEY_Device_ContainerId
            if (store.GetValue(ref key, out PROPVARIANT value) != 0) return null;
            try { return value.vt == 72 /* VT_CLSID */ && value.pointer != IntPtr.Zero ? Marshal.PtrToStructure<Guid>(value.pointer) : null; }
            finally { PropVariantClear(ref value); }
        }
        finally { Marshal.ReleaseComObject(store); }
    }

    [ComImport, Guid("2A07407E-6497-4A18-9787-32F79BD0D98F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDeviceTopology
    {
        [PreserveSig] int GetConnectorCount(out uint count);
        [PreserveSig] int GetConnector(uint index, out IConnector? connector);
    }

    [ComImport, Guid("9C2C4058-23F5-41DE-877A-DF3AF236A09E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IConnector
    {
        [PreserveSig] int GetConnectorType(out int type);
        [PreserveSig] int GetDataFlow(out int flow);
        [PreserveSig] int ConnectTo(IntPtr connector);
        [PreserveSig] int Disconnect();
        [PreserveSig] int IsConnected(out int connected);
        [PreserveSig] int GetConnectedTo(out IntPtr connector);
        [PreserveSig] int GetConnectorIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string? id);
        [PreserveSig] int GetDeviceIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string? id);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KSPROPERTY { public Guid Set; public uint Id; public uint Flags; }

    [ComImport, Guid("28F54685-06FD-11D2-B27A-00A0C9223196"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IKsControl
    {
        [PreserveSig] int KsProperty(ref KSPROPERTY property, uint propertyLength, IntPtr data, uint dataLength, out uint bytesReturned);
    }
}
