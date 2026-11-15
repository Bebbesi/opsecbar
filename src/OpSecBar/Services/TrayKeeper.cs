using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using ManagedShell;
using ManagedShell.WindowsTray;
using OpSecBar.Core;

namespace OpSecBar.Services;

/// <summary>
/// Tiene le icone dell'area di notifica sulla barra.
/// Le app registrano la propria icona sulla prima finestra "Shell_TrayWnd" in ordine Z: se in quell'istante davanti
/// alla nostra c'è quella di Explorer, l'icona finisce lì e sulla barra non compare più (Windows 11 non permette di
/// rileggerla da Explorer). Succede soprattutto all'avvio di Windows, quando molte app partono insieme alla barra.
/// Questa classe riporta subito davanti la nostra finestra e, quando qualche icona può essere andata persa, chiede
/// a tutte le app di registrarsi di nuovo (messaggio "TaskbarCreated", lo stesso che Explorer invia quando riparte).
/// </summary>
public sealed class TrayKeeper : IDisposable
{
    private const string TrayWndClass = "Shell_TrayWnd";
    /// <summary>Dopo l'avvio, per questo tempo ogni perdita del primo posto fa ripetere la registrazione.</summary>
    private const long StartupWindowMs = 120_000;
    /// <summary>A regime la registrazione si ripete solo se la nostra finestra è rimasta dietro più a lungo di così.</summary>
    private const long LongGapMs = 400;
    private const long MinBroadcastGapMs = 8_000;

    private readonly NotificationArea? _area;
    private readonly IntPtr _handle;
    private readonly DispatcherTimer _monitor;
    private readonly DispatcherTimer _rebroadcast;
    private readonly DispatcherTimer? _managedShellMonitor;
    private readonly uint _taskbarCreated;
    private readonly long _startedAt = Environment.TickCount64;
    private readonly Queue<int> _startupPasses = new(new[] { 4, 16 });
    private long _lastOnTop = Environment.TickCount64;
    private long _lastBroadcast = Environment.TickCount64; // ManagedShell ne ha appena inviato uno
    private bool _disposed;
    private int _raises;

    public TrayKeeper(ShellManager shell)
    {
        _area = shell.NotificationArea;
        _handle = _area?.Handle ?? IntPtr.Zero;
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");

        // Il controllo di ManagedShell (ogni 100 ms, a bassa priorità) si limita a riportare davanti la finestra: lo
        // si sostituisce con questo, che sa anche quando è il caso di far ripetere la registrazione.
        try
        {
            _managedShellMonitor = typeof(TrayService)
                .GetField("trayMonitor", BindingFlags.Instance | BindingFlags.NonPublic)?
                .GetValue(shell.TrayService) as DispatcherTimer;
        }
        catch (Exception ex) { Log.Warn("TrayKeeper: controllo di ManagedShell non trovato: " + ex.Message); }

        _monitor = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(100) };
        _monitor.Tick += (_, _) => Check();
        _rebroadcast = new DispatcherTimer(DispatcherPriority.Normal);
        _rebroadcast.Tick += (_, _) => Broadcast();

        if (_handle == IntPtr.Zero)
        {
            Log.Warn("TrayKeeper: area di notifica non disponibile");
            return;
        }
        _managedShellMonitor?.Stop();
        _monitor.Start();
        // All'avvio la barra è occupata per qualche secondo e le altre app stanno ancora partendo: un paio di
        // ripassate recuperano le icone registrate su Explorer nel frattempo.
        Schedule(_startupPasses.Dequeue() * 1000);
    }

    /// <summary>
    /// Da chiamare dopo aver portato di proposito la taskbar di Explorer davanti alla nostra (lettura o modifica del
    /// suo stato): le icone registrate in quell'intervallo vanno recuperate.
    /// </summary>
    public void RequestRefresh()
    {
        if (_disposed || _handle == IntPtr.Zero) return;
        RaiseIfBehind();
        Schedule(1200);
    }

    private void Check()
    {
        if (_managedShellMonitor?.IsEnabled == true) _managedShellMonitor.Stop(); // ManagedShell lo riavvia a ogni Resume
        long now = Environment.TickCount64;
        if (!RaiseIfBehind())
        {
            _lastOnTop = now;
            return;
        }
        // Dietro a Explorer per un solo giro di controllo, a regime, è normale (un click sulla taskbar di Windows):
        // non vale la pena disturbare tutte le app. All'avvio, o dopo un'attesa lunga, sì.
        bool lost = now - _startedAt < StartupWindowMs || now - _lastOnTop > LongGapMs;
        _lastOnTop = now;
        if (lost) Schedule(1200);
    }

    /// <summary>Riporta la nostra finestra davanti alle altre "Shell_TrayWnd". True se non lo era.</summary>
    private bool RaiseIfBehind()
    {
        IntPtr top = FindWindow(TrayWndClass, null);
        if (top == _handle || top == IntPtr.Zero) return false;
        _raises++;
        SetWindowPos(_handle, (IntPtr)(-1) /* HWND_TOPMOST */, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        return true;
    }

    private void Schedule(int delayMs)
    {
        if (_disposed) return;
        // Una richiesta già in attesa basta; le ripetizioni troppo ravvicinate vengono distanziate.
        if (_rebroadcast.IsEnabled) return;
        long wait = Math.Max(delayMs, MinBroadcastGapMs - (Environment.TickCount64 - _lastBroadcast));
        _rebroadcast.Interval = TimeSpan.FromMilliseconds(wait);
        _rebroadcast.Start();
    }

    private void Broadcast()
    {
        _rebroadcast.Stop();
        if (_disposed || _taskbarCreated == 0) return;
        RaiseIfBehind();
        _lastBroadcast = Environment.TickCount64;
        Log.Info($"Tray: chiedo alle app di registrare di nuovo le icone ({_area?.TrayIcons.Count ?? 0} presenti; finestra riportata davanti {_raises} volte)");
        _raises = 0;
        SendNotifyMessage((IntPtr)0xFFFF /* HWND_BROADCAST */, _taskbarCreated, UIntPtr.Zero, IntPtr.Zero);
        if (_startupPasses.Count > 0) Schedule(_startupPasses.Dequeue() * 1000);
    }

    public void Dispose()
    {
        _disposed = true;
        _monitor.Stop();
        _rebroadcast.Stop();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string? windowName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] private static extern bool SendNotifyMessage(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
