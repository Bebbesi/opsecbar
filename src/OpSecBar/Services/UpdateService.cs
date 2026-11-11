using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using OpSecBar.Core;

namespace OpSecBar.Services;

public enum UpdateState { Idle, Checking, UpToDate, Available, Downloading, Ready, Failed }

/// <summary>
/// Aggiornamento automatico dalle release di GitHub.
/// A ogni avvio controlla l'ultima release pubblicata; se è più recente della versione in esecuzione scarica lo zip,
/// ne verifica l'impronta SHA-256 (quando GitHub la fornisce), lo estrae e sostituisce i file dopo l'uscita dell'app,
/// che poi viene riavviata. La taskbar di Windows viene ripristinata durante il cambio, come in una normale uscita.
/// </summary>
public sealed class UpdateService
{
    public const string Repository = "Bebbesi/opsecbar";
    private const string LatestReleaseUrl = "https://api.github.com/repos/" + Repository + "/releases/latest";
    private const string AllowedDownloadPrefix = "https://github.com/" + Repository + "/releases/download/";

    private static readonly HttpClient Http = CreateClient();
    private string? _downloadUrl, _expectedSha256, _stagedDir;
    private bool _busy;

    public UpdateState State { get; private set; } = UpdateState.Idle;
    public Version CurrentVersion { get; } = Normalize(typeof(UpdateService).Assembly.GetName().Version ?? new Version(1, 0, 0));
    public Version? LatestVersion { get; private set; }
    public string? Error { get; private set; }

    /// <summary>Sollevato (su un thread qualsiasi) quando cambia lo stato.</summary>
    public event Action? Changed;

    /// <summary>Le build di sviluppo (cartella bin) non si aggiornano da sole.</summary>
    public static bool CanSelfUpdate
    {
        get
        {
            string dir = AppContext.BaseDirectory;
            return !dir.Contains("\\bin\\Debug", StringComparison.OrdinalIgnoreCase) &&
                   !dir.Contains("\\bin\\Release", StringComparison.OrdinalIgnoreCase);
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OpSecBar-Updater");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    private void Set(UpdateState state, string? error = null)
    {
        State = state;
        Error = error;
        Changed?.Invoke();
    }

    /// <summary>Controlla l'ultima release. Con <paramref name="install"/> scarica e prepara subito l'aggiornamento.</summary>
    /// <returns>True se un aggiornamento è pronto per essere applicato (chiamare <see cref="ApplyAndRestart"/>).</returns>
    public async Task<bool> CheckAsync(bool install)
    {
        if (_busy) return false;
        _busy = true;
        try
        {
            Set(UpdateState.Checking);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using HttpResponseMessage response = await Http.GetAsync(LatestReleaseUrl, cts.Token);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                Set(UpdateState.UpToDate); // nessuna release pubblicata
                return false;
            }
            response.EnsureSuccessStatusCode();

            using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cts.Token));
            JsonElement root = doc.RootElement;
            string tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out Version? latest))
            {
                Set(UpdateState.Failed, $"Unrecognized release tag '{tag}'.");
                return false;
            }
            LatestVersion = Normalize(latest);

            _downloadUrl = null;
            _expectedSha256 = null;
            if (root.TryGetProperty("assets", out JsonElement assets))
            {
                foreach (JsonElement asset in assets.EnumerateArray())
                {
                    string name = asset.GetProperty("name").GetString() ?? "";
                    string url = asset.GetProperty("browser_download_url").GetString() ?? "";
                    if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || !url.StartsWith(AllowedDownloadPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                    _downloadUrl = url;
                    if (asset.TryGetProperty("digest", out JsonElement digest) && digest.GetString() is string d && d.StartsWith("sha256:"))
                        _expectedSha256 = d["sha256:".Length..];
                    if (name.Contains("win-x64", StringComparison.OrdinalIgnoreCase)) break;
                }
            }

            if (LatestVersion <= CurrentVersion)
            {
                Set(UpdateState.UpToDate);
                return false;
            }
            if (_downloadUrl == null)
            {
                Set(UpdateState.Failed, $"Release {LatestVersion} has no downloadable package.");
                return false;
            }

            Log.Info($"Aggiornamento disponibile: {CurrentVersion} -> {LatestVersion}");
            Set(UpdateState.Available);
            if (!install) return false;
            return await DownloadCoreAsync();
        }
        catch (Exception ex)
        {
            Log.Warn("Controllo aggiornamenti: " + ex.Message);
            Set(UpdateState.Failed, ex is TaskCanceledException ? "The update server did not respond." : ex.Message);
            return false;
        }
        finally { _busy = false; }
    }

    /// <summary>Scarica l'aggiornamento già trovato da <see cref="CheckAsync"/>.</summary>
    public async Task<bool> DownloadAsync()
    {
        if (_busy || State != UpdateState.Available) return false;
        _busy = true;
        try { return await DownloadCoreAsync(); }
        catch (Exception ex)
        {
            Log.Warn("Download aggiornamento: " + ex.Message);
            Set(UpdateState.Failed, ex.Message);
            return false;
        }
        finally { _busy = false; }
    }

    private async Task<bool> DownloadCoreAsync()
    {
        if (!CanSelfUpdate)
        {
            Set(UpdateState.Failed, "Development builds are not updated automatically.");
            return false;
        }

        Set(UpdateState.Downloading);
        string updateRoot = Path.Combine(AppPaths.DataDir, "update");
        try { if (Directory.Exists(updateRoot)) Directory.Delete(updateRoot, true); } catch { }
        Directory.CreateDirectory(updateRoot);

        string zipPath = Path.Combine(updateRoot, $"OpSecBar-{LatestVersion}.zip");
        await using (Stream source = await Http.GetStreamAsync(_downloadUrl))
        await using (FileStream target = File.Create(zipPath))
            await source.CopyToAsync(target);

        if (_expectedSha256 != null)
        {
            await using FileStream file = File.OpenRead(zipPath);
            string actual = Convert.ToHexString(await SHA256.HashDataAsync(file));
            if (!actual.Equals(_expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                Set(UpdateState.Failed, "The downloaded package failed its integrity check.");
                return false;
            }
        }

        string extractDir = Path.Combine(updateRoot, "files");
        ZipFile.ExtractToDirectory(zipPath, extractDir, true);
        // Lo zip può avere i file alla radice o dentro una cartella: si cerca dove sta l'eseguibile.
        string? exe = Directory.EnumerateFiles(extractDir, "OpSecBar.exe", SearchOption.AllDirectories).FirstOrDefault();
        if (exe == null)
        {
            Set(UpdateState.Failed, "The downloaded package does not contain OpSecBar.exe.");
            return false;
        }

        _stagedDir = Path.GetDirectoryName(exe)!;
        Set(UpdateState.Ready);
        return true;
    }

    /// <summary>
    /// Avvia lo script che attende la chiusura di OpSec Bar, copia i nuovi file e lo riavvia.
    /// Il chiamante deve poi chiudere l'app (uscita normale, che ripristina la taskbar di Windows).
    /// </summary>
    public bool ApplyAndRestart()
    {
        if (State != UpdateState.Ready || _stagedDir == null) return false;
        try
        {
            string installDir = AppContext.BaseDirectory.TrimEnd('\\');
            string script = Path.Combine(AppPaths.DataDir, "update", "apply-update.cmd");
            File.WriteAllText(script, string.Join("\r\n",
                "@echo off",
                "rem Generated by OpSec Bar: waits for the app to exit, copies the new files, starts it again.",
                "set /a tries=0",
                ":wait",
                "tasklist /fi \"imagename eq OpSecBar.exe\" 2>nul | find /i \"OpSecBar.exe\" >nul",
                "if not errorlevel 1 (",
                "  set /a tries+=1",
                "  if %tries% geq 30 taskkill /im OpSecBar.exe /f >nul 2>&1",
                "  timeout /t 1 /nobreak >nul",
                "  goto wait",
                ")",
                $"robocopy \"{_stagedDir}\" \"{installDir}\" /E /R:5 /W:1 /NFL /NDL /NJH /NJS /NP >nul",
                $"start \"\" \"{Path.Combine(installDir, "OpSecBar.exe")}\" --updated",
                ""));

            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetTempPath()
            });
            Log.Info($"Installazione aggiornamento {LatestVersion} avviata");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Avvio installazione aggiornamento", ex);
            Set(UpdateState.Failed, ex.Message);
            return false;
        }
    }
}
