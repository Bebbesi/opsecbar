using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpSecBar.Core;

namespace OpSecBar.Services;

public enum UpdateState { Idle, Checking, UpToDate, Available, Downloading, Ready, Failed }

/// <summary>
/// Aggiornamento automatico dalle release di GitHub.
/// A ogni avvio controlla l'ultima release pubblicata (o, con le pre-release attive, la più recente in assoluto,
/// build di prova "b20261112" comprese); se è più recente della versione in esecuzione scarica lo zip,
/// ne verifica l'impronta SHA-256 (quando GitHub la fornisce), lo estrae e sostituisce i file dopo l'uscita dell'app,
/// che poi viene riavviata. La taskbar di Windows viene ripristinata durante il cambio, come in una normale uscita.
/// </summary>
public sealed class UpdateService
{
    public const string Repository = "Bebbesi/opsecbar";
    /// <summary>Secondo repository: si guarda qui solo se il primo non ha un aggiornamento da proporre.</summary>
    public const string FallbackRepository = "Bebbesi/opentaskbar";
    private static readonly string[] AllowedDownloadPrefixes =
    {
        "https://github.com/" + Repository + "/releases/download/",
        "https://github.com/" + FallbackRepository + "/releases/download/"
    };

    private static readonly HttpClient Http = CreateClient();
    private string? _downloadUrl, _expectedSha256, _stagedDir;
    private bool _busy;

    public UpdateState State { get; private set; } = UpdateState.Idle;
    public Version CurrentVersion { get; } = Normalize(typeof(UpdateService).Assembly.GetName().Version ?? new Version(1, 0, 0));
    /// <summary>Tag della build di prova in esecuzione ("b20261112", "b20261112-2"); null per una release normale.</summary>
    public string? CurrentBuild { get; } = ReadBuildTag(typeof(UpdateService).Assembly);
    /// <summary>Tag GitHub della versione in esecuzione.</summary>
    public string CurrentTag => CurrentBuild ?? "v" + CurrentVersion.ToString(3);
    /// <summary>Versione in esecuzione da mostrare: "1.1.3" oppure "1.1.3 (b20261112)".</summary>
    public string CurrentLabel => CurrentBuild == null ? CurrentVersion.ToString(3) : $"{CurrentVersion.ToString(3)} ({CurrentBuild})";

    /// <summary>Tag della release trovata dall'ultimo controllo.</summary>
    public string? LatestTag { get; private set; }
    public string LatestLabel => LatestTag == null ? "" : Display(LatestTag);
    /// <summary>La release trovata è una build di prova o è segnata come pre-release su GitHub.</summary>
    public bool LatestIsPrerelease { get; private set; }
    /// <summary>Si lascia la build di prova installata per tornare a una release normale.</summary>
    public bool LeavingPrerelease => CurrentBuild != null && LatestTag != null && !LatestIsPrerelease;
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

    /// <summary>Tag di una build di prova: "b" + anno, mese, giorno, con "-2", "-3"... se ce n'è più di una nello stesso giorno.</summary>
    private static readonly Regex BuildTagPattern = new(@"^b\d{8}(-\d+)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool IsBuildTag(string? tag) => tag != null && BuildTagPattern.IsMatch(tag);

    /// <summary>Numero di una release normale ("v1.2.0" → 1.2.0); null per le build di prova e i tag non riconosciuti.</summary>
    private static Version? ParseStable(string? tag)
    {
        string s = (tag ?? "").Trim().TrimStart('v', 'V');
        return !s.Contains('-') && Version.TryParse(s, out Version? v) ? Normalize(v) : null;
    }

    /// <summary>
    /// Tag della build di prova in esecuzione (null per una release normale). La pipeline di rilascio lo scrive
    /// nell'InformationalVersion, es. "1.1.3-b20261112" (+ l'eventuale "+commit" aggiunto dall'SDK).
    /// </summary>
    private static string? ReadBuildTag(Assembly assembly)
    {
        string? info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (info == null) return null;
        int plus = info.IndexOf('+');
        if (plus >= 0) info = info[..plus];
        int dash = info.IndexOf('-');
        string label = dash >= 0 ? info[(dash + 1)..] : "";
        return IsBuildTag(label) ? label.ToLowerInvariant() : null;
    }

    private static DateTimeOffset PublishedAt(JsonElement release)
    {
        foreach (string key in new[] { "published_at", "created_at" })
            if (release.TryGetProperty(key, out JsonElement d) && d.ValueKind == JsonValueKind.String && d.TryGetDateTimeOffset(out DateTimeOffset when))
                return when;
        return DateTimeOffset.MinValue;
    }

    private void SetLatest(string tag, JsonElement release)
    {
        LatestTag = tag;
        LatestIsPrerelease = IsBuildTag(tag) || (release.TryGetProperty("prerelease", out JsonElement pre) && pre.ValueKind == JsonValueKind.True);
    }

    private static string Display(string tag) => ParseStable(tag)?.ToString(3) ?? tag;

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    private void Set(UpdateState state, string? error = null)
    {
        State = state;
        Error = error;
        Changed?.Invoke();
    }

    private sealed record Release(string Repo, JsonElement Json, string Tag, DateTimeOffset Date);

    /// <summary>
    /// Release pubblicate di un repository, dalla più recente: tutte, oppure solo l'ultima release normale.
    /// Lista vuota se non ce ne sono; null (con il motivo) se il controllo non è riuscito.
    /// </summary>
    private static async Task<(List<Release>? Releases, string? Error)> FetchAsync(string repo, bool all)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            string url = "https://api.github.com/repos/" + repo + (all ? "/releases?per_page=100" : "/releases/latest");
            using HttpResponseMessage response = await Http.GetAsync(url, cts.Token);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return (new List<Release>(), null); // nessuna release pubblicata
            response.EnsureSuccessStatusCode();

            using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cts.Token));
            IEnumerable<JsonElement> items = all ? doc.RootElement.EnumerateArray() : new[] { doc.RootElement };
            // Le build di prova (b20261112) non hanno un numero di versione: release e build si ordinano per data.
            List<Release> releases = items
                .Where(r => !(r.TryGetProperty("draft", out JsonElement d) && d.ValueKind == JsonValueKind.True))
                .Select(r => new Release(repo, r.Clone(), r.GetProperty("tag_name").GetString() ?? "", PublishedAt(r)))
                .Where(r => r.Tag.Length > 0)
                .OrderByDescending(r => r.Date)
                .ToList();
            return (releases, null);
        }
        catch (Exception ex)
        {
            Log.Warn($"Controllo aggiornamenti ({repo}): {ex.Message}");
            return (null, ex is TaskCanceledException ? "The update server did not respond." : ex.Message);
        }
    }

    private bool IsCurrent(Release r) => r.Tag.Equals(CurrentTag, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Controlla l'ultima release su <see cref="Repository"/> e, se lì non c'è un aggiornamento, su
    /// <see cref="FallbackRepository"/>. Con <paramref name="install"/> scarica e prepara subito l'aggiornamento.
    /// </summary>
    /// <param name="includePrereleases">
    /// Considera anche le build di prova: vale la release pubblicata più di recente, qualunque sia. Senza, vale l'ultima
    /// release; chi ha una build di prova installata torna a quella, anche se è più vecchia.
    /// </param>
    /// <returns>True se un aggiornamento è pronto per essere applicato (chiamare <see cref="ApplyAndRestart"/>).</returns>
    public async Task<bool> CheckAsync(bool install, bool includePrereleases = false)
    {
        if (_busy) return false;
        _busy = true;
        try
        {
            Set(UpdateState.Checking);
            (List<Release>? primaryResult, string? primaryError) = await FetchAsync(Repository, includePrereleases);
            List<Release> primary = primaryResult ?? new List<Release>();
            List<Release> fallback = new();
            Release? pick = null, shown = primary.FirstOrDefault();

            if (includePrereleases)
            {
                Release? mine = primary.Find(IsCurrent);
                if (mine != null && primary[0].Date > mine.Date) pick = primary[0];
                else
                {
                    fallback = (await FetchAsync(FallbackRepository, true)).Releases ?? fallback;
                    // Tra i due repository ci si sposta solo verso una release pubblicata dopo quella installata:
                    // così nessuno dei due riporta indietro a una versione che l'altro ha già superato.
                    DateTimeOffset? installed = (mine ?? fallback.Find(IsCurrent))?.Date;
                    // La versione installata non è (più) su GitHub. Una release più avanti di tutte quelle pubblicate
                    // (build locale) resta dov'è; altrimenti si passa alla più recente. Se il primo repository non ha
                    // risposto non si sa abbastanza per decidere, e si resta dove si è.
                    Version? newestStable = primary.Concat(fallback).Select(r => ParseStable(r.Tag)).Where(v => v != null).Max();
                    bool unknownIsOlder = primaryError == null && (CurrentBuild != null || newestStable == null || CurrentVersion <= newestStable);
                    bool Newer(Release r) => !IsCurrent(r) && (installed is DateTimeOffset date ? r.Date > date : unknownIsOlder);

                    if (primary.Count > 0 && Newer(primary[0])) pick = primary[0];
                    else if (fallback.Count > 0 && Newer(fallback[0])) pick = fallback[0];
                    shown ??= fallback.FirstOrDefault();
                }
            }
            else
            {
                if (shown != null && ParseStable(shown.Tag) == null)
                {
                    primaryError = $"Unrecognized release tag '{shown.Tag}'.";
                    shown = null;
                }
                // Da una build di prova si torna all'ultima release, anche se ha un numero di versione più basso.
                if (shown != null && (CurrentBuild != null || ParseStable(shown.Tag) > CurrentVersion)) pick = shown;
                else
                {
                    fallback = (await FetchAsync(FallbackRepository, false)).Releases ?? fallback;
                    Release? other = fallback.FirstOrDefault(r => ParseStable(r.Tag) != null);
                    if (other != null && (ParseStable(other.Tag) > CurrentVersion || (CurrentBuild != null && primaryError == null))) pick = other;
                    shown ??= other;
                }
            }

            shown = pick ?? shown;
            if (shown != null) SetLatest(shown.Tag, shown.Json);
            if (pick == null)
            {
                if (primaryError != null) Set(UpdateState.Failed, primaryError);
                else Set(UpdateState.UpToDate);
                return false;
            }

            _downloadUrl = null;
            _expectedSha256 = null;
            if (pick.Json.TryGetProperty("assets", out JsonElement assets))
            {
                foreach (JsonElement asset in assets.EnumerateArray())
                {
                    string name = asset.GetProperty("name").GetString() ?? "";
                    string url = asset.GetProperty("browser_download_url").GetString() ?? "";
                    if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                        !AllowedDownloadPrefixes.Any(p => url.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;
                    _downloadUrl = url;
                    if (asset.TryGetProperty("digest", out JsonElement digest) && digest.GetString() is string d && d.StartsWith("sha256:"))
                        _expectedSha256 = d["sha256:".Length..];
                    if (name.Contains("win-x64", StringComparison.OrdinalIgnoreCase)) break;
                }
            }

            if (_downloadUrl == null)
            {
                Set(UpdateState.Failed, $"Release {LatestLabel} has no downloadable package.");
                return false;
            }

            Log.Info($"Aggiornamento disponibile: {CurrentLabel} -> {LatestLabel}{(LatestIsPrerelease ? " (pre-release)" : "")} da {pick.Repo}");
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

        string zipPath = Path.Combine(updateRoot, $"OpSecBar-{LatestLabel}.zip");
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
    /// Avvia lo script che attende la chiusura di Open TaskBar, copia i nuovi file e lo riavvia.
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
                "rem Generated by Open TaskBar: waits for the app to exit, copies the new files, starts it again.",
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
            Log.Info($"Installazione aggiornamento {LatestLabel} avviata");
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
