using System.Diagnostics;
using System.Runtime.InteropServices;
using OpSecBar.Core;

namespace OpSecBar.Services;

/// <summary>
/// Intercetta il tasto Windows premuto da solo e apre il menu Start di OpSecBar al posto di quello di Windows.
/// Le combinazioni (Win+E, Win+D, Win+L, Win+frecce...) passano inalterate.
///
/// Come funziona: un hook di tastiera a basso livello osserva i tasti. Quando Win viene rilasciato senza che
/// nel frattempo sia stato premuto altro, prima del rilascio viene inserito un tasto "maschera" senza funzione
/// (vkE8): Windows vede una combinazione e non apre il proprio menu Start. È la stessa tecnica usata da AutoHotkey.
/// </summary>
public sealed class WinKeyHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105;
    private const int VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;
    private const ushort MaskKey = 0xE8;
    private static readonly IntPtr Marker = new(0x4E4F5641); // "NOVA": riconosce i tasti inseriti da noi

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    private readonly HookProc _proc; // tenuto in un campo: il delegate non deve essere raccolto dal GC
    private readonly Action _onWinKey; // deve tornare subito (l'hook non può bloccare la tastiera): usare BeginInvoke
    private IntPtr _hook;
    private bool _winDown, _combo;

    public WinKeyHook(Action onWinKey)
    {
        _onWinKey = onWinKey;
        _proc = Callback;
    }

    public bool IsInstalled => _hook != IntPtr.Zero;

    /// <summary>Va chiamato dal thread della UI (serve un ciclo di messaggi).</summary>
    public void Install()
    {
        if (_hook != IntPtr.Zero) return;
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(module?.ModuleName), 0);
        if (_hook == IntPtr.Zero) Log.Warn("Hook del tasto Windows non installato: errore " + Marshal.GetLastWin32Error());
    }

    public void Uninstall()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _winDown = false;
    }

    public void Dispose() => Uninstall();

    private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (code >= 0)
            {
                var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                if (data.dwExtraInfo != Marker) HandleKey(wParam.ToInt32(), (int)data.vkCode);
            }
        }
        catch (Exception ex) { Log.Error("WinKeyHook", ex); }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private void HandleKey(int message, int vk)
    {
        bool isWin = vk is VK_LWIN or VK_RWIN;

        if (message is WM_KEYDOWN or WM_SYSKEYDOWN)
        {
            if (!isWin)
            {
                if (_winDown) _combo = true; // Win + un altro tasto: è una scorciatoia di Windows
                return;
            }
            // Nuova pressione (non una ripetizione automatica): lo stato asincrono non è ancora aggiornato.
            if (!_winDown || (GetAsyncKeyState(vk) & 0x8000) == 0)
            {
                _winDown = true;
                _combo = IsDown(VK_CONTROL) || IsDown(VK_SHIFT) || IsDown(VK_MENU);
            }
            return;
        }

        if (message is WM_KEYUP or WM_SYSKEYUP && isWin && _winDown)
        {
            _winDown = false;
            if (_combo || IsDown(VK_CONTROL) || IsDown(VK_SHIFT) || IsDown(VK_MENU)) return;

            SendMaskKey();
            _onWinKey();
        }
    }

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private static void SendMaskKey()
    {
        var inputs = new INPUT[2];
        inputs[0].type = 1;
        inputs[0].U.ki = new KEYBDINPUT { wVk = MaskKey, dwExtraInfo = Marker };
        inputs[1].type = 1;
        inputs[1].U.ki = new KEYBDINPUT { wVk = MaskKey, dwFlags = 0x2 /* KEYEVENTF_KEYUP */, dwExtraInfo = Marker };
        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }

    // ---- P/Invoke -----------------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

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

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
}
