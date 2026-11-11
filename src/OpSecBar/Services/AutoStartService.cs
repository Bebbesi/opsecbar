using Microsoft.Win32;
using OpSecBar.Core;

namespace OpSecBar.Services;

/// <summary>Avvio automatico con Windows tramite la chiave Run dell'utente corrente (nessun privilegio richiesto).</summary>
public static class AutoStartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "OpSecBar";

    public static string Command => $"\"{Environment.ProcessPath}\" --autostart";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string s && s.Length > 0;
        }
        catch { return false; }
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled) key.SetValue(ValueName, Command);
            else key.DeleteValue(ValueName, false);
        }
        catch (Exception ex) { Log.Error("Impostazione avvio automatico fallita", ex); }
    }

    /// <summary>
    /// All'avvio allinea il registro all'impostazione salvata: aggiorna il percorso se l'eseguibile è stato spostato
    /// e rimuove la voce se l'avvio automatico è disattivato.
    /// </summary>
    public static void Sync(bool enabled)
    {
        try
        {
            string? current;
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey)) current = key?.GetValue(ValueName) as string;
            if (enabled && current != Command) Set(true);
            else if (!enabled && current != null) Set(false);
        }
        catch (Exception ex) { Log.Error("Sincronizzazione avvio automatico", ex); }
    }
}
