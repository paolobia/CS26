# Ide.App — RAD IDE per Blazor WebAssembly PWA

Grazie per aver scaricato Ide.App. Questo pacchetto contiene l'IDE già compilato,
pronto da avviare — non serve clonare nulla né compilare l'IDE stesso.

## Avvio rapido

```
{{EXE_NAME}}
```

(su Windows, doppio click su `{{EXE_NAME}}` funziona allo stesso modo).

L'IDE apre `templates/BlazorPwaTemplate` (incluso in questo pacchetto): trascina un
componente dalla Toolbox a sinistra sulla superficie di design, impostane le proprietà
nella Property Grid a destra, fai doppio click per generare l'handler di un evento, poi
**Run > Start (F5)** per vedere l'app vera girare dentro la finestra dell'IDE.

## Prerequisiti sulla tua macchina

L'eseguibile dell'IDE è **self-contained**: non serve installare .NET per farlo partire.
Serve però che sulla macchina sia installato:

- **.NET 8 SDK** — necessario perché l'IDE compila ed esegue live (`dotnet watch`) il
  vero progetto Blazor che stai disegnando: è il modo in cui F5 mostra l'app reale
  invece di un'anteprima statica. Scaricabile da https://dotnet.microsoft.com/download
- **Linux**: WebKitGTK (`libwebkit2gtk-4.0-37` o `4.1` a seconda della distro) — serve
  alla WebView incorporata nell'IDE. Di solito già presente su desktop Linux moderni; se
  manca, il gestore pacchetti della tua distro la installa in un comando.
- **Windows**: runtime **WebView2** — preinstallato di default su Windows 10 21H2+ e
  Windows 11. Su versioni precedenti va scaricato da Microsoft (bootstrapper Evergreen).

## Cosa trovi in questo pacchetto

```
{{EXE_NAME}}                      → l'IDE, pronto all'uso
[altre .dll/.pdb]                 → runtime e librerie dell'IDE — non toccare
templates/BlazorPwaTemplate/      → il progetto che l'IDE disegna (apri qui i tuoi form)
samples/HelloWorldApp/            → primo esempio minimo
samples/RssFeedViewer/            → esempio completo funzionante (visualizzatore RSS)
                                     con TUTORIAL.md — i passi per ricostruirlo/
                                     modificarlo direttamente nel designer
src/VbControls*/                  → classi base condivise dai componenti (sorgente:
                                     serve a dotnet watch/build per compilare i progetti
                                     sopra, non va eseguito né modificato direttamente)
ARCHITECTURE.md                   → documentazione completa del progetto e delle scelte
README.md                         → questo file
```

## Provare subito l'esempio RSS Feed Viewer

Senza nemmeno aprire l'IDE, in un terminale:

```
cd samples/RssFeedViewer
dotnet watch
```

Apri `http://localhost:5246` nel browser. Nota: alcuni feed RSS falliscono per un
limite CORS del server che li pubblica, non un problema di questo codice — prova un
altro URL se il primo non risponde.

## Per approfondire

`ARCHITECTURE.md`, incluso in questo pacchetto, spiega l'intera progettazione: perché
i componenti "vivono" nel form anche quando non sono visuali (filosofia stile
C++Builder), come funziona il sistema di componenti-plugin caricati a runtime, come
generatore di codice e Property Grid si parlano, e la lista completa dei moduli
implementati.
