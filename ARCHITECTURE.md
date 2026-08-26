# ARCHITECTURE.md — IDE RAD "VB6-style" per applicazioni Blazor WebAssembly PWA

> Questo documento è la fonte di verità architetturale del progetto. Va letto prima di iniziare qualsiasi task di implementazione. Le decisioni qui contenute non vanno rimesse in discussione senza discuterne esplicitamente — se un task sembra richiedere di violarle, fermati e chiedi conferma invece di procedere.

## 1. Obiettivo del progetto

Un IDE desktop (Windows/Linux) che permette di creare applicazioni **Blazor WebAssembly (.NET 8)**, distribuite come **PWA installabile**, con un'esperienza di sviluppo visuale (RAD) ispirata a Visual Basic 6: form designer drag&drop, property grid, doppio click su un controllo per generare l'handler evento, F5 per eseguire.

**Filosofia "tutto vive nel Form"** (ispirata a C++Builder/Delphi, non solo a VB6): un Form non contiene solo controlli visibili. Qualunque componente — un bottone, ma anche un client HTTP, un timer, un accesso a LocalStorage — è un cittadino di prima classe del Form: ha un nome, appare nella Property Grid, viene generato come campo nello stesso file designer. La differenza fra "visuale" e "non-visuale" è solo se produce una resa grafica *a runtime*: in fase di design, entrambi hanno una rappresentazione sulla superficie (un controllo vero per i visuali, un'icona per i non-visuali), coerentemente col vincolo n.1 (il designer non ridisegna nulla, mostra l'app vera). Dettagli in sezione 2.1.

## 2. Vincoli architetturali (non negoziabili)

1. **Il designer non ridisegna i controlli.** Usa una WebView embedded dentro Avalonia che carica il vero progetto Blazor dell'utente, servito localmente da `dotnet watch`, in modalità `?design=true`. Ciò che si vede nel designer È l'app reale.
2. **Separazione netta aspetto/logica**, per composizione (non ereditarietà). Per "aspetto" si intende la rappresentazione nel designer — resa grafica a runtime per un componente visuale, semplice icona di design per uno non-visuale (sezione 2.1) — mai la logica applicativa:
   - `IVisualComponent`/`INonVisualComponent` → layout (o posizione dell'icona), stile, proprietà (posseduto dal designer).
   - `ComponentBehavior<T>` → logica di default riusabile fornita da chi scrive un componente `VbControls` (non l'event wiring del Form: quello, generato dal doppio click, sono metodi piatti nella partial class del Form — vedi sezione 2.1).
   - Componente Blazor sottile come collante (`VbButton`, `VbTextBox`, ecc.).
3. **Tre file per ogni Form:**
   - `MyForm.razor` — generato dal designer.
   - `MyForm.razor.designer.cs` — metadati designer, mai editato a mano.
   - `MyForm.Behavior.cs` — scritto dallo sviluppatore, mai toccato dal designer.
4. **Linguaggio di descrizione app: C# puro.** Nessun DSL/XML proprietario intermedio.
5. **Design mode e Run mode = stesso bundle**, pilotato da un flag.
6. **Tutto client-side.** Storage locale (IndexedDB/LocalStorage via JS interop), nessun backend richiesto.

## 2.1. Componenti visuali e non-visuali (2026-08-26)

Decisione di design a lungo termine, discussa esplicitamente con l'utente: estendere il modello di componente per supportare, oltre ai controlli visuali (`VbButton`, `VbLabel`, `VbTextBox`), componenti **non-visuali** nello stile C++Builder/Delphi — un client HTTP, un timer, un wrapper su LocalStorage: vivono sul Form, sono configurabili, ma non producono nulla nel DOM a runtime.

**Gerarchia delle interfacce** in `VbControls.Abstractions` (evoluzione non distruttiva dell'attuale `IVisualComponent`, che oggi obbliga `LayoutBox` + `StyleModel` per qualsiasi cosa):
- `IDesignComponent` (nuova base): `Id`, `LayoutBox`, `Properties`. `LayoutBox` qui significa "dove sta la sua rappresentazione nel designer" — non implica che a runtime ci sia una resa grafica in quella posizione.
- `IVisualComponent : IDesignComponent` — contratto invariato per chi lo consuma oggi; aggiunge solo `StyleModel` (colori/font/visibilità, ha senso solo se c'è qualcosa da vedere a runtime). `VbButton`, `VbLabel`, `VbTextBox` non cambiano.
- `INonVisualComponent : IDesignComponent` (nuova) — nessun `StyleModel`: a runtime non c'è nulla da stilare perché non c'è nulla da vedere. `LayoutBox` resta solo per posizionare l'icona nel designer.

**Il componente Blazor "collante"** (vincolo n.2) per un tipo non-visuale renderizza *solo* un'icona di design, condizionata al flag `?design=true` introdotto nel modulo 10 (finora un no-op mai consumato — questo ne è il primo utilizzo reale):

```razor
@if (IsDesignMode) {
    <div class="vb-nonvisual-icon" style="position:absolute;left:...px;top:...px;">🌐 @Visual.Id</div>
}
@code {
    [Parameter, EditorRequired] public VbHttpClientVisual Visual { get; set; } = null!;
    [CascadingParameter] public bool IsDesignMode { get; set; }
}
```

`IsDesignMode` arriva come `CascadingValue` impostato una sola volta nel Form generato (letto da `NavigationManager.Uri`), non riletto da ogni componente.

**I riferimenti incrociati fra componenti funzionano già, senza plumbing aggiuntiva.** Sia i componenti visuali sia quelli non-visuali diventano campi della stessa partial class generata (`{Form}.razor.designer.cs` + `{Form}.Behavior.cs`, vincolo n.3): un handler come `Button1_Click` può chiamare direttamente `HttpClient1.GetAsync(...)`, perché sono entrambi campi della stessa classe. Non ovvio finché non lo si nota esplicitamente.

**Toolbox dinamica**: realizzata nel modulo 14 (sezione 2.2) — non piu' hardcoded in XAML.

**Esempi di componenti non-visuali candidati**, coerenti col vincolo n.6 (tutto client-side): `VbHttpClient` (implementato, chiamate a API esterne dalla WASM), `VbTimer` (implementato, di prova), `VbLocalStorage`/`VbIndexedDb` (wrapper su JS interop - non ancora implementati).

## 2.2. Componenti come plugin caricati a runtime da sorgente (2026-08-26)

Modulo 14. Decisione di design a lungo termine: i componenti — built-in e utente — sono descritti da **file C# in una cartella**, letti a runtime all'avvio dell'IDE, senza dover toccare né ricompilare `Ide.App`. Tutti i componenti (`VbButton`, `VbLabel`, `VbTextBox`, `VbHttpClient`) sono migrati a questo meccanismo: non esiste piu' un percorso "built-in" separato da uno "plugin utente", sono la stessa cosa.

**Il problema centrale**: un componente deve "vivere" in due runtime diversi — il processo desktop di `Ide.App` (per Toolbox/Property Grid/generatore) e l'app Blazor WASM reale mostrata nella WebView (vincolo n.1). La soluzione adottata sfrutta un'osservazione chiave: `Ide.App` non renderizza mai Blazor, gli serve solo la classe C# "aspetto" (`IDesignComponent`) per riflettervi sopra — il `.razor` serve solo al runtime reale.

- **Cartella dei componenti: `{ProjectDir}/Components/`** (oggi `templates/BlazorPwaTemplate/Components/`, non alla radice del repo — generalizza bene al giorno in cui l'IDE apre progetti arbitrari). Essendo dentro l'albero del progetto Blazor, `.cs` e `.razor` messi li' sono automaticamente inclusi dalla build standard (stessi glob impliciti di `Pages/`/`Shared/`): `dotnet watch` li ricompila senza alcun wiring MSBuild aggiuntivo.
- **Stesso identico file sorgente, compilato due volte** da due compilatori per due scopi diversi: il build normale del progetto (SDK Razor/C#) per il runtime reale; `Ide.Designer.ComponentPluginLoader` (Roslyn, in memoria) per il design-time di `Ide.App` — solo i `.cs`, mai i `.razor`.
- **`[ToolboxComponent(DisplayName, Icon, Category)]`** (`VbControls.Abstractions`): il "manifest" richiesto, come attributo sul codice stesso — niente file di manifest separato (coerente col vincolo n.4). Letto via reflection per popolare la Toolbox.
- **Convenzione di naming**: il tag del markup generato e' il nome della classe "Visual" senza il suffisso `Visual` (`VbButtonVisual` -> `<VbButton>`) — nessuna configurazione aggiuntiva da scrivere.
- **`ComponentPluginLoader`** (`src/Ide.Designer/ComponentPluginLoader.cs`): compila `Components/*.cs` con `Microsoft.CodeAnalysis.CSharp`, referenziando l'intera shared framework (`RuntimeEnvironment.GetRuntimeDirectory()`) piu' gli assembly gia' caricati nel processo (per garantire la stessa identita' di tipo di `IDesignComponent` ecc. - vedi sotto). Carica il risultato in un `AssemblyLoadContext` **collezionabile** che delega sempre la risoluzione dei riferimenti al contesto di default (`Load(AssemblyName) => null`): senza questo, i tipi caricati dinamicamente sarebbero copie distinte con identita' diversa, e `IsAssignableFrom`/i cast fallirebbero silenziosamente. Un file che non compila viene segnalato in Output e i suoi tipi esclusi — un plugin rotto non blocca l'IDE ne' gli altri componenti.
- **Comando "Reload Components"** (menu View): scarica il contesto precedente e ricompila da zero, per iterare su un componente senza riavviare l'IDE.
- **Resilienza**: il generatore di codice (`FormCodeGenerator`) non deve mai far crashare l'IDE per un tipo di proprieta' non ancora supportato — un errore di serializzazione si segnala in Output, non termina il processo.
- **Nota di fiducia**: caricare ed eseguire sorgente arbitrario da una cartella e' "trusted code execution" — accettabile perche' e' codice dell'utente sulla propria macchina (stesso modello delle macro VB6/VBA), non un plugin store di terze parti.

## 3. Struttura repository (fissata, non improvvisare nomi diversi)

```
/repo-root
  ARCHITECTURE.md
  TASKS.md
  /src
    /Ide.App               → Avalonia UI, entry point IDE
    /Ide.Designer           → logica designer, JS interop, generatore codice
    /VbControls              → classi base condivise (VisualComponentBase, NonVisualComponentBase)
    /VbControls.Abstractions → IDesignComponent, IVisualComponent, INonVisualComponent, ComponentBehavior<T>, attributi
  /templates
    /BlazorPwaTemplate       → template progetto Blazor WASM PWA
      /Components            → modulo 14: componenti come plugin (VbButton, VbLabel, VbTextBox, VbHttpClient, VbTimer, ...), compilati sia dal build normale sia da Ide.Designer via Roslyn
  /samples
    /HelloWorldApp           → primo progetto generato di prova
  IdeSolution.sln
```

## 4. Stack tecnologico (scelte chiuse)

- .NET 8 SDK (fissare versione esatta in `global.json`)
- Avalonia UI 11.x per l'IDE
- AvaloniaEdit per l'editor codice
- Microsoft.CodeAnalysis.CSharp (Roslyn, `4.14.0`) — parsing/diagnostica del codice Behavior, e dal modulo 14 anche compilazione in memoria dei componenti-plugin in `Components/` (`Ide.Designer.ComponentPluginLoader`)
- `System.Runtime.Loader.AssemblyLoadContext` collezionabile — carica/scarica i componenti-plugin senza riavviare l'IDE (comando "Reload Components")
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
    - **Linux, ambiente sandbox CI (Ubuntu 22.04, WebKitGTK 2.50.4, nessuna GPU)**: build OK; la navigazione verso un file HTML statico locale (`file://`) è stata confermata **funzionalmente** tramite gli eventi `NativeWebView.NavigationStarted`/`NavigationCompleted`. La verifica **visiva a pixel** non è stata possibile per mancanza di accesso GPU (`/dev/dri` non accessibile). Nello stesso sandbox, il caricamento del **vero progetto Blazor servito da `dotnet watch`** (Task 0.2) falliva con `TypeError: Load failed` durante il download di `dotnet.native.wasm` — causa isolata: fallimento del sandbox di rete di WebKitGTK tipico dei container/VM privi delle funzionalità kernel richieste (namespace/seccomp), non un problema del codice o della configurazione applicativa (confermato: il server rispondeva 200 con `Content-Length`/`Content-Type` corretti).
    - **Linux, macchina reale dell'utente (2026-08-25)**: **CONFERMATO end-to-end.** `dotnet watch` avviato su `templates/BlazorPwaTemplate` (porta fissa `5245`, vedi `Properties/launchSettings.json`), `Ide.App` avviato con `dotnet run`: la `NativeWebView` carica `http://localhost:5245/`, mostra la pagina reale con il componente `Shared/DesignerTestButton.razor` e il bottone risponde correttamente al click (contatore incrementa). Fase 0 per Linux considerata **chiusa**.
    - **Windows**: non verificato — validazione esplicitamente rimandata su decisione dell'utente (2026-08-25); si procede con l'implementazione assumendo che WebView2 si comporti secondo la documentazione ufficiale Microsoft/Avalonia. Da tenere presente come rischio aperto prima di un rilascio.
  - **Nota operativa (gotcha riscontrato)**: `dotnet watch` avvia anche un proprio *browser-refresh server* che prova a esporre un endpoint HTTPS interno; se manca il certificato di sviluppo fallisce con `Unable to configure HTTPS endpoint`. Soluzioni: `dotnet dev-certs https --trust`, oppure (più semplice, dato che qui la pagina è consumata dalla WebView e non da un browser con auto-refresh) avviare con `DOTNET_WATCH_SUPPRESS_BROWSER_REFRESH=1 dotnet watch --urls http://localhost:5245`.
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
13. Componenti non-visuali: `IDesignComponent`/`INonVisualComponent` (sezione 2.1) + primo componente di esempio (`VbHttpClient`) + Toolbox estesa via reflection
14. Componenti come plugin caricati a runtime (sezione 2.2): `ComponentPluginLoader` (Roslyn + `AssemblyLoadContext`), attributo `[ToolboxComponent]`, Toolbox dinamica, comando Reload Components, migrazione di tutti i componenti in `Components/`

## 6. Criteri generali di accettazione

Ogni modulo si considera completo solo se:
- `dotnet build` sulla solution intera passa senza errori
- Se il modulo produce un artefatto eseguibile/testabile, viene verificato manualmente (screenshot, log, o comando eseguito) e non solo "scritto"
- Non introduce dipendenze non elencate nello stack senza segnalarlo esplicitamente
- Rispetta la struttura repo e i vincoli architetturali della sezione 2
