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
- WebView: **da validare in Fase 0** — opzioni: `WebView.Avalonia`, CEF via `CefNet`, o wrapper custom su WebView2 (Windows)/WebKitGTK (Linux). Non assumere che una funzioni su entrambe le piattaforme senza test.
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
