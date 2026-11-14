using System.Runtime.InteropServices;
using OpSecBar.Core;
using Windows.Devices.WiFi;
using Windows.Networking.Connectivity;
using Windows.Security.Credentials;

namespace OpSecBar.Services;

/// <summary>Una rete Wi-Fi visibile (il punto d'accesso col segnale migliore, se più d'uno ha lo stesso nome).</summary>
public sealed class WifiNetwork
{
    public required string Ssid { get; init; }
    /// <summary>Tacche di segnale, da 1 a 4.</summary>
    public int Bars { get; init; }
    public bool Secured { get; init; }
    /// <summary>Rete aziendale (802.1X): servono credenziali che solo il pannello di Windows sa chiedere.</summary>
    public bool Enterprise { get; init; }
    public bool Connected { get; init; }
    /// <summary>Windows ne ha già il profilo (password salvata).</summary>
    public bool Saved { get; init; }
    internal WiFiAvailableNetwork? Native { get; init; }
}

public enum WifiListState { Ok, NoAdapter, AccessDenied }

public enum WifiConnectOutcome { Connected, WrongPassword, NeedsPassword, TimedOut, Failed }

/// <summary>Elenco delle reti Wi-Fi e connessione, senza passare dal pannello di Windows (API Windows.Devices.WiFi).</summary>
public static class WifiService
{
    private static WiFiAdapter? _adapter;

    private static async Task<(WiFiAdapter? Adapter, WifiListState State)> AdapterAsync()
    {
        if (_adapter != null) return (_adapter, WifiListState.Ok);
        // Su Windows 11 recente l'elenco delle reti richiede che l'accesso alla posizione sia consentito.
        if (await WiFiAdapter.RequestAccessAsync() != WiFiAccessStatus.Allowed) return (null, WifiListState.AccessDenied);
        _adapter = (await WiFiAdapter.FindAllAdaptersAsync()).FirstOrDefault();
        return (_adapter, _adapter != null ? WifiListState.Ok : WifiListState.NoAdapter);
    }

    /// <param name="scan">True per chiedere alla scheda una nuova ricerca (qualche secondo); altrimenti l'ultimo elenco noto.</param>
    public static async Task<(WifiListState State, List<WifiNetwork> Networks)> ListAsync(bool scan)
    {
        var networks = new List<WifiNetwork>();
        try
        {
            (WiFiAdapter? adapter, WifiListState state) = await AdapterAsync();
            if (adapter == null) return (state, networks);

            if (scan)
            {
                try { await adapter.ScanAsync(); }
                catch (Exception ex) { Log.Warn("Ricerca reti Wi-Fi: " + ex.Message); }
            }

            string? connected = await ConnectedSsidAsync(adapter);
            HashSet<string> saved = SavedProfiles(adapter.NetworkAdapter.NetworkAdapterId);

            foreach (var group in adapter.NetworkReport.AvailableNetworks.Where(n => !string.IsNullOrWhiteSpace(n.Ssid)).GroupBy(n => n.Ssid))
            {
                WiFiAvailableNetwork best = group.OrderByDescending(n => n.NetworkRssiInDecibelMilliwatts).First();
                NetworkAuthenticationType auth = best.SecuritySettings.NetworkAuthenticationType;
                networks.Add(new WifiNetwork
                {
                    Ssid = best.Ssid,
                    Bars = Math.Clamp((int)best.SignalBars, 1, 4),
                    Secured = auth is not (NetworkAuthenticationType.Open80211 or NetworkAuthenticationType.None),
                    Enterprise = auth is NetworkAuthenticationType.Rsna or NetworkAuthenticationType.Wpa,
                    Connected = best.Ssid == connected,
                    Saved = saved.Contains(best.Ssid),
                    Native = best
                });
            }

            return (WifiListState.Ok, networks
                .OrderByDescending(n => n.Connected).ThenByDescending(n => n.Saved)
                .ThenByDescending(n => n.Native!.NetworkRssiInDecibelMilliwatts).ToList());
        }
        catch (UnauthorizedAccessException)
        {
            return (WifiListState.AccessDenied, networks);
        }
        catch (Exception ex)
        {
            Log.Warn("Elenco reti Wi-Fi: " + ex.Message);
            return (WifiListState.Ok, networks);
        }
    }

    private static async Task<string?> ConnectedSsidAsync(WiFiAdapter adapter)
    {
        try
        {
            ConnectionProfile? profile = await adapter.NetworkAdapter.GetConnectedProfileAsync();
            if (profile == null) return null;
            string? ssid = null;
            try { ssid = profile.WlanConnectionProfileDetails?.GetConnectedSsid(); } catch { }
            return string.IsNullOrEmpty(ssid) ? profile.ProfileName : ssid;
        }
        catch { return null; }
    }

    /// <param name="password">Null per usare il profilo salvato (o una rete aperta).</param>
    public static async Task<WifiConnectOutcome> ConnectAsync(WifiNetwork network, string? password)
    {
        try
        {
            (WiFiAdapter? adapter, _) = await AdapterAsync();
            if (adapter == null || network.Native == null) return WifiConnectOutcome.Failed;

            WiFiConnectionResult result = string.IsNullOrEmpty(password)
                ? await adapter.ConnectAsync(network.Native, WiFiReconnectionKind.Automatic)
                : await adapter.ConnectAsync(network.Native, WiFiReconnectionKind.Automatic, new PasswordCredential { Password = password });

            return result.ConnectionStatus switch
            {
                WiFiConnectionStatus.Success => WifiConnectOutcome.Connected,
                WiFiConnectionStatus.InvalidCredential => string.IsNullOrEmpty(password) ? WifiConnectOutcome.NeedsPassword : WifiConnectOutcome.WrongPassword,
                WiFiConnectionStatus.Timeout => WifiConnectOutcome.TimedOut,
                // Senza password su una rete protetta, qualunque rifiuto vuol dire che il profilo salvato non basta.
                _ => network.Secured && string.IsNullOrEmpty(password) ? WifiConnectOutcome.NeedsPassword : WifiConnectOutcome.Failed
            };
        }
        catch (Exception ex)
        {
            Log.Warn($"Connessione a {network.Ssid}: {ex.Message}");
            return WifiConnectOutcome.Failed;
        }
    }

    public static async Task DisconnectAsync()
    {
        try
        {
            (WiFiAdapter? adapter, _) = await AdapterAsync();
            adapter?.Disconnect();
        }
        catch (Exception ex) { Log.Warn("Disconnessione Wi-Fi: " + ex.Message); }
    }

    // ---- Profili salvati (wlanapi) -----------------------------------------------------------------
    // WinRT non dice quali reti hanno già un profilo: lo si chiede al servizio WLAN, in sola lettura.

    private static HashSet<string> SavedProfiles(Guid interfaceId)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        IntPtr client = IntPtr.Zero, list = IntPtr.Zero;
        try
        {
            if (WlanOpenHandle(2, IntPtr.Zero, out _, out client) != 0) return names;
            if (WlanGetProfileList(client, ref interfaceId, IntPtr.Zero, out list) != 0 || list == IntPtr.Zero) return names;

            // WLAN_PROFILE_INFO_LIST: numero di voci, indice, poi le voci (nome di 256 caratteri + flag).
            int count = Marshal.ReadInt32(list);
            const int entrySize = 256 * 2 + 4;
            for (int i = 0; i < count; i++)
            {
                string? name = Marshal.PtrToStringUni(list + 8 + i * entrySize);
                if (!string.IsNullOrEmpty(name)) names.Add(name);
            }
        }
        catch (Exception ex) { Log.Warn("Profili Wi-Fi salvati: " + ex.Message); }
        finally
        {
            if (list != IntPtr.Zero) WlanFreeMemory(list);
            if (client != IntPtr.Zero) WlanCloseHandle(client, IntPtr.Zero);
        }
        return names;
    }

    [DllImport("wlanapi.dll")] private static extern int WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiatedVersion, out IntPtr clientHandle);
    [DllImport("wlanapi.dll")] private static extern int WlanCloseHandle(IntPtr clientHandle, IntPtr reserved);
    [DllImport("wlanapi.dll")] private static extern int WlanGetProfileList(IntPtr clientHandle, ref Guid interfaceGuid, IntPtr reserved, out IntPtr profileList);
    [DllImport("wlanapi.dll")] private static extern void WlanFreeMemory(IntPtr memory);
}
