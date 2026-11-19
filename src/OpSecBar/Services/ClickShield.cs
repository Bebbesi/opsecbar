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

    public ClickShield() => _proc = Callback;

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
                uint down = message switch
                {
                    WM_LBUTTONDOWN => MOUSEEVENTF_LEFTDOWN,
                    WM_RBUTTONDOWN => MOUSEEVENTF_RIGHTDOWN,
                    WM_MBUTTONDOWN => MOUSEEVENTF_MIDDLEDOWN,
                    _ => 0
                };
                if (down != 0)
                {
                    var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    if ((info.flags & LLMHF_INJECTED) == 0 && info.dwExtraInfo != Marker && ShouldShield(info.pt, out IntPtr ours))
                    {
                        Native.ForceForeground(ours, allowKeyTap: false);
                        var input = new INPUT { type = 0 };
                        input.U.mi = new MOUSEINPUT { dwFlags = down, dwExtraInfo = Marker };
                        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
                        return new IntPtr(1); // la pressione originale non arriva a nessuno
                    }
                }
            }
        }
        catch (Exception ex) { Log.Error("ClickShield", ex); }
        return CallNextHookEx(_hook, code, wParam, lParam);
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
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
}
