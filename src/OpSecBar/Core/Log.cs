using System.IO;

namespace OpSecBar.Core;

public static class AppInfo
{
    /// <summary>Nome mostrato all'utente.</summary>
    public const string Name = "OpSec Bar";
}

/// <summary>Percorsi dei dati dell'applicazione (%AppData%\OpSecBar).</summary>
public static class AppPaths
{
    public static string DataDir { get; } = Init();
    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string ThemesDir => Path.Combine(DataDir, "themes");

    /// <summary>GIF caricate dall'utente per la barra.</summary>
    public static string GifsDir
    {
        get
        {
            string dir = Path.Combine(DataDir, "gifs");
            try { Directory.CreateDirectory(dir); } catch { }
            return dir;
        }
    }
    public static string LogFile => Path.Combine(DataDir, "opsecbar.log");
    public static string TaskbarStateFile => Path.Combine(DataDir, "taskbar.state");

    private static string Init()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string dir = Path.Combine(appData, "OpSecBar");
        try
        {
            bool firstRun = !Directory.Exists(dir);
            Directory.CreateDirectory(dir);
            Directory.CreateDirectory(Path.Combine(dir, "themes"));
            if (firstRun) MigrateFrom(Path.Combine(appData, "NovaBar"), dir);
        }
        catch { }
        return dir;
    }

    /// <summary>L'app si chiamava NovaBar: al primo avvio si recuperano impostazioni e temi salvati con il vecchio nome.</summary>
    private static void MigrateFrom(string oldDir, string newDir)
    {
        try
        {
            if (!Directory.Exists(oldDir)) return;
            string oldSettings = Path.Combine(oldDir, "settings.json");
            if (File.Exists(oldSettings)) File.Copy(oldSettings, Path.Combine(newDir, "settings.json"), false);
            string oldThemes = Path.Combine(oldDir, "themes");
            if (Directory.Exists(oldThemes))
                foreach (string file in Directory.EnumerateFiles(oldThemes, "*.json"))
                    File.Copy(file, Path.Combine(newDir, "themes", Path.GetFileName(file)), false);
        }
        catch { /* la migrazione è un di più: in caso di problemi si parte dalle impostazioni predefinite */ }
    }
}

/// <summary>Log su file, semplice e a prova di errore.</summary>
public static class Log
{
    private static readonly object Gate = new();

    public static void Info(string msg) => Write("INFO ", msg);
    public static void Warn(string msg) => Write("WARN ", msg);
    public static void Error(string msg, Exception? ex = null) => Write("ERROR", ex == null ? msg : $"{msg}: {ex}");

    private static void Write(string level, string msg)
    {
        try
        {
            lock (Gate)
            {
                var fi = new FileInfo(AppPaths.LogFile);
                if (fi.Exists && fi.Length > 1_000_000) fi.Delete();
                File.AppendAllText(AppPaths.LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {msg}{Environment.NewLine}");
            }
        }
        catch { /* il log non deve mai far cadere l'app */ }
    }
}
