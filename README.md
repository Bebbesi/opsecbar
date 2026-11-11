# OpSecBar

![Windows 10/11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?logo=windows&logoColor=white)
![.NET 8](https://img.shields.io/badge/.NET-8-512BD4?logo=dotnet&logoColor=white)
![WPF](https://img.shields.io/badge/UI-WPF-5C2D91)
![Version](https://img.shields.io/badge/version-1.0.0-2ea44f)

**Una taskbar alternativa per Windows 10 e 11: minimale, in alto di default, completamente personalizzabile.**
Sostituisce visivamente e funzionalmente la taskbar di Windows **senza terminare Explorer**, e la ripristina
automaticamente in qualsiasi situazione (uscita, crash, kill da Task Manager).

> **English:** OpSecBar is a minimal, fully customizable replacement taskbar for Windows 10/11 built with WPF and .NET 8.
> It never kills Explorer, and a watchdog process restores the original taskbar if OpSecBar exits or crashes.
> Quick start: `.\scripts\publish.ps1`, then `.\scripts\install.ps1`. Emergency key: `Ctrl + Alt + Shift + Q`.
> The rest of this document is in Italian.

```
[ icone app ]                [ player musicale ]                [ orologio | tray | impostazioni rapide | Start ]
```

## Indice

- [Caratteristiche](#caratteristiche)
- [Requisiti](#requisiti)
- [Avvio rapido](#avvio-rapido)
- [Comandi e scorciatoie](#comandi-e-scorciatoie)
- [Cosa fa](#cosa-fa)
- [Sicurezza della taskbar originale](#sicurezza-della-taskbar-originale)
- [Percorsi dei dati](#percorsi-dei-dati)
- [Note e limiti noti](#note-e-limiti-noti)
- [Sviluppo](#sviluppo)
- [Ringraziamenti](#ringraziamenti)

## Caratteristiche

- **Non chiude Explorer**: nasconde la taskbar di Windows e la ripristina in modo affidabile.
- **Tre zone chiare**: app a sinistra, player musicale al centro, orologio / tray / Start a destra.
- **Menu Start proprio**, apribile anche con il tasto Windows.
- **Player multimediale** integrato (Spotify, browser, VLC…) tramite i controlli multimediali di Windows.
- **System tray reale**, con pannello delle icone nascoste.
- **Personalizzazione totale**: colori, trasparenza, blur / acrylic / glass, angoli, margini, font, posizione, monitor multipli.
- **Temi** predefiniti e personali, importabili ed esportabili in `.json`.
- **Leggera**: pensata per restare sempre in esecuzione con un consumo di memoria ridotto.

## Requisiti

| | |
| --- | --- |
| Sistema operativo | Windows 10 (build 17763+) o Windows 11, x64 |
| Per compilare | [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) |
| Per eseguire | .NET 8 Desktop Runtime (non serve se compili con `-SelfContained`) |

## Avvio rapido

```powershell
.\scripts\publish.ps1        # compila in dist\OpSecBar
.\scripts\install.ps1        # copia in %LocalAppData%\Programs\OpSecBar, crea il collegamento e avvia
```

Per distribuire una versione che funziona su qualsiasi PC senza installare il runtime:

```powershell
.\scripts\publish.ps1 -SelfContained
```

Per provarlo senza installare: `dist\OpSecBar\OpSecBar.exe`.
L'installazione è per l'utente corrente e **non richiede privilegi di amministratore**.
L'avvio automatico con Windows si attiva da *Impostazioni → Sistema*.

## Comandi e scorciatoie

| Comando | Effetto |
| --- | --- |
| `OpSecBar.exe` | Avvia la barra. Se è già attiva, apre le Impostazioni. |
| `OpSecBar.exe --restore` | Chiude OpSecBar e ripristina la taskbar di Windows. |
| `scripts\Ripristina-Taskbar.cmd` | Come sopra, con doppio click. |
| `scripts\install.ps1 -Uninstall` | Disinstalla (ripristina la taskbar e rimuove file, collegamento e avvio automatico). |

> **Tasto di emergenza: `Ctrl + Alt + Maiusc + Q`** chiude OpSecBar e ripristina subito la taskbar di Windows.

## Cosa fa

### Sinistra: app
Solo icone, senza etichette: app preferite + app aperte. Più finestre della stessa app vengono raggruppate in un'unica icona.

| Azione | Effetto |
| --- | --- |
| Click | Apri / porta in primo piano / riduci a icona |
| Click centrale | Nuova finestra |
| Click destro | Menu: nuova finestra, esegui come amministratore, preferiti, chiudi |

### Centro: player musicale
Opzionale, basato sui controlli multimediali di Windows: copertina, titolo, artista,
precedente / play-pausa / successivo. Al click si espande con la barra di avanzamento.

### Destra: sistema
- Orologio con data.
- System tray reale: icone visibili e pannello delle icone nascoste; click sinistro e destro inoltrati alle app.
- Impostazioni rapide di Windows e pulsante Start.
- Opzionale: CPU / RAM / batteria.

### Menu Start
Ricerca, preferiti, tutte le app installate (Win32 e Store), blocca / disconnetti / sospendi / riavvia / arresta.
Si apre dal pulsante Start **e dal tasto Windows**; le altre scorciatoie (Win+E, Win+D, Win+L…) restano di Windows.

### Impostazioni
| Area | Opzioni |
| --- | --- |
| Aspetto | colore, opacità, blur / acrylic / glass / trasparente, angoli, bordo, ombra, font |
| Layout | altezza, margine (barra flottante), dimensione e spaziatura icone |
| Posizione | alto / basso / sinistra / destra; sempre visibile o auto-hide; schermo intero; più monitor |
| Elementi | ogni parte attivabile singolarmente; icone del tray sempre visibili |

### Temi
Windows 11 Minimal, Glassmorphism, macOS Inspired, Transparent, Dark, Light, Custom.
Puoi salvare i tuoi temi, importarli ed esportarli come `.json`.

## Sicurezza della taskbar originale

1. **Explorer non viene mai chiuso**: la taskbar di Windows viene messa in auto-hide e nascosta; lo stato originale viene salvato su disco.
2. **Ripristino all'uscita**: menu, Impostazioni, `--restore`, tasto di emergenza e spegnimento riportano tutto com'era.
3. **Watchdog**: un secondo processo leggero sorveglia OpSecBar. Se va in crash o viene terminato dal Task Manager,
   ripristina la taskbar entro mezzo secondo.
4. **Ripristino all'avvio**: se anche il watchdog fallisse, al successivo avvio OpSecBar ripristina lo stato salvato
   prima di fare qualsiasi altra cosa.

Se qualcosa va storto, basta `scripts\Ripristina-Taskbar.cmd` o `Ctrl + Alt + Maiusc + Q`.

## Percorsi dei dati

| Cosa | Dove |
| --- | --- |
| Programma installato | `%LocalAppData%\Programs\OpSecBar` |
| Impostazioni (salvate in automatico) | `%AppData%\OpSecBar\settings.json` |
| Temi personali | `%AppData%\OpSecBar\themes` |
| Log | `%AppData%\OpSecBar\opsecbar.log` |

La disinstallazione lascia impostazioni e temi in `%AppData%\OpSecBar`: eliminali a mano se non servono.

## Note e limiti noti

- **Blur / Acrylic / Glass** richiedono gli *Effetti di trasparenza* di Windows (Impostazioni → Personalizzazione → Colori).
  Se sono disattivati, OpSecBar mostra una semplice trasparenza senza sfocatura e lo segnala nella pagina Aspetto.
- Le **impostazioni rapide** e il **centro notifiche** sono quelli di Windows: si aprono nella loro posizione abituale.
- Il tray mostra le icone che Windows espone come icone di notifica classiche. Se rete o batteria non compaiono
  (dipende dalla versione di Windows 11), si raggiungono dal pulsante delle impostazioni rapide.
- Le icone del tray sono disegnate dalle singole app: su una barra chiara le icone bianche possono risultare poco visibili.

## Sviluppo

```powershell
dotnet build OpSecBar.sln        # compilazione di debug
dotnet run --project src\OpSecBar
```

### Struttura del codice

```
scripts/
  publish.ps1              compila in dist\OpSecBar (-SelfContained per includere il runtime)
  install.ps1              installa / disinstalla per l'utente corrente
  Ripristina-Taskbar.cmd   ripristino con doppio click
src/OpSecBar/
  Program.cs               punto di ingresso; modalità leggere --watchdog e --restore
  App.xaml(.cs)            composizione: servizi, barre, ciclo di vita, gestione errori
  Models/AppSettings.cs    impostazioni, tema (ThemeData), app fissate
  Services/
    SettingsService        caricamento, salvataggio automatico, notifiche di modifica
    ThemeService           temi predefiniti e dell'utente, risorse dinamiche della UI
    TaskbarGuard           nascondi / ripristina la taskbar di Windows, watchdog
    TaskListService        app fissate + finestre aperte -> icone della barra
    MediaService           controlli multimediali di sistema
    AppCatalogService      app installate (menu Start), avvio
    IconService            icone ad alta risoluzione dalla shell
    BackdropHelper/Window  blur e acrylic, anche con angoli arrotondati
    WinKeyHook             tasto Windows -> menu Start di OpSecBar
    AutoStartService, SystemInfoService
  Windows/                 TaskbarWindow, StartMenuWindow, SettingsWindow
  Controls/                MediaWidget, TrayArea
  Themes/Controls.xaml     stili condivisi (pulsanti, menu, tooltip, slider)
```

### Aggiungere un'opzione

1. Aggiungi una proprietà in `AppSettings` (o `ThemeData`).
2. Aggiungi una riga nella pagina giusta di `SettingsWindow.xaml.cs`.
3. Usa il valore in `TaskbarWindow.ApplyTheme` / `ApplyElements`.

## Ringraziamenti

Finestre aperte, system tray e registrazione come AppBar usano la libreria open source
[ManagedShell](https://github.com/cairoshell/ManagedShell) (Apache 2.0), la stessa di RetroBar e Cairo Shell.
