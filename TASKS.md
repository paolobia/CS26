# TASKS.md — Checklist incrementale per Claude Code

> Regola d'uso: dai a Claude Code UN task alla volta (copia/incolla il blocco), verifica il criterio di accettazione, fai commit, poi passa al successivo. Non incollare più task insieme. Se un task fallisce il criterio di accettazione, fermati e correggi prima di proseguire.

---

## Fase 0 — Validazione ipotesi rischiose

### Task 0.1 — Setup solution vuota
**Prompt:**
> Leggi ARCHITECTURE.md. Crea la struttura repo descritta nella sezione 3: la solution `IdeSolution.sln` con i progetti `Ide.App` (Avalonia, template applicazione vuota), `VbControls.Abstractions` (class library), `VbControls` (class library, riferisce Abstractions), e un progetto `templates/BlazorPwaTemplate` creato con `dotnet new blazorwasm -o templates/BlazorPwaTemplate --pwa`. Aggiungi un `global.json` che fissa la versione .NET SDK. Verifica che `dotnet build` sull'intera solution passi. Non implementare ancora nessuna logica.

**Criterio di accettazione:** `dotnet build` passa su tutta la solution; struttura cartelle conforme alla sezione 3 di ARCHITECTURE.md.

### Task 0.2 — Validazione WebView su Linux e Windows
**Prompt:**
> Nel progetto Ide.App, aggiungi una WebView embedded (valuta WebView.Avalonia o CefNet, scegli motivando la scelta) e verifica che riesca a caricare una pagina locale semplice (anche solo un file HTML statico) sia in ambiente Linux che Windows. Documenta in ARCHITECTURE.md sotto "Stack tecnologico" quale libreria hai scelto e eventuali dipendenze di sistema necessarie (es. pacchetti WebKitGTK su Linux).

**Criterio di accettazione:** finestra Avalonia che mostra una pagina HTML dentro la WebView, testata su almeno una piattaforma, con dipendenze documentate.

### Task 0.3 — PoC end-to-end minimale
**Prompt:**
> Nel progetto templates/BlazorPwaTemplate, aggiungi manualmente un componente Blazor con un bottone. Avvia `dotnet watch` su quel progetto e verifica che la WebView creata nel Task 0.2 riesca a caricare `http://localhost:PORT` e mostrare il bottone reale, cliccabile.

**Criterio di accettazione:** il bottone Blazor reale è visibile e funzionante dentro la WebView dell'IDE.

---

## Fase 1 — Modello a oggetti (VbControls.Abstractions)

### Task 1.1 — Interfacce base
**Prompt:**
> Leggi ARCHITECTURE.md sezione 2 punto 2. In VbControls.Abstractions implementa `IVisualComponent` (Id, LayoutBox, StyleModel, Properties), `ComponentBehavior<T>` (classe astratta con OnInitAsync, OnClickAsync), e l'attributo `[VisualProperty(string Category)]` da usare su proprietà pubbliche. Aggiungi test unitari minimi che verificano che l'attributo sia leggibile via reflection.

**Criterio di accettazione:** `dotnet test` passa; le classi compilano e sono usabili da un progetto Blazor separato.

### Task 1.2 — Primo controllo: VbButton
**Prompt:**
> In VbControls, crea `VbButton.razor` + `VbButton.razor.cs` che implementa `IVisualComponent`, espone `Text` e `BackgroundColor` con `[VisualProperty]`, e inoltra il click a `Behavior.OnClickAsync()` se un Behavior è assegnato. Crea un piccolo progetto di test in /samples che lo usa manualmente (senza designer) per verificare che funzioni a runtime.

**Criterio di accettazione:** il sample compila, gira, il bottone risponde al click invocando il Behavior.

---

## Fase 2 — Designer minimo (drag&drop + property grid)

### Task 2.1 — Overlay di selezione via JS interop
**Prompt:**
> [dettaglio da scrivere insieme quando arrivi qui — dipende dagli esiti di Fase 0 e 1]

### Task 2.2 — Property Grid via reflection
**Prompt:**
> [da definire]

### Task 2.3 — Generatore di codice: scrittura .razor + .razor.designer.cs
**Prompt:**
> [da definire]

---

## Fase 3+ — Event editor, Run/F5, Debug, Publish

Da dettagliare a valle della Fase 2, quando le decisioni prese in Fase 0-1 (in particolare la libreria WebView e il meccanismo di JS interop) sono consolidate. Non anticipare questi task: le scelte fatte prima influenzano troppo la forma di questi moduli.

---

## Note d'uso

- Ogni volta che completi un task, fai **commit separato** con messaggio che riferisce il numero del task (es. `Task 0.1: setup solution`).
- Se Claude Code propone una deviazione dai vincoli di ARCHITECTURE.md, chiedigli esplicitamente di motivarla prima di accettarla, e valuta se aggiornare il documento invece di lasciare un'incoerenza silenziosa.
- Aggiorna questo file mano a mano: quando completi una fase, scrivi in dettaglio i task della fase successiva (come mostrato per la Fase 2), invece di scriverli tutti in anticipo — le decisioni tecniche a monte cambiano cosa ha senso a valle.
