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
    /// All'avvio la voce Run è la fonte di verità: la crea o la rimuove anche l'installer ("Start OpSec Bar when I sign in").
    /// Se c'è ma punta a un altro percorso (eseguibile spostato) viene aggiornata.
    /// Restituisce lo stato dell'avvio automatico, o <paramref name="saved"/> se il registro non è leggibile.
    /// </summary>
    public static bool Sync(bool saved)
    {
        try
        {
            string? current;
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey)) current = key?.GetValue(ValueName) as string;
            if (string.IsNullOrEmpty(current)) return false;
            if (current != Command) Set(true);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Sincronizzazione avvio automatico", ex);
            return saved;
        }
    }
}
