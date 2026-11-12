using System.Diagnostics;
using System.Runtime.InteropServices;
using OpSecBar.Core;

namespace OpSecBar.Services;

/// <summary>
/// Volume di una singola app (il "mixer" di Windows), usato dal player: regola solo l'app che sta riproducendo.
/// L'app della sessione multimediale (AUMID come "Spotify.exe", "MSEdge" o "SpotifyAB.SpotifyMusic_xxx!Spotify")
/// viene associata alle sessioni audio del dispositivo predefinito tramite processo o percorso dell'eseguibile.
/// </summary>
public static class AppVolumeService
{
    /// <summary>Volume 0–100 e mute dell'app; null se non ha una sessione audio riconoscibile.</summary>
    public static (int Volume, bool Muted)? Get(string appId)
    {
        (int, bool)? result = null;
        ForEachSession(appId, v =>
        {
            if (result != null) return;
            if (v.GetMasterVolume(out float level) == 0 && v.GetMute(out bool muted) == 0)
                result = ((int)Math.Round(level * 100), muted);
        });
        return result;
    }

    public static bool SetVolume(string appId, int percent)
    {
        float level = Math.Clamp(percent, 0, 100) / 100f;
        return ForEachSession(appId, v =>
        {
            Guid ctx = Guid.Empty;
            v.SetMasterVolume(level, ref ctx);
            if (percent > 0) v.SetMute(false, ref ctx);
        });
    }

    public static bool SetMute(string appId, bool mute) => ForEachSession(appId, v =>
    {
        Guid ctx = Guid.Empty;
        v.SetMute(mute, ref ctx);
    });

    /// <summary>Applica l'azione a tutte le sessioni audio dell'app (i browser ne hanno spesso più d'una).</summary>
    private static bool ForEachSession(string appId, Action<ISimpleAudioVolume> action)
    {
        if (string.IsNullOrWhiteSpace(appId)) return false;
        bool found = false;
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioSessionManager2? manager = null;
        IAudioSessionEnumerator? sessions = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            if (enumerator.GetDefaultAudioEndpoint(0, 1, out device) != 0 || device == null) return false;
            Guid iid = typeof(IAudioSessionManager2).GUID;
            if (device.Activate(ref iid, 0x17, IntPtr.Zero, out object? o) != 0) return false;
            manager = o as IAudioSessionManager2;
            if (manager == null || manager.GetSessionEnumerator(out sessions) != 0 || sessions == null) return false;
            if (sessions.GetCount(out int count) != 0) return false;

            for (int i = 0; i < count; i++)
            {
                if (sessions.GetSession(i, out IAudioSessionControl2? control) != 0 || control == null) continue;
                try
                {
                    if (!Matches(control, appId)) continue;
                    if (control is ISimpleAudioVolume volume)
                    {
                        action(volume);
                        found = true;
                    }
                }
                catch (Exception ex) { Log.Warn("Sessione audio: " + ex.Message); }
                finally { Marshal.ReleaseComObject(control); }
            }
        }
        catch (Exception ex) { Log.Warn("Volume app: " + ex.Message); }
        finally
        {
            foreach (object? com in new object?[] { sessions, manager, device, enumerator })
                if (com != null) try { Marshal.ReleaseComObject(com); } catch { }
        }
        return found;
    }

    private static bool Matches(IAudioSessionControl2 control, string appId)
    {
        string id = appId.Trim();
        string identifier = "";
        try { if (control.GetSessionIdentifier(out string? s) == 0 && s != null) identifier = s; } catch { }

        // App pacchettizzata: "Nome.Pacchetto_idEditore!App" → il percorso contiene "WindowsApps\Nome.Pacchetto_..._idEditore".
        int bang = id.IndexOf('!');
        if (bang > 0)
        {
            string family = id[..bang];
            int us = family.LastIndexOf('_');
            if (us > 0)
            {
                string name = family[..us], publisher = family[(us + 1)..];
                if (identifier.Contains(name, StringComparison.OrdinalIgnoreCase) &&
                    identifier.Contains(publisher, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }

        string processName = "";
        if (control.GetProcessId(out uint pid) == 0 && pid != 0)
        {
            try { using var p = Process.GetProcessById((int)pid); processName = p.ProcessName; } catch { }
        }
        if (processName.Length == 0) return false;

        // Win32: "Spotify.exe" / "C:\...\vlc.exe"; browser: "MSEdge", "Chrome".
        string exe = System.IO.Path.GetFileNameWithoutExtension(id.Contains('\\') ? id : id.Replace('/', '\\'));
        if (string.Equals(exe, processName, StringComparison.OrdinalIgnoreCase)) return true;
        if (bang < 0 && id.Length >= 4 && processName.Length >= 4 &&
            (processName.Contains(id, StringComparison.OrdinalIgnoreCase) || id.Contains(processName, StringComparison.OrdinalIgnoreCase)))
            return true;
        // Ultima possibilità: il nome dell'app compare nel percorso dell'eseguibile della sessione.
        string last = (bang > 0 ? id[(bang + 1)..] : exe);
        return last.Length >= 4 && identifier.Contains("\\" + last + ".exe", StringComparison.OrdinalIgnoreCase);
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

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr groupingParam, uint flags, out IntPtr sessionControl);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr groupingParam, uint flags, out IntPtr audioVolume);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator? sessionEnum);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, out IAudioSessionControl2? session);
    }

    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        // IAudioSessionControl
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string? name);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid ctx);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string? path);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid ctx);
        [PreserveSig] int GetGroupingParam(out Guid param);
        [PreserveSig] int SetGroupingParam(ref Guid param, ref Guid ctx);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr client);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr client);
        // IAudioSessionControl2
        [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string? id);
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string? id);
        [PreserveSig] int GetProcessId(out uint pid);
        [PreserveSig] int IsSystemSoundsSession();
        [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float level, ref Guid ctx);
        [PreserveSig] int GetMasterVolume(out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
