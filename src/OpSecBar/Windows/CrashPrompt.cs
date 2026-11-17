using System.Windows;
using OpSecBar.Core;
using OpSecBar.Services;

namespace OpSecBar.Windows;

/// <summary>
/// Chiede se segnalare il crash dell'ultima sessione. Con "Sì" si sceglie dove salvare latest.log e si apre la
/// pagina per aprire una issue su GitHub, con il file già selezionato in Esplora file da trascinare nella pagina.
/// Lo si chiede una volta sola per crash, qualunque sia la risposta. Non richiede che la barra sia avviata
/// (lo usa anche OpSecBar.exe --mock-crash).
/// </summary>
public static class CrashPrompt
{
    public static void Ask()
    {
        if (!CrashReport.Pending) return;
        CrashReport.ClearPending();
        // Senza proprietario il popup può finire dietro le altre finestre (l'app parte all'avvio di Windows, senza il
        // diritto di andare in primo piano) e nessuno lo vede: lo si appoggia a una finestra invisibile sempre in cima.
        var owner = new Window
        {
            Width = 0, Height = 0, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false, Topmost = true, AllowsTransparency = true, Background = null,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        try
        {
            owner.Show();
            owner.Activate();
            MessageBoxResult answer = MessageBox.Show(owner,
                $"{AppInfo.Name} closed unexpectedly the last time it ran.\n\n" +
                "Do you want to report this crash? You'll choose where to save the crash log (latest.log), " +
                "then the GitHub issue page will open so you can attach it.\n\n" +
                "The log can contain the names of your apps and windows: you can check it before attaching it.",
                $"{AppInfo.Name} crashed", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.Yes);
            if (answer != MessageBoxResult.Yes) return;

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save crash log",
                FileName = "latest.log",
                DefaultExt = ".log",
                Filter = "Log file (*.log)|*.log|All files (*.*)|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };
            string? saved = null;
            if (dialog.ShowDialog(owner) == true)
            {
                try
                {
                    System.IO.File.Copy(CrashReport.LogPath, dialog.FileName, overwrite: true);
                    saved = dialog.FileName;
                }
                catch (Exception ex)
                {
                    Log.Error("Salvataggio di latest.log", ex);
                    MessageBox.Show(owner, "The crash log could not be saved.\n" + ex.Message, AppInfo.Name,
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            OpenExternal(CrashReport.IssueUrl());
            if (saved != null) OpenExternal("explorer.exe", $"/select,\"{saved}\"");
        }
        catch (Exception ex) { Log.Error("Segnalazione crash", ex); }
        finally
        {
            try { owner.Close(); } catch { }
        }
    }

    /// <summary>Prova del popup senza crash: scrive un rapporto finto e mostra subito la richiesta.</summary>
    public static void Mock()
    {
        CrashReport.Record("Mock crash (OpSecBar.exe --mock-crash): nothing actually went wrong",
            new InvalidOperationException("This is a test crash used to preview the crash report popup."));
        Ask();
    }

    private static void OpenExternal(string target, string? args = null)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target)
            {
                Arguments = args ?? "", UseShellExecute = true
            })?.Dispose();
        }
        catch (Exception ex) { Log.Error("Apertura di " + target, ex); }
    }
}
