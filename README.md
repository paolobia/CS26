# Ide.App — RAD IDE per Blazor WebAssembly PWA

Un IDE visuale in stile VB6/C++Builder per creare applicazioni Blazor WebAssembly PWA:
si trascinano componenti su un form, si impostano le proprietà da una Property Grid, si
fa doppio click per generare gli handler degli eventi, e si preme F5 per vedere l'app
vera (non un'anteprima) girare dentro una WebView nativa incorporata nell'IDE.

Per la progettazione completa (perché queste scelte, come funziona il generatore di
codice, l'architettura a plugin dei componenti, ecc.) vedi **[ARCHITECTURE.md](ARCHITECTURE.md)**.
Questo README copre solo "cosa serve" e "come si prova".

## Download rapido (consigliato — nessuna compilazione necessaria)

**[⬇️ Scarica l'ultima release](https://github.com/paolobia/CS26/releases/latest)** —
pacchetto già compilato per Linux o Windows: estrai lo zip e avvia l'eseguibile incluso.
Contiene l'IDE, il progetto template, gli esempi e la documentazione. Vedi il README
dentro lo zip per i dettagli. Le sezioni sotto servono solo se vuoi compilare da sorgente
(per contribuire, o per verificare/modificare il codice dell'IDE stesso).

## Cosa serve (prerequisiti)

- **.NET 8 SDK** (versione fissata in `global.json`).
- **Linux**: WebKitGTK installato — pacchetto `libwebkit2gtk-4.0-37` o `4.1` a seconda
  della distro/versione (es. Ubuntu 22.04 usa la serie `4.0`). Senza questo la WebView
  embedded nell'IDE non parte.
- **Windows**: runtime **WebView2** — preinstallato di default su Windows 10 21H2+ e
  Windows 11. Su versioni precedenti va installato il bootstrapper Evergreen Microsoft.
- Non serve altro: l'IDE non richiede Node.js, npm o toolchain JS — Blazor WASM è puro
  .NET.

## Come compilare

Dalla radice del repo:

```bash
dotnet build IdeSolution.sln
```

Compila tutto: l'IDE (`Ide.App`), le librerie di supporto (`VbControls*`, `Ide.Designer`),
il progetto template (`templates/BlazorPwaTemplate`) e gli esempi in `samples/`.

## Come avviare l'IDE

```bash
dotnet run --project src/Ide.App
```

All'avvio l'IDE apre `templates/BlazorPwaTemplate` (oggi è l'unico progetto che sa
disegnare — l'apertura di progetti Blazor arbitrari è una direzione futura, vedi
ARCHITECTURE.md) e mostra:

- **Toolbox** (sinistra) — i componenti disponibili, scoperti a runtime leggendo i file
  `.cs` in `templates/BlazorPwaTemplate/Components/` (architettura a plugin, modulo 14:
  aggiungere un componente significa scrivere due file lì dentro, non toccare l'IDE).
- **Superficie di design** (centro) — trascina qui un componente dalla Toolbox.
- **Property Grid** (destra) — seleziona un controllo piazzato per editarne le proprietà
  marcate `[VisualProperty]`.
- **Output** (in basso) — log dell'IDE, di `dotnet watch` e (in modalità Debug) della
  console del browser.

Doppio click su un controllo genera lo stub del suo evento principale (`OnClick` per un
bottone, `Tick` per un timer, ...) in `DesignerForm.Behavior.cs` — il file che scrivi tu,
mai sovrascritto dal designer. Menu **Run > Start (F5)** compila ed esegue l'app vera
nella WebView; **Build > Publish PWA** produce un pacchetto pubblicabile in `bin/PwaPublish`.

## Come provare l'esempio RSS Feed Viewer

`samples/RssFeedViewer` è un'app completa, generata con l'IDE seguendo esattamente il
flusso sopra, poi resa eseguibile in autonomia. È il modo più rapido per vedere l'IDE
"in produzione" senza doverci lavorare dentro tu stesso. Per il tutorial passo-passo su
come è stata costruita (e per rifarla da zero tu stesso nell'IDE) vedi
**[samples/RssFeedViewer/TUTORIAL.md](samples/RssFeedViewer/TUTORIAL.md)**.

Per avviarla:

```bash
cd samples/RssFeedViewer
dotnet watch
```

Apri **http://localhost:5246**: c'è una text box con un URL RSS già precompilato, un
bottone "Carica ora" e sotto titolo/contenuto dell'ultimo articolo (in sola lettura),
aggiornati automaticamente ogni 60 secondi da un `VbTimer`.

**Nota sui feed RSS**: molti server RSS non impostano le intestazioni CORS necessarie
per essere letti da un browser — è un limite del server, non un bug di questo codice. Se
un feed non carica, prova un altro URL o un feed che sai imposta
`Access-Control-Allow-Origin`.

## Come creare un pacchetto distribuibile (Linux / Windows)

```bash
# Linux (self-contained: non serve .NET installato sulla macchina di destinazione,
# solo WebKitGTK)
./scripts/publish-linux.sh

# Windows (compilabile anche da Linux via cross-publish; da eseguire e verificare su
# una macchina Windows reale — vedi nota sotto)
pwsh ./scripts/publish-windows.ps1
# oppure, senza PowerShell:
dotnet publish src/Ide.App/Ide.App.csproj -c Release -r win-x64 --self-contained true -o dist/win-x64
```

L'output è una **cartella** (non un singolo `.exe`/installer con wizard): l'app è
self-contained (nessun .NET richiesto sulla macchina di destinazione), ma di proposito
*non* usa la modalità single-file, perché il caricamento dei componenti-plugin
(`ComponentPluginLoader`) cerca `VbControls.dll`/`VbControls.Abstractions.dll` come file
fisici accanto all'eseguibile — in single-file verrebbero incorporati nell'exe e
sparirebbero da disco, rompendo silenziosamente la Toolbox. Per distribuire, zippa la
cartella `dist/<rid>/` così com'è.

- **Linux**: pubblicato e **verificato in esecuzione** in questo ambiente di sviluppo —
  si avvia, carica la Toolbox con tutti i componenti, funziona.
- **Windows**: la compilazione cross-platform è stata verificata (produce
  `dist/win-x64/Ide.App.exe` senza errori), ma **l'esecuzione va provata su una macchina
  Windows reale** — questo ambiente di sviluppo è Linux e non può avviare un `.exe`
  Windows. Se qualcosa non parte, il primo sospetto è il runtime WebView2 (vedi
  prerequisiti sopra).

## Struttura del repo

Vedi [ARCHITECTURE.md](ARCHITECTURE.md) sezione 3 per la struttura completa e motivata.
In breve:

- `src/Ide.App` — l'IDE (Avalonia UI).
- `src/Ide.Designer` — generatore di codice, `dotnet watch` host, caricatore di plugin.
- `src/VbControls*` — classi base condivise dal modello a componenti.
- `templates/BlazorPwaTemplate` — il progetto Blazor che l'IDE disegna; `Components/`
  al suo interno è la cartella dei componenti-plugin (Toolbox dinamica).
- `samples/` — app di esempio generate con l'IDE, eseguibili in autonomia.
- `scripts/` — script di pubblicazione per la distribuzione.

## Stato del progetto

Tutti i 15 moduli descritti in ARCHITECTURE.md sezione 5 sono implementati e verificati
(build pulita, test automatici dove applicabile, verifica manuale del flusso IDE). I
limiti noti (validazione Windows rimandata all'utente, un limite di rete del sandbox di
sviluppo che non riguarda macchine reali) sono documentati in ARCHITECTURE.md sezione 4.
