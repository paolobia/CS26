# ARCHITECTURE.md — IDE RAD "VB6-style" per applicazioni Blazor WebAssembly PWA

> Questo documento è la fonte di verità architetturale del progetto. Va letto prima di iniziare qualsiasi task di implementazione. Le decisioni qui contenute non vanno rimesse in discussione senza discuterne esplicitamente — se un task sembra richiedere di violarle, fermati e chiedi conferma invece di procedere.

## 1. Obiettivo del progetto

Un IDE desktop (Windows/Linux) che permette di creare applicazioni **Blazor WebAssembly (.NET 8)**, distribuite come **PWA installabile**, con un'esperienza di sviluppo visuale (RAD) ispirata a Visual Basic 6: form designer drag&drop, property grid, doppio click su un controllo per generare l'handler evento, F5 per eseguire.

## 2. Vincoli architetturali (non negoziabili)

1. **Il designer non ridisegna i controlli.** Usa una WebView embedded dentro Avalonia che carica il vero progetto Blazor dell'utente, servito localmente da `dotnet watch`, in modalità `?design=true`. Ciò che si vede nel designer È l'app reale.
2. **Separazione netta aspetto/logica**, per composizione (non ereditarietà):
   - `IVisualComponent` → layout, stile, proprietà (posseduto dal designer).
   - `ComponentBehavior<T>` → eventi e logica (posseduto dallo sviluppatore).
   - Componente Blazor sottile come collante (`VbButton`, `VbTextBox`, ecc.).
3. **Tre file per ogni Form:**
   - `MyForm.razor` — generato dal designer.
   - `MyForm.razor.designer.cs` — metadati designer, mai editato a mano.
   - `MyForm.Behavior.cs` — scritto dallo sviluppatore, mai toccato dal designer.
4. **Linguaggio di descrizione app: C# puro.** Nessun DSL/XML proprietario intermedio.
5. **Design mode e Run mode = stesso bundle**, pilotato da un flag.
6. **Tutto client-side.** Storage locale (IndexedDB/LocalStorage via JS interop), nessun backend richiesto.

## 3. Struttura repository (fissata, non improvvisare nomi diversi)

```
/repo-root
  ARCHITECTURE.md
  TASKS.md
  /src
    /Ide.App               → Avalonia UI, entry point IDE
    /Ide.Designer           → logica designer, JS interop, generatore codice
    /VbControls              → libreria Blazor riusabile (VbButton, VbTextBox, ...)
    /VbControls.Abstractions → IVisualComponent, ComponentBehavior<T>, attributi
  /templates
    /BlazorPwaTemplate       → template progetto Blazor WASM PWA vuoto
  /samples
    /HelloWorldApp           → primo progetto generato di prova
  IdeSolution.sln
```

## 4. Stack tecnologico (scelte chiuse)

- .NET 8 SDK (fissare versione esatta in `global.json`)
- Avalonia UI 11.x per l'IDE
- AvaloniaEdit per l'editor codice
- Microsoft.CodeAnalysis (Roslyn) per parsing/diagnostica del codice Behavior
- WebView: **`Avalonia.Controls.WebView` (pacchetto ufficiale AvaloniaUI OÜ), versione `12.1.0`** — validato in Fase 0 (2026-08-25).
  - Motivazione della scelta rispetto alle alternative valutate:
    - `WebView.Avalonia` (MicroSugarDeveloperOrg): community, ferma alla 11.0.0.1, poco manutenuta.
    - `CefNet` (CEF): ultima release `105.3.22248.142` legata a Chromium 105 (~2022), progetto di fatto abbandonato; inoltre imbarca un intero runtime Chromium (>100MB) per piattaforma.
    - `Avalonia.Controls.WebView`: pacchetto ufficiale del team Avalonia, mantenuto attivamente (ultimo aggiornamento verificato il 15/08/2026), usa il **webview nativo del sistema operativo** (nessun runtime browser da ridistribuire): `WebView2` su Windows, `WebKitGTK` (via GTK, con adapter sia offscreen/composito sia X11 nativo) su Linux, `WKWebView` su macOS.
  - **Nota di versione**: richiede `Avalonia` core `>= 12.0.0`. La versione `12.1.1` (e successive) del pacchetto `Avalonia` ha rimosso il target `net8.0` (richiede `net10.0`) ed emette source generator compilati per un compilatore Roslyn più recente di quello incluso nell'SDK .NET 8 (`CS9057`/`InitializeComponent` mancante). Per restare su **.NET 8** occorre fissare tutti i pacchetti Avalonia (incluso `Avalonia.Controls.WebView`) alla riga `12.1.0` o `12.0.x`, non alla latest.
  - Dipendenze di sistema:
    - **Linux**: richiede WebKitGTK installato (`libwebkit2gtk-4.0-37`/`4.1`, `libjavascriptcoregtk-4.0-18`/`4.1` — nome pacchetto varia per distro/versione, es. Ubuntu 22.04 usa la serie `4.0`). Va documentato come prerequisito di sistema per l'utente finale (non ridistribuibile via NuGet).
    - **Windows**: richiede il runtime **WebView2** (Evergreen), preinstallato di default su Windows 10 21H2+/11; su versioni precedenti va verificato/distribuito il bootstrapper Evergreen.
  - Stato della validazione:
    - **Linux (Ubuntu 22.04, WebKitGTK 2.50.4)**: build OK; la navigazione verso un file HTML statico locale (`file://`) è stata confermata **funzionalmente** tramite gli eventi `NativeWebView.NavigationStarted`/`NavigationCompleted` (entrambi scattano con l'URL corretto). La verifica **visiva a pixel** non è stata possibile in questo ambiente sandbox perché privo di accesso GPU (`/dev/dri` non accessibile, warning `libEGL: failed to open /dev/dri/card1`), condizione che impedisce la composizione grafica del webview anche forzando il rendering software di Avalonia. **Da riconfermare visivamente su una macchina Linux reale con GPU/driver disponibili.**
    - **Windows**: non verificato in questo ciclo — l'ambiente di sviluppo corrente è un sandbox Linux, non è stato possibile eseguire test su Windows. **Da validare su una macchina Windows reale prima di considerare la Fase 0 chiusa.**
- Blazor WebAssembly .NET 8 per le app generate, PWA standard (manifest + service worker)

## 5. Moduli (ordine di implementazione)

1. Scaffolding solution + template Blazor PWA vuoto
2. VbControls.Abstractions (`IVisualComponent`, `ComponentBehavior<T>`, attributi `[VisualProperty]`)
3. 2-3 controlli base in VbControls (Button, Label, TextBox)
4. IDE Shell Avalonia (finestra, pannelli vuoti, nessuna logica)
5. Integrazione WebView + avvio `dotnet watch` in background
6. Toolbox + drag&drop base (senza persistenza su file ancora)
7. Generatore di codice: scrittura `.razor` + `.razor.designer.cs`
8. Property Grid via reflection
9. Doppio click → generazione handler in `.Behavior.cs`
10. Run (F5) end-to-end
11. Debug
12. Build/Publish/Distribuzione PWA

## 6. Criteri generali di accettazione

Ogni modulo si considera completo solo se:
- `dotnet build` sulla solution intera passa senza errori
- Se il modulo produce un artefatto eseguibile/testabile, viene verificato manualmente (screenshot, log, o comando eseguito) e non solo "scritto"
- Non introduce dipendenze non elencate nello stack senza segnalarlo esplicitamente
- Rispetta la struttura repo e i vincoli architetturali della sezione 2
