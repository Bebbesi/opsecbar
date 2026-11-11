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
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--watchdog" && int.TryParse(args[1], out int pid))
            return TaskbarGuard.RunWatchdog(pid);

        if (args.Length >= 1 && args[0] == "--restore")
        {
            if (EventWaitHandle.TryOpenExisting(ExitEventName, out EventWaitHandle? exit))
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

        return RunApp();
    }

    // Metodo separato: così le modalità leggere non caricano WPF.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunApp()
    {
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
