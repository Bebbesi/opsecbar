using System.Runtime.InteropServices;
using System.Windows.Media;
using OpSecBar.Core;
using OpSecBar.Models;

namespace OpSecBar.Services;

/// <summary>Effetti di sfondo della finestra (blur, acrylic) e ritaglio ad angoli arrotondati.</summary>
public static class BackdropHelper
{
    private static readonly bool ClassicBlurBroken = Environment.OSVersion.Version.Build >= 22621;

    /// <summary>
    /// Da Windows 11 24H2 l'acrylic ignora la regione della finestra: il ritaglio arrotondato schiarisce solo l'interno
    /// e negli angoli resta un quadrato sfocato. Il blur "classico" invece sfoca di nuovo e rispetta la regione,
    /// quindi con gli angoli arrotondati si usa quello (senza tinta: il colore lo disegna la barra).
    /// </summary>
    private static readonly bool ClassicBlurForRounded = Environment.OSVersion.Version.Build >= 26100;

    /// <summary>True se, con questi angoli, Windows non applica la tinta e il colore va disegnato dalla finestra sopra la lastra.</summary>
    public static bool TintDrawnByWindow(BackdropMode mode, bool rounded) =>
        mode != BackdropMode.Acrylic || (rounded && ClassicBlurForRounded);

    public static bool UsesCompositionBlur(BackdropMode mode) =>
        mode is BackdropMode.Blur or BackdropMode.Acrylic or BackdropMode.Glass;

    /// <summary>
    /// Blur e acrylic dipendono dagli "Effetti di trasparenza" di Windows (Impostazioni → Personalizzazione → Colori).
    /// Se sono disattivati Windows sostituisce l'effetto con un colore pieno: in quel caso OpSecBar disegna da sé
    /// la barra semitrasparente, senza sfocatura.
    /// </summary>
    public static bool SystemTransparencyEnabled
    {
        get
        {
            if (Environment.GetEnvironmentVariable("OPSECBAR_FORCE_BLUR") == "1") return true; // solo per test
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("EnableTransparency") is not int value || value != 0;
            }
            catch { return true; }
        }
    }

    /// <summary>True se per questo tema va usata la sfocatura di sistema.</summary>
    public static bool BlurActive(BackdropMode mode) => UsesCompositionBlur(mode) && SystemTransparencyEnabled;

    /// <summary>Applica (o rimuove) l'effetto di composizione dietro la finestra.</summary>
    /// <param name="rounded">True se la finestra verrà ritagliata con una regione ad angoli arrotondati.</param>
    public static void Apply(IntPtr hwnd, BackdropMode mode, Color tint, double opacity, bool enabled = true, bool rounded = false)
    {
        if (hwnd == IntPtr.Zero) return;

        var policy = new Native.AccentPolicy { AccentState = Native.AccentState.Disabled };
        if (enabled && rounded && ClassicBlurForRounded)
            policy.AccentState = Native.AccentState.BlurBehind;
        else if (enabled)
        {
            switch (mode)
            {
                case BackdropMode.Blur:
                case BackdropMode.Glass:
                    if (ClassicBlurBroken)
                    {
                        // Da Windows 11 22H2 il blur "classico" non sfoca più: si usa l'acrylic con tinta quasi nulla
                        // (il colore vero lo disegna la barra sopra lo sfondo sfocato).
                        policy.AccentState = Native.AccentState.AcrylicBlurBehind;
                        policy.AccentFlags = 2;
                        policy.GradientColor = (uint)(0x01 << 24 | tint.B << 16 | tint.G << 8 | tint.R);
                    }
                    else policy.AccentState = Native.AccentState.BlurBehind;
                    break;
                case BackdropMode.Acrylic:
                    policy.AccentState = Native.AccentState.AcrylicBlurBehind;
                    policy.AccentFlags = 2;
                    // Formato ABGR. Con alfa 0 l'acrylic non viene disegnato: minimo 1.
                    byte a = (byte)Math.Clamp(Math.Round(opacity * 255), 1, 255);
                    policy.GradientColor = (uint)(a << 24 | tint.B << 16 | tint.G << 8 | tint.R);
                    break;
            }
        }

        int size = Marshal.SizeOf<Native.AccentPolicy>();
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, ptr, false);
            var data = new Native.WindowCompositionAttributeData { Attribute = 19 /* WCA_ACCENT_POLICY */, Data = ptr, SizeOfData = size };
            Native.SetWindowCompositionAttribute(hwnd, ref data);
        }
        catch (Exception ex) { Log.Warn("Backdrop non applicabile: " + ex.Message); }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    /// <summary>Ritaglia la finestra a un rettangolo arrotondato (in pixel fisici). Serve per arrotondare anche il blur.</summary>
    public static void SetRoundedRegion(IntPtr hwnd, int x, int y, int width, int height, int radius)
    {
        if (hwnd == IntPtr.Zero || width <= 0 || height <= 0) return;
        IntPtr rgn = Native.CreateRoundRectRgn(x, y, x + width + 1, y + height + 1, radius * 2, radius * 2);
        // Dopo SetWindowRgn la regione appartiene al sistema: non va eliminata.
        if (Native.SetWindowRgn(hwnd, rgn, true) == 0) Native.DeleteObject(rgn);
    }

    public static void ClearRegion(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero) Native.SetWindowRgn(hwnd, IntPtr.Zero, true);
    }

    /// <summary>Barra del titolo scura per le finestre normali (Impostazioni).</summary>
    public static void UseDarkTitleBar(IntPtr hwnd)
    {
        try
        {
            int on = 1;
            if (Native.DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0)
                Native.DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
        }
        catch { }
    }
}
