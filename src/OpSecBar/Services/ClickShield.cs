using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using OpSecBar.Core;

namespace OpSecBar.Services;

/// <summary>
/// Impedisce che un click sulla barra arrivi anche al gioco in primo piano.
///
/// I giochi (e i visori VR tramite lo streamer) leggono il mouse in raw input finché restano la finestra attiva,
/// qualunque cosa ci sia sotto il cursore: il click sulla barra veniva quindi ricevuto pure dal gioco, come se la
/// barra non esistesse. Un hook del mouse a basso livello trattiene la pressione del tasto quando il cursore è su
/// una nostra finestra e il primo piano è di un'altra app: la barra prende il primo piano e solo dopo la pressione
/// viene reinviata. Un evento bloccato nell'hook non genera raw input, quindi il gioco non lo vede mai.
///
/// L'hook gira su un thread dedicato: se la UI è occupata il mouse del sistema non rallenta.
/// </summary>
public sealed class ClickShield : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x201, WM_RBUTTONDOWN = 0x204, WM_MBUTTONDOWN = 0x207;
    private const uint LLMHF_INJECTED = 0x1;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x2, MOUSEEVENTF_RIGHTDOWN = 0x8, MOUSEEVENTF_MIDDLEDOWN = 0x20;
    private const uint GA_ROOT = 2;
    private static readonly IntPtr Marker = new(0x4E4F5642);

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    private readonly HookProc _proc;
    private readonly uint _pid = (uint)Environment.ProcessId;
    private IntPtr _hook;
    private Thread? _thread;
    private uint _threadId;
    private readonly Action<Action> _onUi;
    private readonly object _gate = new();
    private uint _pendingDown, _pendingUp; // pressione trattenuta e rilascio arrivato prima dell'attivazione
    private Native.POINT _pendingPt;
    // Ultimo punto in cui l'utente ha portato il mouse. I giochi a cursore bloccato (VRChat, Unity) lo riportano al
    // centro dello schermo a ogni frame con SetCursorPos, che non passa dall'hook: al momento del click la posizione
    // del cursore è quella del gioco, non quella dove l'utente ha portato il mouse.
    private Native.POINT _lastMovePt;
    private long _lastMoveTick;

    // Diagnostica: con questo file accanto all'exe anche i click simulati (es. controllo remoto) vengono protetti
    // e ogni passaggio finisce in clickshield.log nella stessa cartella.
    private static readonly string DebugFlag = Path.Combine(AppContext.BaseDirectory, "clickshield.debug");
    private static readonly bool Debug = File.Exists(DebugFlag);
    private static void Trace(string text)
    {
        if (!Debug) return;
        try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "clickshield.log"), $"{DateTime.Now:HH:mm:ss.fff} {text}" + Environment.NewLine); } catch { }
    }
    private static string Describe(IntPtr hwnd)
    {
        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        string name = "?";
        try { name = Process.GetProcessById((int)pid).ProcessName; } catch { }
        return $"{hwnd:X}({name})";
    }

    /// <param name="onUi">Esegue un'azione sul thread della UI senza attendere (Dispatcher.BeginInvoke).</param>
    public ClickShield(Action<Action> onUi)
    {
        _onUi = onUi;
        _proc = Callback;
    }

    public void Install()
    {
        if (_thread != null) return;
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            using (var process = Process.GetCurrentProcess())
            using (var module = process.MainModule)
                _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(module?.ModuleName), 0);
            if (_hook == IntPtr.Zero) Log.Warn("Hook del mouse non installato: errore " + Marshal.GetLastWin32Error());
            Trace($"hook installato={_hook != IntPtr.Zero} err={Marshal.GetLastWin32Error()}");
            ready.Set();
            while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0) { }
            if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }) { IsBackground = true, Name = "ClickShield", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
        ready.Wait(2000);
    }

    public void Dispose()
    {
        if (_thread == null) return;
        PostThreadMessage(_threadId, 0x0012 /* WM_QUIT */, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(1000);
        _thread = null;
    }

    private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (code >= 0)
            {
                int message = wParam.ToInt32();
                if (Debug && message != 0x200) Trace($"msg {message:X}");
                var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                bool real = ((info.flags & LLMHF_INJECTED) == 0 || Debug) && info.dwExtraInfo != Marker;
                if (real && message == 0x200) { _lastMovePt = info.pt; _lastMoveTick = Environment.TickCount64; }
                else if (real && Handle(message, info.pt))
                    return new IntPtr(1); // l'evento originale non arriva a nessuno, nemmeno in raw input
            }
        }
        catch (Exception ex) { Log.Error("ClickShield", ex); }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private bool Handle(int message, Native.POINT pt)
    {
        uint down = message switch
        {
            WM_LBUTTONDOWN => MOUSEEVENTF_LEFTDOWN,
            WM_RBUTTONDOWN => MOUSEEVENTF_RIGHTDOWN,
            WM_MBUTTONDOWN => MOUSEEVENTF_MIDDLEDOWN,
            _ => 0
        };
        lock (_gate)
        {
            if (_pendingDown != 0)
            {
                // In attesa dell'attivazione: il rilascio dello stesso tasto viene trattenuto e reinviato dopo.
                if (message == UpOf(_pendingDown)) { _pendingUp = _pendingDown << 1; return true; }
                return false;
            }
            if (down == 0) return false;
            bool shield = ShouldShield(pt, out IntPtr ours);
            if (!shield && Environment.TickCount64 - _lastMoveTick < 1000 && ShouldShield(_lastMovePt, out ours))
            {
                pt = _lastMovePt; // il gioco ha spostato il cursore: vale il punto dove l'utente l'aveva portato
                shield = true;
            }
            Trace($"down {down:X} at {pt.X},{pt.Y} under={Describe(WindowFromPoint(pt))} fg={Describe(Native.GetForegroundWindow())} shield={shield}");
            if (!shield) return false;
            _pendingDown = down;
            _pendingUp = 0;
            // Nell'hook si fa il minimo: il cambio di primo piano può attendere il thread del gioco.
            _pendingPt = pt;
            _onUi(() => ActivateAndReplay(ours));
            return true;
        }
    }

    private static int UpOf(uint down) => down switch
    {
        MOUSEEVENTF_LEFTDOWN => 0x202,
        MOUSEEVENTF_RIGHTDOWN => 0x205,
        _ => 0x208
    };

    private void ActivateAndReplay(IntPtr ours)
    {
        try
        {
            Native.ForceForeground(ours, allowKeyTap: true);
            Trace($"activate {Describe(ours)} -> fg={Describe(Native.GetForegroundWindow())}");
            if (Native.GetForegroundWindow() != ours) Log.Warn("ClickShield: la barra non ha preso il primo piano");
        }
        catch (Exception ex) { Log.Error("ClickShield.Activate", ex); }
        finally
        {
            uint down, up;
            Native.POINT pt;
            lock (_gate)
            {
                pt = _pendingPt;
                down = _pendingDown;
                up = _pendingUp;
                _pendingDown = 0;
                _pendingUp = 0;
            }
            var inputs = new List<INPUT>();
            // Il click torna nel punto della barra in cui era stato fatto, anche se il gioco aveva spostato il cursore.
            int vx = GetSystemMetrics(76), vy = GetSystemMetrics(77), vw = GetSystemMetrics(78), vh = GetSystemMetrics(79);
            if (down != 0 && vw > 1 && vh > 1)
                inputs.Add(new INPUT { type = 0, U = new InputUnion { mi = new MOUSEINPUT
                {
                    dx = (int)((pt.X - vx) * 65535L / (vw - 1)), dy = (int)((pt.Y - vy) * 65535L / (vh - 1)),
                    dwFlags = 0x1 | 0x8000 | 0x4000 /* MOVE | ABSOLUTE | VIRTUALDESK */, dwExtraInfo = Marker
                } } });
            if (down != 0) inputs.Add(new INPUT { type = 0, U = new InputUnion { mi = new MOUSEINPUT { dwFlags = down, dwExtraInfo = Marker } } });
            if (up != 0) inputs.Add(new INPUT { type = 0, U = new InputUnion { mi = new MOUSEINPUT { dwFlags = up, dwExtraInfo = Marker } } });
            Trace($"replay down={down:X} up={up:X} at {pt.X},{pt.Y}");
            if (inputs.Count > 0) SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
        }
    }

    /// <summary>Il cursore è su una nostra finestra mentre il primo piano è di un'altra app?</summary>
    private bool ShouldShield(Native.POINT pt, out IntPtr ours)
    {
        ours = IntPtr.Zero;
        IntPtr target = WindowFromPoint(pt);
        if (target == IntPtr.Zero) return false;
        IntPtr root = GetAncestor(target, GA_ROOT);
        if (root == IntPtr.Zero) root = target;
        Native.GetWindowThreadProcessId(root, out uint targetPid);
        if (targetPid != _pid) return false;

        IntPtr fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        Native.GetWindowThreadProcessId(fg, out uint fgPid);
        if (fgPid == _pid) return false;
        ours = root;
        return true;
    }

    // ---- P/Invoke -----------------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT { public Native.POINT pt; public uint mouseData, flags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion U; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public Native.POINT pt; }

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Native.POINT pt);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
}
