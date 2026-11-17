using System.Runtime.CompilerServices;
using OpSecBar.Services;

namespace OpSecBar;

public static class Program
{
    public const string ExitEventName = "Local\\OpSecBar.Exit";
    public const string ShowSettingsEventName = "Local\\OpSecBar.ShowSettings";

    /// <summary>
    /// Modalità:
    ///   OpSecBar.exe                 avvia la taskbar
    ///   OpSecBar.exe --restore       chiude OpSecBar (se attivo) e ripristina la taskbar di Windows
    ///   OpSecBar.exe --watchdog PID  (interno) ripristina la taskbar se il processo PID termina in modo anomalo
    ///   OpSecBar.exe --mock-crash    mostra il popup di segnalazione crash con un rapporto finto (prova)
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--watchdog" && int.TryParse(args[1], out int pid))
            return TaskbarGuard.RunWatchdog(pid);

        if (args.Length >= 1 && args[0] == "--restore")
        {
            if (TryOpenEvent(ExitEventName, out EventWaitHandle? exit))
            {
                exit.Set();
                exit.Dispose();
                for (int i = 0; i < 30; i++)
                {
                    Thread.Sleep(100);
                    if (!Mutex.TryOpenExisting(TaskbarGuard.MainMutexName, out Mutex? m)) break;
                    m.Dispose();
                }
            }
            TaskbarGuard.ForceRestore();
            return 0;
        }

        if (args.Length >= 1 && args[0] == "--mock-crash")
        {
            MockCrash();
            return 0;
        }

        return RunApp();
    }

    /// <summary>OpSecBar.exe --mock-crash: mostra il popup di segnalazione crash con un rapporto finto, senza avviare la barra.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void MockCrash() => OpSecBar.Windows.CrashPrompt.Mock();

    // Metodo separato: così le modalità leggere non caricano WPF.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunApp()
    {
        var app = new App();
        app.InitializeComponent();
        try
        {
            return app.Run();
        }
        catch (Exception ex) when (app.IsExiting)
        {
            // L'uscita è già stata completata (impostazioni salvate, taskbar di Windows ripristinata): un errore di WPF
            // mentre smonta il rendering (es. UCEERR_RENDERTHREADFAILURE) non deve diventare un crash con tanto di
            // segnalazione di Windows.
            OpSecBar.Core.Log.Warn("Errore ignorato durante la chiusura: " + ex.Message);
            return 0;
        }
    }

    /// <summary>
    /// Apre un evento con nome di un'altra istanza. Se quell'istanza gira con privilegi diversi Windows nega l'accesso
    /// (con un'eccezione, non con "non trovato"): lo si tratta come se l'evento non ci fosse.
    /// </summary>
    public static bool TryOpenEvent(string name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out EventWaitHandle? handle)
    {
        try { return EventWaitHandle.TryOpenExisting(name, out handle); }
        catch (Exception)
        {
            handle = null;
            return false;
        }
    }
}
