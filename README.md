# OpSecBar

Taskbar alternativa per Windows 10 e 11: minimale, in alto di default, completamente personalizzabile.
Sostituisce visivamente e funzionalmente la taskbar di Windows **senza terminare Explorer**.

```
[ icone app ]                [ player musicale ]                [ orologio | tray | impostazioni rapide | Start ]
```

## Avvio rapido

Serve il **.NET 8 SDK** per compilare (e il .NET 8 Desktop Runtime per eseguire, se non usi `-SelfContained`).

```powershell
.\scripts\publish.ps1        # compila in dist\OpSecBar
.\scripts\install.ps1        # copia in %LocalAppData%\Programs\OpSecBar, crea il collegamento e avvia
```

Oppure, per provarlo senza installare: `dist\OpSecBar\OpSecBar.exe`.

| Comando | Effetto |
| --- | --- |
| `OpSecBar.exe` | Avvia la barra. Se è già attiva, apre le Impostazioni. |
| `OpSecBar.exe --restore` | Chiude OpSecBar e ripristina la taskbar di Windows. |
| `scripts\Ripristina-Taskbar.cmd` | Come sopra, con doppio click. |
| `scripts\install.ps1 -Uninstall` | Disinstalla. |

**Tasto di emergenza: `Ctrl + Alt + Maiusc + Q`** chiude OpSecBar e ripristina subito la taskbar di Windows.

## Cosa fa

- **Sinistra** – solo icone, senza etichette: app preferite + app aperte. Click = apri / porta in primo piano / riduci a icona;
  click centrale = nuova finestra; click destro = menu (nuova finestra, esegui come amministratore, preferiti, chiudi).
  Più finestre della stessa app vengono raggruppate in un'unica icona.
- **Centro** – player musicale opzionale basato sui controlli multimediali di Windows (Spotify, browser, VLC…):
  copertina, titolo, artista, precedente / play-pausa / successivo; al click si espande con barra di avanzamento.
- **Destra** – orologio con data, system tray reale (icone visibili + pannello delle icone nascoste, click sinistro/destro
  inoltrati alle app), impostazioni rapide di Windows, pulsante Start. Opzionale: CPU / RAM / batteria.
- **Menu Start** – ricerca, preferiti, tutte le app installate (Win32 e Store), blocca / disconnetti / sospendi / riavvia / arresta.
  Si apre dal pulsante Start **e dal tasto Windows** (le scorciatoie Win+E, Win+D, Win+L… restano di Windows).
- **Impostazioni** – colore, opacità, blur / acrylic / glass / trasparente, angoli, altezza, margine (barra flottante), bordo,
  ombra, dimensione e spaziatura icone, font; posizione (alto / basso / sinistra / destra); sempre visibile o auto-hide;
  schermo intero; più monitor; elementi attivabili singolarmente; icone del tray sempre visibili.
- **Temi** – Windows 11 Minimal, Glassmorphism, macOS Inspired, Transparent, Dark, Light, Custom; salva, importa ed esporta (`.json`).

Le impostazioni si salvano da sole in `%AppData%\OpSecBar\settings.json`; i temi in `%AppData%\OpSecBar\themes`; il log in `opsecbar.log`.

## Sicurezza della taskbar originale

1. Explorer non viene mai chiuso: la taskbar di Windows viene messa in auto-hide e nascosta; lo stato originale viene salvato su disco.
2. All'uscita (menu, Impostazioni, `--restore`, tasto di emergenza, spegnimento) viene ripristinata.
3. Un secondo processo leggero (**watchdog**) sorveglia OpSecBar: se va in crash o viene terminato dal Task Manager,
   ripristina la taskbar entro mezzo secondo.
4. Se anche il watchdog fallisse, al successivo avvio OpSecBar ripristina lo stato salvato prima di fare qualsiasi altra cosa.

## Note

- **Blur / Acrylic / Glass** richiedono gli *Effetti di trasparenza* di Windows (Impostazioni → Personalizzazione → Colori).
  Se sono disattivati, OpSecBar mostra una semplice trasparenza senza sfocatura e lo segnala nella pagina Aspetto.
- Le **impostazioni rapide** e il **centro notifiche** sono quelli di Windows: si aprono nella loro posizione abituale.
- Il tray mostra le icone che Windows espone come icone di notifica classiche. Se rete o batteria non compaiono
  (dipende dalla versione di Windows 11), si raggiungono dal pulsante delle impostazioni rapide.
- Le icone del tray sono disegnate dalle singole app: su una barra chiara le icone bianche possono risultare poco visibili.

## Struttura del codice

```
src/OpSecBar/
  Program.cs            punto di ingresso; modalità leggere --watchdog e --restore
  App.xaml(.cs)         composizione: servizi, barre, ciclo di vita, gestione errori
  Models/AppSettings.cs impostazioni, tema (ThemeData), app fissate
  Services/
    SettingsService     caricamento, salvataggio automatico, notifiche di modifica
    ThemeService        temi predefiniti e dell'utente, risorse dinamiche della UI
    TaskbarGuard        nascondi / ripristina la taskbar di Windows, watchdog
    TaskListService     app fissate + finestre aperte -> icone della barra
    MediaService        controlli multimediali di sistema
    AppCatalogService   app installate (menu Start), avvio
    IconService         icone ad alta risoluzione dalla shell
    BackdropHelper/Window  blur e acrylic, anche con angoli arrotondati
    WinKeyHook          tasto Windows -> menu Start di OpSecBar
    AutoStartService, SystemInfoService
  Windows/              TaskbarWindow, StartMenuWindow, SettingsWindow
  Controls/             MediaWidget, TrayArea
  Themes/Controls.xaml  stili condivisi (pulsanti, menu, tooltip, slider)
```

Finestre aperte, system tray e registrazione come AppBar usano la libreria open source
[ManagedShell](https://github.com/cairoshell/ManagedShell) (Apache 2.0), la stessa di RetroBar e Cairo Shell.

Per aggiungere un'opzione: una proprietà in `AppSettings` (o `ThemeData`), una riga nella pagina giusta di
`SettingsWindow.xaml.cs`, e l'uso del valore in `TaskbarWindow.ApplyTheme` / `ApplyElements`.
