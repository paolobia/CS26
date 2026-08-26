# Tutorial: costruire il visualizzatore RSS da zero nell'IDE

Questo esempio non è stato scritto a mano: è stato costruito trascinando componenti
nell'IDE, esattamente come faresti tu. Questo tutorial ripercorre gli stessi passi, così
puoi rifarlo — o modificarlo — direttamente nel designer.

Prerequisiti: aver letto "Come avviare l'IDE" nel [README](../../README.md) principale.

## 1. Avvia l'IDE

```bash
dotnet run --project src/Ide.App
```

L'IDE apre `templates/BlazorPwaTemplate` e mostra la Toolbox a sinistra con: HttpClient,
LocalStorage, Timer, Button, Label, TextArea, TextBox.

## 2. Trascina i controlli sulla superficie di design

Nell'ordine (l'ordine determina solo il nome generato — `TextBox1`, `TextBox2`, ... — non
il comportamento):

1. **TextBox** → sarà il campo URL del feed (`TextBox1`).
2. **Button** → il bottone "Carica ora" (`Button1`).
3. **TextBox** → mostrerà il titolo dell'ultimo articolo (`TextBox2`).
4. **TextArea** → mostrerà il contenuto dell'ultimo articolo (`TextArea1`).
5. **Timer** → farà scattare l'aggiornamento periodico (`Timer1`) — non visibile a
   runtime, solo un'icona a design-time (filosofia "tutto vive nel form", vedi
   ARCHITECTURE.md sezione 2.1).
6. **HttpClient** → farà la chiamata di rete (`HttpClient1`) — anche questo non-visuale.
7. **Label** → mostrerà lo stato dell'ultimo aggiornamento (`Label1`).

Ogni trascinamento rigenera subito `DesignerForm.razor` + `.razor.designer.cs` e fa
ripartire `dotnet watch` sul progetto: guarda il pannello Output per seguire la
ricompilazione.

## 3. Imposta le proprietà con la Property Grid

Seleziona ogni controllo nell'elenco (sopra la Property Grid) e imposta:

| Controllo | Proprietà | Valore |
|---|---|---|
| `TextBox1` | Text | un URL RSS, es. `https://feeds.bbci.co.uk/news/rss.xml` |
| `TextBox1` | Placeholder | `URL del feed RSS` |
| `Button1` | Text | `Carica ora` |
| `TextBox2` | Placeholder | `Titolo ultimo articolo` |
| `TextBox2` | ReadOnly | ✓ (sola lettura: lo aggiorna solo il codice, non l'utente) |
| `TextArea1` | Placeholder | `Contenuto ultimo articolo` |
| `TextArea1` | ReadOnly | ✓ |
| `Timer1` | IntervalMs | `60000` (60 secondi) |
| `Label1` | Text | `In attesa del primo aggiornamento...` |

Ogni modifica rigenera il form: se sbagli un valore (es. una lettera in un campo
numerico) l'IDE lo segnala in Output invece di bloccarsi.

## 4. Genera gli handler degli eventi

Doppio click su **Button1**: genera in `DesignerForm.Behavior.cs` uno stub
`private void Button1_Click() { }` e lo collega nel markup generato.

Doppio click su **Timer1**: genera uno stub `private async Task Timer1_Tick() { }` e lo
collega in codice (`Timer1.Tick += Timer1_Tick;`) — un timer usa un vero evento .NET, non
un parametro Blazor, quindi il designer lo cablatura diversamente da un bottone
(vedi `Ide.Designer.ComponentEventInfo` se sei curioso del meccanismo).

## 5. Scrivi la logica in `DesignerForm.Behavior.cs`

Questo è l'unico file che scrivi tu a mano — il designer lo crea ma non lo tocca più una
volta che esiste. La logica completa:

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace BlazorPwaTemplate.Pages;

public partial class DesignerForm
{
    private void Button1_Click() => _ = RefreshFeedAsync();

    private async Task Timer1_Tick() => await RefreshFeedAsync();

    private async Task RefreshFeedAsync()
    {
        var url = TextBox1.Text;
        if (string.IsNullOrWhiteSpace(url))
        {
            Label1.Text = "Inserisci un URL nella text box.";
            StateHasChanged();
            return;
        }

        try
        {
            var xml = await HttpClient1.GetStringAsync(url);
            var (title, content) = ParseLatestItem(xml);
            TextBox2.Text = title;
            TextArea1.Text = content;
            Label1.Text = $"Aggiornato alle {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            Label1.Text = $"Errore durante il caricamento: {ex.Message}";
        }

        StateHasChanged();
    }

    private static (string Title, string Content) ParseLatestItem(string xml)
    {
        var root = XDocument.Parse(xml).Root;
        if (root is null)
            return ("(feed vuoto)", "");

        var item = root.Descendants().FirstOrDefault(e => e.Name.LocalName == "item");
        if (item is not null)
        {
            var title = item.Elements().FirstOrDefault(e => e.Name.LocalName == "title")?.Value ?? "";
            var body = item.Elements().FirstOrDefault(e => e.Name.LocalName == "description")?.Value ?? "";
            return (title.Trim(), body.Trim());
        }

        var entry = root.Descendants().FirstOrDefault(e => e.Name.LocalName == "entry");
        if (entry is not null)
        {
            var title = entry.Elements().FirstOrDefault(e => e.Name.LocalName == "title")?.Value ?? "";
            var body = entry.Elements().FirstOrDefault(e => e.Name.LocalName is "summary" or "content")?.Value ?? "";
            return (title.Trim(), body.Trim());
        }

        return ("(formato feed non riconosciuto)", "");
    }
}
```

Punti da notare:

- `HttpClient1.GetStringAsync(url)` e `TextBox1.Text`/`TextBox2.Text`/`TextArea1.Text`/
  `Label1.Text` sono tutti campi della stessa classe generata: un controllo può leggere
  e scrivere liberamente le proprietà di un altro, senza plumbing aggiuntivo (vedi
  ARCHITECTURE.md sezione 2.1, "i riferimenti incrociati funzionano già").
- `ParseLatestItem` riconosce sia RSS 2.0 (`<item>`) sia Atom (`<entry>`) confrontando
  solo il nome locale dell'elemento — sufficiente per un esempio semplice, non un parser
  RSS completo.
- `StateHasChanged()` è necessario perché l'aggiornamento arriva da un timer, non da
  un'interazione utente diretta: senza, Blazor non saprebbe di dover ridisegnare.

## 6. Premi F5

Menu **Run > Start**: l'app vera parte nella WebView, cliccabile e funzionante, non
un'anteprima statica.

## 7. Rendilo un progetto autonomo (opzionale)

Il form che hai appena costruito vive dentro `templates/BlazorPwaTemplate` (l'unico
progetto che l'IDE sa disegnare oggi). Per trasformarlo in un'app distribuibile per conto
suo — come `samples/RssFeedViewer` — copia l'intero progetto template in una nuova
cartella sotto `samples/`, rinomina il progetto/namespace, e sposta i tre file generati
(`DesignerForm.razor`, `.razor.designer.cs`, `.Behavior.cs`) in `Pages/Home.razor` (con
`@page "/"`) così l'app parte direttamente sulla tua pagina invece che su `/designerform`.
È esattamente il procedimento seguito per creare questo esempio.

## Nota sui feed RSS e CORS

Un feed che funziona da riga di comando (`curl`) può fallire nel browser con un errore di
rete generico: la maggior parte dei server RSS non imposta le intestazioni
`Access-Control-Allow-Origin` necessarie perché un `HttpClient` dentro Blazor WASM (che
gira nel browser) possa leggerli. Non è un bug di questo codice — è una regola di
sicurezza del browser che si applica a qualunque `fetch()` cross-origin. Per provare con
feed diversi, cerca uno che dichiari esplicitamente il supporto CORS, oppure usa un proxy
CORS server-side.
